using System.Web;

namespace CHAOS.Portal.Core.HttpModule.HttpMethod
{
    using System.Threading.Tasks;

    public interface IHttpMethodStrategy
    {
        Task ProcessRequest(HttpApplication application);
    }
}