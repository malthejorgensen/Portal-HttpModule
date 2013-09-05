namespace CHAOS.Portal.Core.HttpModule.HttpMethod.Strategies
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Threading.Tasks;
    using System.Web;

    using Chaos.Portal.Core;
    using Chaos.Portal.Core.Exceptions;
    using Chaos.Portal.Core.Request;

    /// <summary>
    /// The a http method strategy.
    /// </summary>
    public abstract class AHttpMethodStrategy : IHttpMethodStrategy
    {
        #region Properties

        protected static IPortalApplication PortalApplication { get; private set; }

        #endregion
        #region Abstract methods

        protected abstract IPortalRequest CreatePortalRequest(HttpRequest request);

        #endregion
        #region Initialize

        protected AHttpMethodStrategy(IPortalApplication portalApplication)
        {
            PortalApplication = portalApplication;
        }

        #endregion
        #region Business Logic

        /// <summary>
        /// The process request.
        /// </summary>
        /// <param name="application">
        /// The application.
        /// </param>
        public async Task ProcessRequest(HttpApplication application)
        {
            var request  = CreatePortalRequest(application.Request);

            using (var response = PortalApplication.ProcessRequest(request))
            {
                application.Response.AppendHeader( "Access-Control-Allow-Origin", "*" );
                application.Response.ContentType     = GetContentType(response.ReturnFormat);
                application.Response.Charset         = response.Encoding.HeaderName;
                application.Response.ContentEncoding = response.Encoding;

                SetCompression(application);

                using (var inputStream = response.GetResponseStream())
                using (var outputStream = application.Response.OutputStream)
                {
                    inputStream.Position = 0;
                    await inputStream.CopyToAsync(outputStream);
                }
            }
        }

        /// <summary>
        /// Get the http response content type based on the return format requested 
        /// </summary>
        /// <param name="format">The return format for which the mime type is returned</param>
        /// <returns>The mime type associated with the ReturnFormat</returns>
        private static string GetContentType(ReturnFormat format)
        {
            // TODO: Should validate when request is received, not after it's done processing
            switch (format)
            {
                case ReturnFormat.XML:
                case ReturnFormat.XML2:
                    return "text/xml";
                case ReturnFormat.JSON:
                case ReturnFormat.JSON2:
                    return "application/json";
                case ReturnFormat.JSONP:
                case ReturnFormat.JSONP2:
                    return "application/javascript";
                default:
                    throw new NotImplementedException("Unknown return format");
            }
        }

        private static void SetCompression(HttpApplication application)
        {
            var acceptEncoding = HttpContext.Current.Request.Headers["Accept-Encoding"];

            if (string.IsNullOrEmpty(acceptEncoding) || (!acceptEncoding.Contains("gzip") && !acceptEncoding.Contains("deflate"))) return;

            if (acceptEncoding.Contains("gzip"))
            {
                application.Response.AppendHeader("Content-Encoding", "gzip");
                application.Response.Filter = new System.IO.Compression.GZipStream(application.Response.Filter, System.IO.Compression.CompressionMode.Compress);
            }
            else
            {
                application.Response.AppendHeader("Content-Encoding", "deflate");
                application.Response.Filter = new System.IO.Compression.DeflateStream(application.Response.Filter, System.IO.Compression.CompressionMode.Compress);
            }
        }

        /// <summary>
        /// Converts a NameValueCollection to a IDictionary
        /// </summary>
        /// <param name="nameValueCollection"></param>
        /// <returns></returns>
        protected static IDictionary<string, string> ConvertToIDictionary(NameValueCollection nameValueCollection)
        {
            var parameters = new Dictionary<string, string>();

            for (var i = 0; i < nameValueCollection.Keys.Count; i++)
            {
                parameters.Add(nameValueCollection.Keys[i], nameValueCollection[i]);
            }

            return parameters;
        }

        protected Protocol GetProtocolVersion(string version)
        {
            switch(version.ToUpper())
            {
                case "V5":
                    return Protocol.V5;
                case "V6":
                    return Protocol.V6;
                default:
                    throw new ProtocolVersionException(string.Format("Protocol ({0}) is not supported", version));
            }
        }

        #endregion
    }
}
