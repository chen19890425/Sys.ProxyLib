using System;
using System.Globalization;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Sys.ProxyLib.Proxy.Exceptions;

namespace Sys.ProxyLib.Proxy
{
    public abstract class ProxyClientBase : IProxyClient
    {
        private Lazy<TcpClient> _tcpClient = null;

        protected ProxyClientBase(string proxyHost, int proxyPort)
        {
            if (string.IsNullOrEmpty(proxyHost))
            {
                throw new ArgumentNullException(nameof(proxyHost));
            }

            if (proxyPort <= 0 || proxyPort > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(proxyPort), "port must be greater than zero and less than 65535");
            }

            ProxyHost = proxyHost;
            ProxyPort = proxyPort;
            _tcpClient = new Lazy<TcpClient>(() => new TcpClient());
        }

        protected TcpClient Client => _tcpClient.Value;

        public abstract string ProxyName { get; }

        public string ProxyHost { get; }

        public int ProxyPort { get; }

        public bool Connected => Client.Connected;

        public int Available => Client.Available;

        public int SendTimeout { get; set; }

        public int ReceiveTimeout { get; set; }

        public NetworkStream GetStream() => Client.GetStream();

        protected abstract Task SendProxyCommandAsync(string destinationHost, int destinationPort, CancellationToken cancellationToken = default);

        public async Task ConnectionAsync(string destinationHost, int destinationPort, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(destinationHost))
            {
                throw new ArgumentNullException(nameof(destinationHost));
            }

            if (destinationPort <= 0 || destinationPort > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationPort), "port must be greater than zero and less than 65535");
            }

            try
            {
                Client.SendTimeout = SendTimeout;

                Client.ReceiveTimeout = ReceiveTimeout;

                await Client.ConnectAsync(ProxyHost, ProxyPort).ConfigureAwait(false);

                await SendProxyCommandAsync(destinationHost, destinationPort, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new ProxyException(string.Format(CultureInfo.InvariantCulture, "Connection to proxy host {0} on port {1} failed.", ProxyHost, ProxyPort), e);
            }
        }

        public void Dispose()
        {
            if (_tcpClient.IsValueCreated)
            {
                _tcpClient.Value.Dispose();
            }
        }
    }
}