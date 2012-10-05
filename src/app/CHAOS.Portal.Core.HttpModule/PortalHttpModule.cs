using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Xml.Linq;
using CHAOS.Extensions;
using CHAOS.Index.Solr;
using CHAOS.Portal.Core.Module;
using CHAOS.Portal.Core.Request;
using CHAOS.Portal.Core.Standard;
using CHAOS.Portal.Data.EF;
using CHAOS.Portal.Exception;
using CHAOS.Portal.Core.Extension;

namespace CHAOS.Portal.Core.HttpModule
{
    public class PortalHttpModule : IHttpModule
	{
		#region Delegates

		private delegate void AssemblyLoaderDelegate( PortalApplication application, object obj, PrettyNameAttribute attribute );

		#endregion
		#region Properties

		protected PortalApplication PortalApplication { get; set; }

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
						PortalApplication = new PortalApplication(new Cache.Membase.Membase(), new SolrCoreManager());
	                    
						AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

						LoadTypesFromAssembliesAt<IModule>( string.Format( "{0}\\Modules", PortalApplication.ServiceDirectory ), PortalApplication, LoadModules);
						LoadTypesFromAssembliesAt<IExtension>( string.Format( "{0}\\Extensions", PortalApplication.ServiceDirectory ), PortalApplication, LoadExtensions );

						context.Application["PortalApplication"] = PortalApplication;
                    }
                }
            }

			PortalApplication = (PortalApplication)context.Application["PortalApplication"];
            context.BeginRequest += ContextBeginRequest;
        }
		
	    private static void LoadTypesFromAssembliesAt<T>( string path, PortalApplication application, AssemblyLoaderDelegate loadAssembly )
		{
			foreach (var assembly in System.IO.Directory.GetFiles( path, "*.dll" ).Select(Assembly.LoadFile))
			{
				application.LoadedAssemblies.Add(assembly.FullName, assembly);

				foreach (var type in GetClassesOf<T>(from: assembly))
				{
					var attribute = type.GetCustomAttribute<PrettyNameAttribute>(true);
					var obj       = assembly.CreateInstance( type.FullName );

					loadAssembly(application, obj, attribute );
				}
			}
		}

	    Assembly CurrentDomain_AssemblyResolve( object sender, ResolveEventArgs args )
		{
			if( PortalApplication.LoadedAssemblies.ContainsKey( args.Name ) )
				return PortalApplication.LoadedAssemblies[ args.Name ];
			
			throw new AssemblyNotLoadedException( string.Format( "The assembly {0} is not loaded", args.Name ));
		}

    	private static void LoadModules( PortalApplication application, object obj, PrettyNameAttribute prettyNameAttribute )
    	{
    		var attribute = (ModuleAttribute) prettyNameAttribute;
    		var module    = (IModule)obj;

            // If an attribute is present on the class, load config from database
            if( attribute != null )
            {
				using (var db = new PortalEntities())
				{
					var moduleConfig = db.Module_Get(null, attribute.ModuleConfigName).FirstOrDefault();

					if (moduleConfig == null)
						throw new ModuleConfigurationMissingException(string.Format("The module requires a configuration, but none was found with the name: {0}", attribute.ModuleConfigName));

					module.Initialize(moduleConfig.Configuration);

					var indexSettings = db.IndexSettings_Get((int?)moduleConfig.ID).FirstOrDefault();

					if (indexSettings != null)
					{
						foreach (var url in XElement.Parse(indexSettings.Settings).Elements("Core").Select(core => core.Attribute("url").Value))
						{
							application.IndexManager.AddIndex(module.GetType().FullName, new SolrCoreConnection(url));
						}
					}
				}
            }

            // Index modules by the Extensions they subscribe to
            foreach( var method in module.GetType().GetMethods() )
            {
                foreach( Datatype datatypeAttribute in method.GetCustomAttributes( typeof( Datatype ), true ) )
                {
                    if( !application.LoadedModules.ContainsKey( datatypeAttribute.ExtensionName ) )
                        application.LoadedModules.Add( datatypeAttribute.ExtensionName, new Collection<IModule>() );

                    if( !application.LoadedModules[ datatypeAttribute.ExtensionName ].Contains( module ) )
                        application.LoadedModules[ datatypeAttribute.ExtensionName ].Add( module );
                }
            }
        }

	    private static IEnumerable<Type> GetClassesOf<T>(Assembly from)
	    {
			return from.GetTypes().Where(type => type.IsClass && type.Implements<T>());
	    }

	    private static void LoadExtensions( PortalApplication application, object obj, PrettyNameAttribute prettyNameAttribute )
        {
			var attribute = (ExtensionAttribute) prettyNameAttribute;
    		var extension = (IExtension)obj;

			application.LoadedExtensions.Add(attribute == null ? extension.GetType().Name : attribute.ExtensionName, extension);
        }

        #endregion
        #region Business Logic

        private void ContextBeginRequest( object sender, EventArgs e )
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            using( var application = (HttpApplication) sender )
            {
                if( IsOnIgnoreList( application.Request.Url.AbsolutePath ) )
                    return; // TODO: 404

                var callContext = CreateCallContext( application.Request );
                callContext.Log.Debug(string.Format("{0} CallContext Created",sw.Elapsed));
                PortalApplication.ProcessRequest(callContext);
                callContext.Log.Debug(string.Format("{0} Processed", sw.Elapsed));
                application.Response.ContentEncoding = System.Text.Encoding.Unicode;
                application.Response.ContentType     = GetContentType( callContext );
                application.Response.Charset         = "utf-16";
                application.Response.CacheControl    = "no-cache";

                callContext.Log.Debug(string.Format("{0} Setting Compression", sw.Elapsed));
                SetCompression(application);
                callContext.Log.Debug(string.Format("{0} Creating Response", sw.Elapsed));

                using( var inputStream  = callContext.GetResponseStream() )
                using( var outputStream = application.Response.OutputStream )
                {
                    callContext.Log.Debug(string.Format("{0} Sending output", sw.Elapsed));
                    inputStream.CopyTo( outputStream );
                    callContext.Log.Debug(string.Format("{0} Output sent", sw.Elapsed));
                }

                callContext.Log.Debug(string.Format("{0} Ending", sw.Elapsed));
                callContext.Log.Commit((uint) sw.ElapsedMilliseconds);
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
        private string GetContentType( ICallContext callContext )
        {
            // TODO: Should validate when request is received, not after it's done processing
            switch( callContext.ReturnFormat )
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
        private bool IsOnIgnoreList( string absolutePath )
        {
            if( absolutePath.EndsWith( "favicon.ico" ) )
                return true;

            // TODO: other resources that should be ignored

            return false;
        }

        /// <summary>
        /// Creates a CallContext based on the HttpRequest
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        protected ICallContext CreateCallContext( HttpRequest request )
        {
            var split     = request.Url.AbsolutePath.Substring( request.ApplicationPath.Length ).Split('/');
            var extension = split[ split.Length - 2 ];
            var action    = split[ split.Length - 1 ];
	        
            switch( request.HttpMethod )
            {
                case "DELETE":
                case "PUT":
                case "POST":
                    {
                        var files = request.Files.AllKeys.Select(key => request.Files[key]).Select(file => new FileStream(file.InputStream, file.FileName, file.ContentType, file.ContentLength)).ToList();
                        var callContext = new CallContext(PortalApplication, new PortalRequest(extension, action, ConvertToIDictionary(request.Form), files), new PortalResponse());

                        callContext.Log.Debug(request.ContentEncoding.EncodingName);
                        foreach (var parameter in callContext.PortalRequest.Parameters)
                        {
                            callContext.Log.Debug(parameter.Value);
                        }

                        return callContext;
                    }
                case "GET":
                    {
                        var callContext = new CallContext(PortalApplication, new PortalRequest(extension, action, ConvertToIDictionary(request.QueryString)), new PortalResponse());

                        callContext.Log.Debug(request.ContentEncoding.EncodingName);
                        foreach (var parameter in callContext.PortalRequest.Parameters)
                        {
                            callContext.Log.Debug(parameter.Value);
                        }

                        return callContext;
                    }
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
