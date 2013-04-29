// --------------------------------------------------------------------------------------------------------------------
// <copyright file="PostMethodStrategy.cs" company="Chaos ApS">
//   All rights Reserved
// </copyright>
// <summary>
//   The post method strategy.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace CHAOS.Portal.Core.HttpModule.HttpMethod.Strategies
{
    using System.Linq;
    using System.Web;

    using Chaos.Portal;
    using Chaos.Portal.Request;

    /// <summary>
    /// The post method strategy.
    /// </summary>
    public class PostMethodStrategy : AHttpMethodStrategy
    {
        #region Initialize

        /// <summary>
        /// Initializes a new instance of the <see cref="PostMethodStrategy"/> class.
        /// </summary>
        /// <param name="portalApplication">
        /// The portal application.
        /// </param>
        public PostMethodStrategy(IPortalApplication portalApplication) : base(portalApplication)
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
            var extension = request.Url.Segments[request.Url.Segments.Length - 2].Trim('/');
            var action    = request.Url.Segments[request.Url.Segments.Length - 1].Trim('/');

            var files = request.Files.AllKeys.Select(key => request.Files[key]).Select(file => new FileStream(file.InputStream, file.FileName, file.ContentType, file.ContentLength)).ToList();

            return new PortalRequest(extension, action, ConvertToIDictionary(request.Form), files);
        }

        #endregion
    }
}