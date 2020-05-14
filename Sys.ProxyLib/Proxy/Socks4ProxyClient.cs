using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sys.ProxyLib.Proxy.Exceptions;

namespace Sys.ProxyLib.Proxy
{
    public class Socks4ProxyClient : ProxyClientBase
    {
        private const int WAIT_FOR_DATA_INTERVAL = 50;   // 50 ms
        private const int WAIT_FOR_DATA_TIMEOUT = 15000; // 15 seconds
        private const string PROXY_NAME = "SOCKS4";

        internal const int SOCKS_PROXY_DEFAULT_PORT = 1080;
        internal const byte SOCKS4_VERSION_NUMBER = 4;
        internal const byte SOCKS4_CMD_CONNECT = 0x01;
        internal const byte SOCKS4_CMD_BIND = 0x02;
        internal const byte SOCKS4_CMD_REPLY_REQUEST_GRANTED = 90;
        internal const byte SOCKS4_CMD_REPLY_REQUEST_REJECTED_OR_FAILED = 91;
        internal const byte SOCKS4_CMD_REPLY_REQUEST_REJECTED_CANNOT_CONNECT_TO_IDENTD = 92;
        internal const byte SOCKS4_CMD_REPLY_REQUEST_REJECTED_DIFFERENT_IDENTD = 93;

        public Socks4ProxyClient(string proxyHost, int proxyPort = SOCKS_PROXY_DEFAULT_PORT, string proxyUserId = null)
            : base(proxyHost, proxyPort)
        {
            ProxyUserId = proxyUserId;
        }

        public override string ProxyName => PROXY_NAME;

        public string ProxyUserId { get; }

        protected override async Task SendProxyCommandAsync(string destinationHost, int destinationPort, CancellationToken cancellationToken = default(CancellationToken))
        {
            await SendCommandAsync(Client.GetStream(), SOCKS4_CMD_CONNECT, destinationHost, destinationPort, ProxyUserId, cancellationToken);
        }

        internal virtual async Task SendCommandAsync(NetworkStream proxy, byte command, string destinationHost, int destinationPort, string userId, CancellationToken cancellationToken)
        {
            // PROXY SERVER REQUEST
            // The client connects to the SOCKS server and sends a CONNECT request when
            // it wants to establish a connection to an application server. The client
            // includes in the request packet the IP address and the port number of the
            // destination host, and userid, in the following format.
            //
            //        +----+----+----+----+----+----+----+----+----+----+....+----+
            //        | VN | CD | DSTPORT |      DSTIP        | USERID       |NULL|
            //        +----+----+----+----+----+----+----+----+----+----+....+----+
            // # of bytes:	   1    1      2              4           variable       1
            //
            // VN is the SOCKS protocol version number and should be 4. CD is the
            // SOCKS command code and should be 1 for CONNECT request. NULL is a byte
            // of all zero bits.         

            //  userId needs to be a zero length string so that the GetBytes method
            //  works properly
            if (userId == null)
            {
                userId = "";
            }

            byte[] destIp = GetIPAddressBytes(destinationHost);
            byte[] destPort = GetDestinationPortBytes(destinationPort);
            byte[] userIdBytes = ASCIIEncoding.ASCII.GetBytes(userId);
            byte[] request = new byte[9 + userIdBytes.Length];

            //  set the bits on the request byte array
            request[0] = SOCKS4_VERSION_NUMBER;
            request[1] = command;
            destPort.CopyTo(request, 2);
            destIp.CopyTo(request, 4);
            userIdBytes.CopyTo(request, 8);
            request[8 + userIdBytes.Length] = 0x00;  // null (byte with all zeros) terminator for userId

            // send the connect request
            await proxy.WriteAsync(request, 0, request.Length, cancellationToken);

            // wait for the proxy server to respond
            await WaitForDataAsync(proxy, cancellationToken);

            // PROXY SERVER RESPONSE
            // The SOCKS server checks to see whether such a request should be granted
            // based on any combination of source IP address, destination IP address,
            // destination port number, the userid, and information it may obtain by
            // consulting IDENT, cf. RFC 1413.  If the request is granted, the SOCKS
            // server makes a connection to the specified port of the destination host.
            // A reply packet is sent to the client when this connection is established,
            // or when the request is rejected or the operation fails. 
            //
            //        +----+----+----+----+----+----+----+----+
            //        | VN | CD | DSTPORT |      DSTIP        |
            //        +----+----+----+----+----+----+----+----+
            // # of bytes:	   1    1      2              4
            //
            // VN is the version of the reply code and should be 0. CD is the result
            // code with one of the following values:
            //
            //    90: request granted
            //    91: request rejected or failed
            //    92: request rejected becuase SOCKS server cannot connect to
            //        identd on the client
            //    93: request rejected because the client program and identd
            //        report different user-ids
            //
            // The remaining fields are ignored.
            //
            // The SOCKS server closes its connection immediately after notifying
            // the client of a failed or rejected request. For a successful request,
            // the SOCKS server gets ready to relay traffic on both directions. This
            // enables the client to do I/O on its connection as if it were directly
            // connected to the application server.

            // create an 8 byte response array  
            byte[] response = new byte[8];

            // read the resonse from the network stream
            await proxy.ReadAsync(response, 0, 8, cancellationToken);

            //  evaluate the reply code for an error condition
            if (response[1] != SOCKS4_CMD_REPLY_REQUEST_GRANTED)
            {
                HandleProxyCommandError(response, destinationHost, destinationPort);
            }
        }

        internal byte[] GetIPAddressBytes(string destinationHost)
        {
            IPAddress ipAddr = null;

            //  if the address doesn't parse then try to resolve with dns
            if (!IPAddress.TryParse(destinationHost, out ipAddr))
            {
                try
                {
                    ipAddr = Dns.GetHostEntry(destinationHost).AddressList[0];
                }
                catch (Exception ex)
                {
                    throw new ProxyException(string.Format(CultureInfo.InvariantCulture, "A error occurred while attempting to DNS resolve the host name {0}.", destinationHost), ex);
                }
            }

            // return address bytes
            return ipAddr.GetAddressBytes();
        }

        internal byte[] GetDestinationPortBytes(int value)
        {
            byte[] array = new byte[2];
            array[0] = Convert.ToByte(value / 256);
            array[1] = Convert.ToByte(value % 256);
            return array;
        }

        internal void HandleProxyCommandError(byte[] response, string destinationHost, int destinationPort)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            //  extract the reply code
            byte replyCode = response[1];

            //  extract the ip v4 address (4 bytes)
            byte[] ipBytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                ipBytes[i] = response[i + 4];
            }

            //  convert the ip address to an IPAddress object
            IPAddress ipAddr = new IPAddress(ipBytes);

            //  extract the port number big endian (2 bytes)
            byte[] portBytes = new byte[2];
            portBytes[0] = response[3];
            portBytes[1] = response[2];
            short port = BitConverter.ToInt16(portBytes, 0);

            // translate the reply code error number to human readable text
            string proxyErrorText;
            switch (replyCode)
            {
                case SOCKS4_CMD_REPLY_REQUEST_REJECTED_OR_FAILED:
                    proxyErrorText = "connection request was rejected or failed";
                    break;
                case SOCKS4_CMD_REPLY_REQUEST_REJECTED_CANNOT_CONNECT_TO_IDENTD:
                    proxyErrorText = "connection request was rejected because SOCKS destination cannot connect to identd on the client";
                    break;
                case SOCKS4_CMD_REPLY_REQUEST_REJECTED_DIFFERENT_IDENTD:
                    proxyErrorText = "connection request rejected because the client program and identd report different user-ids";
                    break;
                default:
                    proxyErrorText = string.Format(CultureInfo.InvariantCulture, "proxy client received an unknown reply with the code value '{0}' from the proxy destination", replyCode.ToString(CultureInfo.InvariantCulture));
                    break;
            }

            //  build the exeception message string
            string exceptionMsg = string.Format(CultureInfo.InvariantCulture, "The {0} concerning destination host {1} port number {2}.  The destination reported the host as {3} port {4}.", proxyErrorText, destinationHost, destinationPort, ipAddr.ToString(), port.ToString(CultureInfo.InvariantCulture));

            //  throw a new application exception 
            throw new ProxyException(exceptionMsg);
        }

        internal async Task WaitForDataAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            int sleepTime = 0;
            while (!stream.DataAvailable)
            {
                await Task.Delay(WAIT_FOR_DATA_INTERVAL, cancellationToken);
                sleepTime += WAIT_FOR_DATA_INTERVAL;
                if (sleepTime > WAIT_FOR_DATA_TIMEOUT)
                {
                    throw new ProxyException("A timeout while waiting for the proxy destination to respond.");
                }
            }
        }
    }
}