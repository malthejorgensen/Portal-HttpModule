using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Xml.Linq;
using CHAOS.Extensions;
using CHAOS.Portal.Core.HttpModule.HttpMethod;
using CHAOS.Portal.Core.HttpModule.HttpMethod.Strategies;
using CHAOS.Portal.Data.EF;
using Chaos.Portal;
using Chaos.Portal.Cache.Couchbase;
using Chaos.Portal.Data.EF;
using Chaos.Portal.Exceptions;
using Chaos.Portal.Extension;
using Chaos.Portal.Logging.Database;
using Chaos.Portal.Logging;

namespace CHAOS.Portal.Core.HttpModule
{
    using Chaos.Portal.Indexing.View;
    using Chaos.Portal.Module;

    using Couchbase;

    using IView = Chaos.Portal.Indexing.View.IView;

    public class PortalHttpModule : IHttpModule
	{
        #region Fields

        private static IDictionary<string, IHttpMethodStrategy> _httpMethodHandlers; 

        protected string    ServiceDirectory  = ConfigurationManager.AppSettings["ServiceDirectory"];
        protected Guid      AnonymousUserGuid = new Guid( ConfigurationManager.AppSettings["AnonymousUserGUID"] );
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
                        var cache            = new Cache(new CouchbaseClient());
                        var loggingFactory   = new DatabaseLoggerFactory(portalRepository).WithLogLevel(LogLevel);
                        var viewManager      = new ViewManager(new Dictionary<string, IView>(), cache);
                        PortalApplication = new PortalApplication( cache, viewManager, portalRepository, loggingFactory );

						//AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
                        new PortalModule().Load(PortalApplication);
                        InitializeModules(string.Format("{0}\\Modules", ServiceDirectory), PortalApplication);

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

        private static IEnumerable<IModule> LoadModules(string path)
		{
            if (!System.IO.Directory.Exists(path)) yield break;

			foreach (var assembly in System.IO.Directory.GetFiles( path, "*.dll" ).Select(Assembly.LoadFile))
			{
                foreach (var type in GetClassesOf<IModule>(from: assembly))
				{
					var obj = assembly.CreateInstance( type.FullName );

                    yield return (IModule)obj;
				}
			}
		}

        private static void InitializeModules( string path, IPortalApplication application )
        {
            foreach (var module in LoadModules(path))
	        {
                module.Load(application);
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
        private static bool IsOnIgnoreList( Uri uri )
        {
            if(uri.AbsolutePath.EndsWith( "favicon.ico" )) return true;

            // TODO: other resources that should be ignored

            return false;
        }

        #endregion
    }
}
