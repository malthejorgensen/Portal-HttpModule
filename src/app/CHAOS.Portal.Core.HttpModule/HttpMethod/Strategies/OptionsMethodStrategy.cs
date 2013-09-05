using System;
using System.Web;

namespace CHAOS.Portal.Core.HttpModule.HttpMethod.Strategies
{
    using System.Threading.Tasks;

    public class OptionsMethodStrategy : IHttpMethodStrategy
    {
        #region Business Logic

        public async Task ProcessRequest(HttpApplication application)
        {
            application.Response.AppendHeader("Access-Control-Allow-Origin", "*");
            application.Response.CacheControl = "Private";
            application.Response.Cache.SetMaxAge(new TimeSpan(0,1,0));
        }

        #endregion
    }
}