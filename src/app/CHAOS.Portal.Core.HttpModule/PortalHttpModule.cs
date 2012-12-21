using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Xml.Linq;
using CHAOS.Extensions;
using CHAOS.Index.Solr;
using CHAOS.Portal.Data.EF;
using CHAOS.Portal.Exception;
using Chaos.Portal;
using Chaos.Portal.Cache.Couchbase;
using Chaos.Portal.Data.Dto.Standard;
using Chaos.Portal.Extension;
using Chaos.Portal.Logging.Database;
using Chaos.Portal.Request;
using Chaos.Portal.Response;
using Chaos.Portal.Standard;
using Chaos.Portal.Logging;

namespace CHAOS.Portal.Core.HttpModule
{
    public class PortalHttpModule : IHttpModule
	{
		#region Delegates

        private delegate void AssemblyLoaderDelegate( IPortalApplication application, IExtension extension, PortalExtensionAttribute attribute );

		#endregion
        #region Fields

        protected string    ServiceDirectory  = ConfigurationManager.AppSettings["ServiceDirectory"];
        protected UUID      AnonymousUserGuid = new UUID( ConfigurationManager.AppSettings["AnonymousUserGUID"] );
        protected LogLevel? _logLevel;

        #endregion
        #region Properties

        protected IPortalApplication PortalApplication { get; set; }

        protected LogLevel LogLevel
        {
            get
            {
                if (!_logLevel.HasValue)
                {
                    var logLevel = ConfigurationManager.AppSettings["LOG_LEVEL"];

                    _logLevel = (LogLevel)Enum.Parse(typeof(LogLevel), logLevel ?? "Info");
                }

                return _logLevel.Value;
            }
        }

        #endregion
        #region IHttpModule Members

        public void Dispose()
        {
            //clean-up code here.
        }

        public void Init( HttpApplication context )
        {
            // REVIEW: Look into moving the loading process out of the http module
            if( context.Application["PortalApplication"] == null )
            {
                lock( context.Application )
                {
                    if( context.Application["PortalApplication"] == null )
                    {
                        //Log = new DatabaseLogger( string.Format("{0}/{1}", PortalRequest.Extension, PortalRequest.Action ), GetSessionFromDatabase() != null ? GetSessionFromDatabase().GUID : null, LogLevel ); // TODO: LogLevel should be set in config
                        
                        var portalRepository = new PortalRepository().WithConfiguration(ConfigurationManager.ConnectionStrings["PortalEntities"].ConnectionString);
                        var cache            = new Cache();
                        var index            = new SolrCoreManager();
                        var log              = new DatabaseLogger("tmp", null);

                        PortalApplication = new PortalApplication( cache, index, portalRepository, log );

						//AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

						LoadTypesFromAssembliesAt<IExtension>( string.Format( "{0}\\Extensions", ServiceDirectory ), PortalApplication, LoadExtensions );

						context.Application["PortalApplication"] = PortalApplication;
                    }
                }
            }

			PortalApplication = (PortalApplication)context.Application["PortalApplication"];
            context.BeginRequest += ContextBeginRequest;
        }

        private static void LoadTypesFromAssembliesAt<T>( string path, IPortalApplication application, AssemblyLoaderDelegate loadAssembly )
		{
			foreach (var assembly in System.IO.Directory.GetFiles( path, "*.dll" ).Select(Assembly.LoadFile))
			{
				foreach (var type in GetClassesOf<T>(from: assembly))
				{
					var attribute = type.GetCustomAttribute<PortalExtensionAttribute>(true);
					var obj       = assembly.CreateInstance( type.FullName );

					loadAssembly(application, (IExtension)obj, attribute );
				}
			}
		}

        //Assembly CurrentDomain_AssemblyResolve( object sender, ResolveEventArgs args )
        //{
        //    if( PortalApplication.LoadedAssemblies.ContainsKey( args.Name ) )
        //        return PortalApplication.LoadedAssemblies[ args.Name ];
			
        //    throw new AssemblyNotLoadedException( string.Format( "The assembly {0} is not loaded", args.Name ));
        //}

	    private static IEnumerable<Type> GetClassesOf<T>(Assembly from)
	    {
	        try
	        {
                return from.GetTypes().Where(type => type.IsClass && type.Implements<T>() && !type.IsAbstract);
	        }
            catch (ReflectionTypeLoadException e)
	        {
	            throw e.LoaderExceptions[0];
	        }
	    }

	    private static void LoadExtensions( IPortalApplication application, IExtension extension, PortalExtensionAttribute configurationAttribute )
        {
			var attribute = configurationAttribute;

            if (attribute != null)
            {
                using (var db = new PortalEntities())
                {
                    var config = db.Module_Get(null, attribute.ConfigurationName).FirstOrDefault();

                    if (config == null)
                        throw new ModuleConfigurationMissingException(string.Format("The module requires a configuration, but none was found with the name: {0}", attribute.ConfigurationName));

                    extension.WithPortalApplication(application)
                             .WithConfiguration(config.Configuration);

                    var indexSettings = db.IndexSettings_Get((int?)config.ID).FirstOrDefault();

                    if (indexSettings != null)
                    {
                        foreach (var url in XElement.Parse(indexSettings.Settings).Elements("Core").Select(core => core.Attribute("url").Value))
                        {
                            application.IndexManager.AddIndex(extension.GetType().FullName, new SolrCoreConnection(url));
                        }
                    }
                }
            }

            application.LoadedExtensions.Add( attribute != null && !string.IsNullOrEmpty( attribute.Name ) ? attribute.Name : extension.GetType().Name, extension );
        }

        #endregion
        #region Business Logic

        private void ContextBeginRequest(object sender, EventArgs e)
        {
            using (var application = (HttpApplication) sender)
            {
                if (IsOnIgnoreList(application.Request.Url)) return; // TODO: 404

                var request  = CreatePortalRequest(application.Request);
                var response = new PortalResponse(new PortalHeader(request.Stopwatch), new PortalResult(), new PortalError());

                try
                {
                    PortalApplication.ProcessRequest(request, response);
                    
                    application.Response.ContentEncoding = System.Text.Encoding.UTF8;
                    application.Response.ContentType     = GetContentType(response.Header.ReturnFormat);
                    application.Response.Charset         = "utf-8";
                    application.Response.CacheControl    = "no-cache";

                    application.Response.AddHeader("Access-Control-Allow-Origin", "*");

                    SetCompression(application);
                }
			    catch (System.Exception ex)
			    {
			        response.Error.SetException(ex);
			    //    Log.Fatal("ProcessRequest() - Unhandeled exception occured during", e);
			    }

                using (var inputStream = response.GetResponseStream())
                using (var outputStream = application.Response.OutputStream)
                {
                    inputStream.Position = 0;
                    inputStream.CopyTo(outputStream);
                }

                application.Response.End();
            }
        }
        

        private static void SetCompression(HttpApplication application)
        {
            var acceptEncoding = HttpContext.Current.Request.Headers["Accept-Encoding"];

            if (!string.IsNullOrEmpty(acceptEncoding) && (acceptEncoding.Contains("gzip") || acceptEncoding.Contains("deflate")))
            {
                if (acceptEncoding.Contains("gzip"))
                {
                    application.Response.AppendHeader("Content-Encoding", "gzip");
                    application.Response.Filter = new System.IO.Compression.GZipStream(application.Response.Filter, System.IO.Compression.CompressionMode.Compress);
                }
                else
                {
                    application.Response.AppendHeader("Content-Encoding", "deflate");
                    application.Response.Filter = new System.IO.Compression.DeflateStream(application.Response.Filter, System.IO.Compression.CompressionMode.Compress);
                }
            }
        }

        /// <summary>
        /// Get the http response content type based on the return format requested 
        /// </summary>
        /// <param name="callContext"></param>
        /// <returns></returns>
        private string GetContentType( ReturnFormat format )
        {
            // TODO: Should validate when request is received, not after it's done processing
            switch(format)
            {
                case ReturnFormat.XML:
                    return "text/xml";
                case ReturnFormat.JSON:
                    return "application/json";
                case ReturnFormat.JSONP:
                    return "application/javascript";
                default:
                    throw new NotImplementedException( "Unknown return format" ); 
            }
        }

        /// <summary>
        /// Determine if the requested resource should be ignored
        /// </summary>
        /// <param name="absolutePath"></param>
        /// <returns></returns>
        private bool IsOnIgnoreList( Uri uri )
        {
            if(uri.AbsolutePath.EndsWith( "favicon.ico" )) return true;

            // TODO: other resources that should be ignored

            return false;
        }

        /// <summary>
        /// Creates a CallContext based on the HttpRequest
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        protected IPortalRequest CreatePortalRequest( HttpRequest request )
        {
            var extension = request.Url.Segments[request.Url.Segments.Length - 2].Trim( '/' );
            var action    = request.Url.Segments[request.Url.Segments.Length - 1].Trim( '/' );
	        
            switch( request.HttpMethod )
            {
                case "DELETE":
                case "PUT":
                case "POST":    
                        var files = request.Files.AllKeys.Select(key => request.Files[key]).Select(file => new FileStream(file.InputStream, file.FileName, file.ContentType, file.ContentLength)).ToList();

                        return new PortalRequest( extension, action, ConvertToIDictionary( request.Form ), files );
                case "GET":
                        return new PortalRequest( extension, action, ConvertToIDictionary( request.QueryString ) );
                default:
                    throw new UnhandledException( "Unknown Http Method" );
            }
        }

        /// <summary>
        /// Converts a NameValueCollection to a IDictionary
        /// </summary>
        /// <param name="nameValueCollection"></param>
        /// <returns></returns>
        private static IDictionary<string, string> ConvertToIDictionary( NameValueCollection nameValueCollection )
        {
            var parameters = new Dictionary<string, string>();
            
			for( var i = 0; i < nameValueCollection.Keys.Count; i++ )
            {
                parameters.Add( nameValueCollection.Keys[i], nameValueCollection[i]);
            }

            return parameters;
        }

        #endregion
    }
}
