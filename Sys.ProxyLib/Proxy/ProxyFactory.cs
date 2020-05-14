using System;
using Sys.ProxyLib.Proxy.Exceptions;

namespace Sys.ProxyLib.Proxy
{
    public enum ProxyType
    {
        None,
        Http,
        Socks4,
        Socks4a,
        Socks5
    }

    public class ProxyFactory
    {
        public IProxyClient CreateProxy(ProxyType type, string proxyHost, int? proxyPort = null, string proxyUsername = null, string proxyPassword = null)
        {
            if (type == ProxyType.None)
            {
                throw new ArgumentOutOfRangeException(nameof(type));
            }

            switch (type)
            {
                case ProxyType.Http:
                    return new HttpProxyClient(proxyHost, proxyPort ?? HttpProxyClient.HTTP_PROXY_DEFAULT_PORT, proxyUsername, proxyPassword);
                case ProxyType.Socks4:
                    return new Socks4ProxyClient(proxyHost, proxyPort ?? Socks4ProxyClient.SOCKS_PROXY_DEFAULT_PORT, proxyUsername);
                case ProxyType.Socks4a:
                    return new Socks4aProxyClient(proxyHost, proxyPort ?? Socks4ProxyClient.SOCKS_PROXY_DEFAULT_PORT, proxyUsername);
                case ProxyType.Socks5:
                    return new Socks5ProxyClient(proxyHost, proxyPort ?? Socks5ProxyClient.SOCKS5_DEFAULT_PORT, proxyUsername, proxyPassword);
                default:
                    throw new ProxyException(String.Format("Unknown proxy type {0}.", type.ToString()));
            }
        }
    }
}