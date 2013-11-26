namespace CHAOS.Portal.Core.HttpModule
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using System.Web;

    using CHAOS.Extensions;
    using CHAOS.Portal.Core.HttpModule.HttpMethod;
    using CHAOS.Portal.Core.HttpModule.HttpMethod.Strategies;

    using Chaos.Portal;
    using Chaos.Portal.Core;
    using Chaos.Portal.Core.Cache.Couchbase;
    using Chaos.Portal.Core.Data;
    using Chaos.Portal.Core.Exceptions;
    using Chaos.Portal.Core.Indexing.View;
    using Chaos.Portal.Core.Logging.Database;
    using Chaos.Portal.Core.Module;
    using Chaos.Portal.Module;

    using Couchbase;
    using Chaos.Portal.Core.Logging;

    public class PortalHttpModule : HttpTaskAsyncHandler
	{
        #region Fields

        protected static IDictionary<string, IHttpMethodStrategy> HttpMethodHandlers;
        protected static IDictionary<string, Assembly>            LoadedAssemblies;
        protected static string                                   ServiceDirectory  = ConfigurationManager.AppSettings["ServiceDirectory"];
        protected static Guid                                     AnonymousUserGuid = new Guid(ConfigurationManager.AppSettings["AnonymousUserGUID"]);
        protected static LogLevel?                                _logLevel;

        #endregion
        #region Properties

        protected static IPortalApplication PortalApplication { get; set; }

        protected static LogLevel LogLevel
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

        public override bool IsReusable
        {
            get
            {
                return true;
            }
        }

        #endregion
        #region Initializa

        static PortalHttpModule()
        {
            LoadedAssemblies = new Dictionary<string, Assembly>();

            var portalRepository = new PortalRepository().WithConfiguration(ConfigurationManager.ConnectionStrings["PortalEntities"].ConnectionString);
            var cache            = new Cache(new CouchbaseClient());
            var loggingFactory   = new DatabaseLoggerFactory(portalRepository).WithLogLevel(LogLevel);
            var viewManager      = new ViewManager(new Dictionary<string, Chaos.Portal.Core.Indexing.View.IView>(), cache);
            PortalApplication    = new PortalApplication(cache, viewManager, portalRepository, loggingFactory);

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
            PortalApplication.AddModule(new PortalModule());
            InitializeModules(string.Format("{0}\\Modules", ServiceDirectory), PortalApplication);

            HttpMethodHandlers = new Dictionary<string, IHttpMethodStrategy>
                                      {
                                          {"GET",     new GetMethodStrategy(PortalApplication)},
                                          {"POST",    new PostMethodStrategy(PortalApplication)},
                                          {"PUT",     new PostMethodStrategy(PortalApplication)},
                                          {"DELETE",  new PostMethodStrategy(PortalApplication)},
                                          {"OPTIONS", new OptionsMethodStrategy()}
                                      };
        }

        private static void InitializeModules( string path, IPortalApplication application )
        {
            foreach (var assembly in System.IO.Directory.GetFiles(path, "*.dll").Select(Assembly.LoadFile))
            {
                LoadedAssemblies.Add(assembly.FullName, assembly);

                foreach (var type in GetClassesOf<IModule>(from: assembly))
                {
                    var obj = assembly.CreateInstance(type.FullName);

                    application.AddModule((IModule)obj);
                }
            }
        }

        static Assembly CurrentDomain_AssemblyResolve( object sender, ResolveEventArgs args )
        {
            if( LoadedAssemblies.ContainsKey( args.Name ) )
                return LoadedAssemblies[args.Name];

            foreach (var file in Directory.GetFiles(string.Format("{0}\\Modules", ServiceDirectory), "*.dll"))
            {
                var assembly = Assembly.LoadFile(file);

                if (args.Name.Equals(assembly.FullName))
                    return assembly;
            }
			
            throw new AssemblyNotLoadedException( string.Format( "The assembly {0} is not loaded", args.Name ));
        }

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

        /// <summary>
        /// Determine if the requested resource should be ignored
        /// </summary>
        /// <param name="uri"> </param>
        /// <returns></returns>
        private static bool IsOnIgnoreList( Uri uri )
        {
            return uri.AbsolutePath.EndsWith( "favicon.ico" );

            // TODO: other resources that should be ignored
        }

        #endregion

        #region Overrides of HttpTaskAsyncHandler

        public async override Task ProcessRequestAsync(HttpContext context)
        {
            try
            {
                using (var application = context.ApplicationInstance)
                {
                    if (!IsOnIgnoreList(application.Request.Url)) // TODO: 404
                    {
                        await HttpMethodHandlers[application.Request.HttpMethod].ProcessRequest(application);
                    }
                }
            }
            catch (Exception ex)
            {
                PortalApplication.Log.Fatal("ProcessRequest() - Unhandeled exception occured during", ex);
            }
        }

        #endregion
	}
}
