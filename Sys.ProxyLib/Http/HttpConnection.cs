using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sys.ProxyLib.Helpers;

namespace Sys.ProxyLib.Http
{
    internal class HttpConnection : IDisposable
    {
        private const string CRLF = "\r\n";

        public HttpConnection(BufferedReadStream transport)
        {
            Transport = transport;
        }

        public BufferedReadStream Transport { get; private set; }

        internal PooledObject<ProxyClientWrapper> PooledClient { get; set; }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CookieContainer cookies, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                // Serialize headers & send
                string rawRequest = SerializeRequest(request, cookies);
                byte[] requestBytes = Encoding.ASCII.GetBytes(rawRequest);
                await Transport.WriteAsync(requestBytes, 0, requestBytes.Length, cancellationToken);

                // TODO: Determin if there's a request body?
                // Wait for 100-continue?
                // Send body
                if (request.Content != null)
                {
                    await request.Content.CopyToAsync(Transport);
                }

                // Receive headers
                List<string> responseLines = await ReadResponseLinesAsync(cancellationToken);
                // Determine response type (Chunked, Content-Length, opaque, none...)
                // Receive body
                return CreateResponseMessage(responseLines, request, cookies);
            }
            catch (Exception ex)
            {
                Dispose(); // Any errors at this layer abort the connection.
                throw new HttpRequestException("The requested failed, see inner exception for details.", ex);
            }
        }

        private string SerializeRequest(HttpRequestMessage request, CookieContainer cookies)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(request.Method);
            builder.Append(' ');
            builder.Append(request.RequestUri.PathAndQuery);
            builder.Append(" HTTP/");
            builder.Append(request.Version.ToString(2));
            builder.Append(CRLF);

            if (string.IsNullOrEmpty(request.Headers.Host))
            {
                builder.Append($"Host: {request.RequestUri.Authority}{CRLF}");
            }

            foreach (var header in request.Headers)
            {
                foreach (var value in header.Value)
                {
                    builder.Append(header.Key);
                    builder.Append(": ");
                    builder.Append(value);
                    builder.Append(CRLF);
                }
            }

            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    foreach (var value in header.Value)
                    {
                        builder.Append(header.Key);
                        builder.Append(": ");
                        builder.Append(value);
                        builder.Append(CRLF);
                    }
                }

                if (!request.Content.Headers.Contains("Content-Length"))
                {
                    var content = request.Content.Headers as HttpContentHeaders;

                    if (content != null)
                    {
                        builder.Append("Content-Length");
                        builder.Append(": ");
                        builder.Append(content.ContentLength);
                        builder.Append(CRLF);
                    }
                }
            }

            if (cookies != null)
            {
                var cookiesCollection = cookies.GetCookies(request.RequestUri);

                if (cookiesCollection.Count > 0)
                {
                    var str_cookies = cookiesCollection.Cast<Cookie>().Select(cookie => string.Concat(cookie.Name, "=", cookie.Value));

                    builder.Append("Cookie");
                    builder.Append(": ");
                    builder.Append(string.Concat(string.Join("; ", str_cookies), ";"));
                    builder.Append(CRLF);
                }
            }

            builder.Append(CRLF);

            return builder.ToString();
        }

        private async Task<List<string>> ReadResponseLinesAsync(CancellationToken cancellationToken)
        {
            List<string> lines = new List<string>();
            while (true)
            {
                string line = await Transport.ReadLineAsync(cancellationToken);
                if (line.Length == 0)
                {
                    break;
                }
                lines.Add(line);
            }
            return lines;
        }

        private HttpResponseMessage CreateResponseMessage(List<string> responseLines, HttpRequestMessage request, CookieContainer cookies)
        {
            string responseLine = responseLines.FirstOrDefault() ?? string.Empty;
            // HTTP/1.1 200 OK
            string[] responseLineParts = responseLine.Split(new[] { ' ' }, 3);
            // TODO: Verify HTTP/1.0 or 1.1.
            if (responseLineParts.Length < 2)
            {
                throw new HttpRequestException("Invalid response line: " + responseLine);
            }
            int statusCode = 0;
            if (!int.TryParse(responseLineParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out statusCode))
            {
                throw new HttpRequestException("Invalid status code: " + responseLineParts[1]);
            }
            HttpResponseMessage response = new HttpResponseMessage((HttpStatusCode)statusCode);
            if (responseLineParts.Length >= 3)
            {
                response.ReasonPhrase = responseLineParts[2];
            }
            var content = new HttpConnectionResponseContent(this);
            response.Content = content;

            foreach (var rawHeader in responseLines.Skip(1))
            {
                int colonOffset = rawHeader.IndexOf(':');
                if (colonOffset <= 0)
                {
                    throw new HttpRequestException("The given header line format is invalid: " + rawHeader);
                }
                string headerName = rawHeader.Substring(0, colonOffset);
                string headerValue = rawHeader.Substring(colonOffset + 2);
                if (cookies != null && string.Compare(headerName, "Set-Cookie", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    cookies.Set_Cookie(request, headerValue);
                }
                else if (!response.Headers.TryAddWithoutValidation(headerName, headerValue))
                {
                    bool success = response.Content.Headers.TryAddWithoutValidation(headerName, headerValue);
                    Debug.Assert(success, "Failed to add response header: " + rawHeader);
                }
            }
            // After headers have been set
            content.ResolveResponseStream(response.Headers.TransferEncodingChunked ?? false, response.Content.Headers.ContentEncoding.FirstOrDefault());

            return response;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Transport.Dispose();
            }
        }
    }
}