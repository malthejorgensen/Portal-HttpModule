using System;
using System.Text;
using System.Web;
using Chaos.Portal;
using Chaos.Portal.Data.Dto.Standard;
using Chaos.Portal.Request;
using Chaos.Portal.Response;

namespace CHAOS.Portal.Core.HttpModule.HttpMethod.Strategies
{
    public class GetMethodStrategy : AHttpMethodStrategy
    {
        #region Initialize

        public GetMethodStrategy(IPortalApplication portalApplication) : base(portalApplication)
        {
        }

        #endregion
        #region Business Logic

        protected override IPortalRequest CreatePortalRequest(HttpRequest request)
        {
            var extension = request.Url.Segments[request.Url.Segments.Length - 2].Trim( '/' );
            var action    = request.Url.Segments[request.Url.Segments.Length - 1].Trim( '/' );

            return new PortalRequest(extension, action, ConvertToIDictionary(request.QueryString));
        }

        protected override IPortalResponse CreatePortalResponse(IPortalRequest request)
        {
            return new PortalResponse(new PortalHeader(request.Stopwatch, Encoding.UTF8), 
                                      new PortalResult(), 
                                      new PortalError());
        }

        #endregion
    }
}