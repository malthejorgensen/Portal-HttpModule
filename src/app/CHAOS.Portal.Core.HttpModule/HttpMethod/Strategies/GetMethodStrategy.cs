namespace CHAOS.Portal.Core.HttpModule.HttpMethod.Strategies
{
    using System.Web;

    using Chaos.Portal;
    using Chaos.Portal.Request;

    /// <summary>
    /// The get method strategy.
    /// </summary>
    public class GetMethodStrategy : AHttpMethodStrategy
    {
        #region Initialize

        public GetMethodStrategy(IPortalApplication portalApplication) : base(portalApplication)
        {
        }

        #endregion
        #region Business Logic

        /// <summary>
        /// The create portal request.
        /// </summary>
        /// <param name="request">
        /// The request.
        /// </param>
        /// <returns>
        /// The <see cref="IPortalRequest"/>.
        /// </returns>
        protected override IPortalRequest CreatePortalRequest(HttpRequest request)
        {
            var extension = request.Url.Segments[request.Url.Segments.Length - 2].Trim( '/' );
            var action    = request.Url.Segments[request.Url.Segments.Length - 1].Trim( '/' );

            return new PortalRequest(extension, action, ConvertToIDictionary(request.QueryString));
        }

        #endregion
    }
}