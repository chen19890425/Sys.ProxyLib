using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sys.ProxyLib.Proxy
{
    public interface IProxyClient : IDisposable
    {
        string ProxyName { get; }

        string ProxyHost { get; }

        int ProxyPort { get; }

        bool Connected { get; }

        int Available { get; }

        int SendTimeout { get; set; }

        int ReceiveTimeout { get; set; }

        NetworkStream GetStream();

        Task ConnectionAsync(string destinationHost, int destinationPort, CancellationToken cancellationToken = default(CancellationToken));
    }
}