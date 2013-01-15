using System.Linq;
using System.Web;
using Chaos.Portal;
using Chaos.Portal.Request;
using Chaos.Portal.Response;

namespace CHAOS.Portal.Core.HttpModule.HttpMethod.Strategies
{
    public class PostMethodStrategy : AHttpMethodStrategy
    {
        #region Initialize

        public PostMethodStrategy(IPortalApplication portalApplication) : base(portalApplication)
        {

        }

        #endregion
        #region Business Logic

        protected override IPortalResponse CreatePortalResponse(IPortalRequest request)
        {
            throw new System.NotImplementedException();
        }

        protected override IPortalRequest CreatePortalRequest(HttpRequest request)
        {
            var extension = request.Url.Segments[request.Url.Segments.Length - 2].Trim('/');
            var action    = request.Url.Segments[request.Url.Segments.Length - 1].Trim('/');

            var files = request.Files.AllKeys.Select(key => request.Files[key]).Select(file => new FileStream(file.InputStream, file.FileName, file.ContentType, file.ContentLength)).ToList();

            return new PortalRequest(extension, action, ConvertToIDictionary(request.Form), files);
        }

        #endregion
    }
}