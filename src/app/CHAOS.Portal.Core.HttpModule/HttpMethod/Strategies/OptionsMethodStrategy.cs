using System;
using System.Web;

namespace CHAOS.Portal.Core.HttpModule.HttpMethod.Strategies
{
    public class OptionsMethodStrategy : IHttpMethodStrategy
    {
        #region Business Logic

        public void ProcessRequest(HttpApplication application)
        {
            application.Response.AppendHeader("Access-Control-Allow-Origin", "*");
            application.Response.CacheControl = "Private";
            application.Response.Cache.SetMaxAge(new TimeSpan(0,1,0));
        }

        #endregion
    }
}