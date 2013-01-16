using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Web;
using Chaos.Portal;
using Chaos.Portal.Request;
using Chaos.Portal.Response;

namespace CHAOS.Portal.Core.HttpModule.HttpMethod.Strategies
{
    public abstract class AHttpMethodStrategy : IHttpMethodStrategy
    {
        #region Properties

        protected static IPortalApplication PortalApplication { get; private set; }

        #endregion
        #region Abstract methods

        protected abstract IPortalRequest CreatePortalRequest(HttpRequest request);
        protected abstract IPortalResponse CreatePortalResponse(IPortalRequest request);

        #endregion
        #region Initialize

        protected AHttpMethodStrategy(IPortalApplication portalApplication)
        {
            PortalApplication = portalApplication;
        }

        #endregion
        #region Business Logic

        public void ProcessRequest(HttpApplication application)
        {
            var message = new StringBuilder();
            var timer = new System.Diagnostics.Stopwatch();timer.Start();
            
            var request  = CreatePortalRequest(application.Request); message.AppendFormat("{0} var request  = CreatePortalRequest(application.Request);\n", timer.Elapsed);
            var response = CreatePortalResponse( request ); message.AppendFormat( "{0} var response = CreatePortalResponse(request);\n", timer.Elapsed );

            try
            {
                PortalApplication.ProcessRequest( request, response ); message.AppendFormat( "{0} PortalApplication.ProcessRequest( request, response );\n", timer.Elapsed );
            }
            catch (System.Exception ex)
            {
                response.Error.SetException(ex);
                PortalApplication.Log.Fatal("ProcessRequest() - Unhandeled exception occured during", ex);
            }

            application.Response.Cache.SetMaxAge(new TimeSpan(0,0,30));
            application.Response.CacheControl    = "private";
            application.Response.ContentType     = GetContentType(response.Header.ReturnFormat);
            
            application.Response.AppendHeader( "Access-Control-Allow-Origin", "*" );

            application.Response.Charset         = response.Header.Encoding.HeaderName;
            application.Response.ContentEncoding = response.Header.Encoding;
            message.AppendFormat( "{0} set headers\n", timer.Elapsed );
            SetCompression(application);
            message.AppendFormat( "{0} SetCompression(application);\n", timer.Elapsed );

            using (var inputStream = response.GetResponseStream())
            using (var outputStream = application.Response.OutputStream)
            {
                message.AppendFormat( "{0} open streams\n", timer.Elapsed );
                inputStream.Position = 0;
                inputStream.CopyTo(outputStream);
                message.AppendFormat( "{0} copy stream", timer.Elapsed );
            }

            PortalApplication.Log.WithStopwatch(timer).Debug( message.ToString() );
        }

        /// <summary>
        /// Get the http response content type based on the return format requested 
        /// </summary>
        /// <param name="format"></param>
        /// <returns></returns>
        private static string GetContentType(ReturnFormat format)
        {
            // TODO: Should validate when request is received, not after it's done processing
            switch (format)
            {
                case ReturnFormat.XML:
                    return "text/xml";
                case ReturnFormat.JSON:
                    return "application/json";
                case ReturnFormat.JSONP:
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

        #endregion
    }
}
