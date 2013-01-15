using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Xml.Linq;
using CHAOS.Extensions;
using CHAOS.Index.Solr;
using CHAOS.Portal.Core.HttpModule.HttpMethod;
using CHAOS.Portal.Core.HttpModule.HttpMethod.Strategies;
using CHAOS.Portal.Data.EF;
using Chaos.Portal;
using Chaos.Portal.Cache.Couchbase;
using Chaos.Portal.Data.EF;
using Chaos.Portal.Exceptions;
using Chaos.Portal.Extension;
using Chaos.Portal.Index;
using Chaos.Portal.Logging.Database;
using Chaos.Portal.Logging;

namespace CHAOS.Portal.Core.HttpModule
{
    public class PortalHttpModule : IHttpModule
	{
        #region Fields

        private static IDictionary<string, IHttpMethodStrategy> _httpMethodHandlers; 

        protected string    ServiceDirectory  = ConfigurationManager.AppSettings["ServiceDirectory"];
        protected UUID      AnonymousUserGuid = new UUID( ConfigurationManager.AppSettings["AnonymousUserGUID"] );
        protected LogLevel? _logLevel;

        #endregion
        #region Properties

        protected static IPortalApplication PortalApplication { get; set; }

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
        #region Initializa

        static PortalHttpModule()
        {
            
        }

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
                        var portalRepository = new PortalRepository().WithConfiguration(ConfigurationManager.ConnectionStrings["PortalEntities"].ConnectionString);
                        var cache            = new Cache();
                        var index            = new SolrCoreManager();
                        var loggingFactory   = new DatabaseLoggerFactory(portalRepository).WithLogLevel(LogLevel);

                        PortalApplication = new PortalApplication( cache, index, new ViewManager(), portalRepository, loggingFactory );

						//AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

                        InitializeExtensions(string.Format("{0}\\Extensions", ServiceDirectory), PortalApplication);

						context.Application["PortalApplication"] = PortalApplication;
                    }
                }
            }

			PortalApplication = (PortalApplication)context.Application["PortalApplication"];
            _httpMethodHandlers = new Dictionary<string, IHttpMethodStrategy>
                                      {
                                          {"GET",     new GetMethodStrategy(PortalApplication)},
                                          {"POST",    new PostMethodStrategy(PortalApplication)},
                                          {"PUT",     new PostMethodStrategy(PortalApplication)},
                                          {"DELETE",  new PostMethodStrategy(PortalApplication)},
                                          {"OPTIONS", new OptionsMethodStrategy()}
                                      };

            context.BeginRequest += ContextBeginRequest;
        }

        private static IEnumerable<IExtension> LoadExtensions( string path )
		{
            if (!System.IO.Directory.Exists(path)) yield break;

			foreach (var assembly in System.IO.Directory.GetFiles( path, "*.dll" ).Select(Assembly.LoadFile))
			{
                foreach (var type in GetClassesOf<IExtension>(from: assembly))
				{
					var obj = assembly.CreateInstance( type.FullName );

				    yield return (IExtension) obj;
				}
			}
		}

        private static void InitializeExtensions( string path, IPortalApplication application )
        {
            foreach (var extension in LoadExtensions(path))
	        {
                var attribute = extension.GetType().GetCustomAttribute<PortalExtensionAttribute>(true);

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

        #endregion
        #region Business Logic

        private void ContextBeginRequest(object sender, EventArgs e)
        {
            using (var application = (HttpApplication) sender)
            {
                if (IsOnIgnoreList(application.Request.Url)) return; // TODO: 404

                _httpMethodHandlers[application.Request.HttpMethod].ProcessRequest(application);

                application.Response.End();
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

        #endregion
    }
}
