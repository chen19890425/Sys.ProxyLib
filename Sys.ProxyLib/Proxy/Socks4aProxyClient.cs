using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sys.ProxyLib.Proxy
{
    public class Socks4aProxyClient : Socks4ProxyClient
    {
        private const string PROXY_NAME = "SOCKS4a";

        public Socks4aProxyClient(string proxyHost, int proxyPort = Socks4ProxyClient.SOCKS_PROXY_DEFAULT_PORT, string proxyUserId = null)
            : base(proxyHost, proxyPort, proxyUserId)
        {

        }

        public override string ProxyName => PROXY_NAME;

        internal override async Task SendCommandAsync(NetworkStream proxy, byte command, string destinationHost, int destinationPort, string userId, CancellationToken cancellationToken)
        {
            // PROXY SERVER REQUEST
            //Please read SOCKS4.protocol first for an description of the version 4
            //protocol. This extension is intended to allow the use of SOCKS on hosts
            //which are not capable of resolving all domain names.
            //
            //In version 4, the client sends the following packet to the SOCKS server
            //to request a CONNECT or a BIND operation:
            //
            //        +----+----+----+----+----+----+----+----+----+----+....+----+
            //        | VN | CD | DSTPORT |      DSTIP        | USERID       |NULL|
            //        +----+----+----+----+----+----+----+----+----+----+....+----+
            // # of bytes:	   1    1      2              4           variable       1
            //
            //VN is the SOCKS protocol version number and should be 4. CD is the
            //SOCKS command code and should be 1 for CONNECT or 2 for BIND. NULL
            //is a byte of all zero bits.
            //
            //For version 4A, if the client cannot resolve the destination host's
            //domain name to find its IP address, it should set the first three bytes
            //of DSTIP to NULL and the last byte to a non-zero value. (This corresponds
            //to IP address 0.0.0.x, with x nonzero. As decreed by IANA  -- The
            //Internet Assigned Numbers Authority -- such an address is inadmissible
            //as a destination IP address and thus should never occur if the client
            //can resolve the domain name.) Following the NULL byte terminating
            //USERID, the client must sends the destination domain name and termiantes
            //it with another NULL byte. This is used for both CONNECT and BIND requests.
            //
            //A server using protocol 4A must check the DSTIP in the request packet.
            //If it represent address 0.0.0.x with nonzero x, the server must read
            //in the domain name that the client sends in the packet. The server
            //should resolve the domain name and make connection to the destination
            //host if it can. 
            //
            //SOCKSified sockd may pass domain names that it cannot resolve to
            //the next-hop SOCKS server.    

            //  userId needs to be a zero length string so that the GetBytes method
            //  works properly
            if (userId == null)
            {
                userId = "";
            }

            byte[] destIp = { 0, 0, 0, 1 };  // build the invalid ip address as specified in the 4a protocol
            byte[] destPort = GetDestinationPortBytes(destinationPort);
            byte[] userIdBytes = ASCIIEncoding.ASCII.GetBytes(userId);
            byte[] hostBytes = ASCIIEncoding.ASCII.GetBytes(destinationHost);
            byte[] request = new byte[10 + userIdBytes.Length + hostBytes.Length];

            //  set the bits on the request byte array
            request[0] = SOCKS4_VERSION_NUMBER;
            request[1] = command;
            destPort.CopyTo(request, 2);
            destIp.CopyTo(request, 4);
            userIdBytes.CopyTo(request, 8);  // copy the userid to the request byte array
            request[8 + userIdBytes.Length] = 0x00;  // null (byte with all zeros) terminator for userId
            hostBytes.CopyTo(request, 9 + userIdBytes.Length);  // copy the host name to the request byte array
            request[9 + userIdBytes.Length + hostBytes.Length] = 0x00;  // null (byte with all zeros) terminator for userId

            // send the connect request
            await proxy.WriteAsync(request, 0, request.Length, cancellationToken);

            // wait for the proxy server to send a response
            await base.WaitForDataAsync(proxy, cancellationToken);

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
    }
}