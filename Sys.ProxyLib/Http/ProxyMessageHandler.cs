using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using Sys.ProxyLib.Proxy;

namespace Sys.ProxyLib.Http
{
    public class Options
    {
        public bool UseProxy => true;

        public bool SupportsRedirectConfiguration => true;

        public DecompressionMethods AutomaticDecompression => DecompressionMethods.GZip | DecompressionMethods.Deflate;

        public TimeSpan? PoolTimeout { get; set; }

        public uint PoolSizePerHost { get; set; }

        public ProxyType ProxyType { get; set; }

        public string ProxyHost { get; set; }

        public int? ProxyPort { get; set; }

        public string ProxyUsername { get; set; }

        public string ProxyPassword { get; set; }

        public TimeSpan? ProxySendTimeout { get; set; }

        public TimeSpan? ProxyReceiveTimeout { get; set; }

        public bool AllowAutoRedirect { get; set; }

        public uint MaxAutomaticRedirections { get; set; }

        public bool UseCookies { get; set; }

        public CookieContainer CookieContainer { get; set; }

        public RemoteCertificateValidationCallback ServerCertificateCustomValidationCallback { get; set; }
    }

    public class ProxyMessageHandler : HttpMessageHandler
    {
        private Options _options = null;
        private ProxyClientPool _pool = null;

        public ProxyMessageHandler(Action<Options> action = null)
        {
            _options = new Options();

            action?.Invoke(_options);

            if (_options.ProxyType == ProxyType.None)
            {
                throw new Exception(String.Format("Unknown proxy type {0}.", _options.ProxyType.ToString()));
            }

            if (string.IsNullOrEmpty(_options.ProxyHost))
            {
                throw new ArgumentNullException(nameof(_options.ProxyHost));
            }

            if (_options.ProxyPort <= 0 || _options.ProxyPort > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(_options.ProxyPort), "port must be greater than zero and less than 65535");
            }

            var factory = new ProxyFactory();

            _pool = new ProxyClientPool(
                Math.Max(1, _options.PoolSizePerHost),
                cancel =>
                {
                    var proxyClient = factory.CreateProxy(
                        _options.ProxyType, _options.ProxyHost, _options.ProxyPort, _options.ProxyUsername,
                        _options.ProxyPassword);

                    if (_options.ProxySendTimeout != null)
                    {
                        proxyClient.SendTimeout = (int)_options.ProxySendTimeout.Value.TotalMilliseconds;
                    }

                    if (_options.ProxyReceiveTimeout != null)
                    {
                        proxyClient.ReceiveTimeout = (int)_options.ProxyReceiveTimeout.Value.TotalMilliseconds;
                    }

                    return Task.FromResult(proxyClient);
                },
                _options.ServerCertificateCustomValidationCallback);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return SendAsync(request, cancellationToken, 0);
        }

        private async Task StreamDrainAsync(Stream stream, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] buffer = new byte[4096];

            for (int num = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                num > 0;
                num = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken, int redictIndex)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var uri = request.RequestUri;
            var maxRedictCount = Math.Max(1, _options.MaxAutomaticRedirections);
            var pooledClient = await _pool.GetClientAsync(uri, _options.PoolTimeout, cancellationToken);

            try
            {
                var cookies = _options.UseCookies ? _options.CookieContainer : null;
                var stream = await pooledClient.Value.GetStreamAsync(cancellationToken).ConfigureAwait(false);
                var httpConnection = new HttpConnection(new BufferedReadStream(stream, leaveOpen: true)) { PooledClient = pooledClient };
                var responseMessage = await httpConnection.SendAsync(request, cookies, cancellationToken).ConfigureAwait(false);

                if (_options.AllowAutoRedirect &&
                    redictIndex <= maxRedictCount && (
                    responseMessage.StatusCode == HttpStatusCode.Moved ||
                    responseMessage.StatusCode == HttpStatusCode.Redirect ||
                    responseMessage.StatusCode == HttpStatusCode.RedirectMethod ||
                    responseMessage.StatusCode == HttpStatusCode.RedirectKeepVerb))
                {
                    var tmpStream = await responseMessage.Content.ReadAsStreamAsync().ConfigureAwait(false);

                    await StreamDrainAsync(tmpStream, cancellationToken).ConfigureAwait(false);

                    var location = responseMessage.Headers.Location;

                    if (location.IsAbsoluteUri)
                    {
                        request.RequestUri = location;
                    }
                    else
                    {
                        request.RequestUri = new Uri(string.Format("{0}://{1}/{2}", uri.Scheme, uri.Authority, location.ToString().TrimStart('/')));
                    }

                    redictIndex++;

                    switch (responseMessage.StatusCode)
                    {
                        case HttpStatusCode.Moved:
                        case HttpStatusCode.Redirect:
                            if (request.Method == HttpMethod.Post)
                            {
                                request.Method = HttpMethod.Get;
                            }
                            break;
                        case HttpStatusCode.RedirectMethod:
                            request.Method = HttpMethod.Get;
                            break;
                    }

                    responseMessage = await SendAsync(request, cancellationToken, redictIndex).ConfigureAwait(false);
                }

                return responseMessage;
            }
            catch (Exception e)
            {
                pooledClient.Dispose();
                throw e;
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _pool?.Dispose();
            }
        }
    }
}