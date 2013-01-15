using System.Web;

namespace CHAOS.Portal.Core.HttpModule.HttpMethod
{
    public interface IHttpMethodStrategy
    {
        void ProcessRequest(HttpApplication application);
    }
}