using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sys.ProxyLib.Helpers;
using Sys.ProxyLib.Proxy.Exceptions;

namespace Sys.ProxyLib.Proxy
{
    public class Socks5ProxyClient : ProxyClientBase
    {
        private const string PROXY_NAME = "SOCKS5";
        internal const int SOCKS5_DEFAULT_PORT = 1080;

        private const byte SOCKS5_VERSION_NUMBER = 5;
        private const byte SOCKS5_RESERVED = 0x00;
        private const byte SOCKS5_AUTH_NUMBER_OF_AUTH_METHODS_SUPPORTED = 2;
        private const byte SOCKS5_AUTH_METHOD_NO_AUTHENTICATION_REQUIRED = 0x00;
        private const byte SOCKS5_AUTH_METHOD_GSSAPI = 0x01;
        private const byte SOCKS5_AUTH_METHOD_USERNAME_PASSWORD = 0x02;
        private const byte SOCKS5_AUTH_METHOD_IANA_ASSIGNED_RANGE_BEGIN = 0x03;
        private const byte SOCKS5_AUTH_METHOD_IANA_ASSIGNED_RANGE_END = 0x7f;
        private const byte SOCKS5_AUTH_METHOD_RESERVED_RANGE_BEGIN = 0x80;
        private const byte SOCKS5_AUTH_METHOD_RESERVED_RANGE_END = 0xfe;
        private const byte SOCKS5_AUTH_METHOD_REPLY_NO_ACCEPTABLE_METHODS = 0xff;
        private const byte SOCKS5_CMD_CONNECT = 0x01;
        private const byte SOCKS5_CMD_BIND = 0x02;
        private const byte SOCKS5_CMD_UDP_ASSOCIATE = 0x03;
        private const byte SOCKS5_CMD_REPLY_SUCCEEDED = 0x00;
        private const byte SOCKS5_CMD_REPLY_GENERAL_SOCKS_SERVER_FAILURE = 0x01;
        private const byte SOCKS5_CMD_REPLY_CONNECTION_NOT_ALLOWED_BY_RULESET = 0x02;
        private const byte SOCKS5_CMD_REPLY_NETWORK_UNREACHABLE = 0x03;
        private const byte SOCKS5_CMD_REPLY_HOST_UNREACHABLE = 0x04;
        private const byte SOCKS5_CMD_REPLY_CONNECTION_REFUSED = 0x05;
        private const byte SOCKS5_CMD_REPLY_TTL_EXPIRED = 0x06;
        private const byte SOCKS5_CMD_REPLY_COMMAND_NOT_SUPPORTED = 0x07;
        private const byte SOCKS5_CMD_REPLY_ADDRESS_TYPE_NOT_SUPPORTED = 0x08;
        private const byte SOCKS5_ADDRTYPE_IPV4 = 0x01;
        private const byte SOCKS5_ADDRTYPE_DOMAIN_NAME = 0x03;
        private const byte SOCKS5_ADDRTYPE_IPV6 = 0x04;

        private enum SocksAuthentication
        {
            None,
            UsernamePassword
        }

        public Socks5ProxyClient(string proxyHost, int proxyPort = SOCKS5_DEFAULT_PORT, string proxyUserName = null, string proxyPassword = null)
            : base(proxyHost, proxyPort)
        {
            ProxyUserName = proxyUserName;
            ProxyPassword = proxyPassword;
        }

        public override string ProxyName => PROXY_NAME;

        public string ProxyUserName { get; }

        public string ProxyPassword { get; }

        protected override async Task SendProxyCommandAsync(string destinationHost, int destinationPort, CancellationToken cancellationToken = default)
        {
            await NegotiateServerAuthMethodAsync(cancellationToken);

            await SendCommandAsync(SOCKS5_CMD_CONNECT, destinationHost, destinationPort, cancellationToken);
        }

        private SocksAuthentication DetermineClientAuthMethod()
        {
            if (ProxyUserName != null && ProxyPassword != null)
            {
                return SocksAuthentication.UsernamePassword;
            }
            else
            {
                return SocksAuthentication.None;
            }
        }

        private async Task NegotiateServerAuthMethodAsync(CancellationToken cancellationToken)
        {
            //  get a reference to the network stream
            NetworkStream stream = Client.GetStream();

            // SERVER AUTHENTICATION REQUEST
            // The client connects to the server, and sends a version
            // identifier/method selection message:
            //
            //      +----+----------+----------+
            //      |VER | NMETHODS | METHODS  |
            //      +----+----------+----------+
            //      | 1  |    1     | 1 to 255 |
            //      +----+----------+----------+

            byte[] authRequest = new byte[4];
            authRequest[0] = SOCKS5_VERSION_NUMBER;
            authRequest[1] = SOCKS5_AUTH_NUMBER_OF_AUTH_METHODS_SUPPORTED;
            authRequest[2] = SOCKS5_AUTH_METHOD_NO_AUTHENTICATION_REQUIRED;
            authRequest[3] = SOCKS5_AUTH_METHOD_USERNAME_PASSWORD;

            //  send the request to the server specifying authentication types supported by the client.
            await stream.WriteAsync(authRequest, 0, authRequest.Length, cancellationToken);

            //  SERVER AUTHENTICATION RESPONSE
            //  The server selects from one of the methods given in METHODS, and
            //  sends a METHOD selection message:
            //
            //     +----+--------+
            //     |VER | METHOD |
            //     +----+--------+
            //     | 1  |   1    |
            //     +----+--------+
            //
            //  If the selected METHOD is X'FF', none of the methods listed by the
            //  client are acceptable, and the client MUST close the connection.
            //
            //  The values currently defined for METHOD are:
            //   * X'00' NO AUTHENTICATION REQUIRED
            //   * X'01' GSSAPI
            //   * X'02' USERNAME/PASSWORD
            //   * X'03' to X'7F' IANA ASSIGNED
            //   * X'80' to X'FE' RESERVED FOR PRIVATE METHODS
            //   * X'FF' NO ACCEPTABLE METHODS

            //  receive the server response 
            byte[] response = new byte[2];
            await stream.ReadAsync(response, 0, response.Length, cancellationToken);

            //  the first byte contains the socks version number (e.g. 5)
            //  the second byte contains the auth method acceptable to the proxy server
            byte acceptedAuthMethod = response[1];

            // if the server does not accept any of our supported authenication methods then throw an error
            if (acceptedAuthMethod == SOCKS5_AUTH_METHOD_REPLY_NO_ACCEPTABLE_METHODS)
            {
                Dispose();
                throw new ProxyException("The proxy destination does not accept the supported proxy client authentication methods.");
            }

            SocksAuthentication proxyAuthMethod = DetermineClientAuthMethod();

            // if the server accepts a username and password authentication and none is provided by the user then throw an error
            if (acceptedAuthMethod == SOCKS5_AUTH_METHOD_USERNAME_PASSWORD && proxyAuthMethod == SocksAuthentication.None)
            {
                Dispose();
                throw new ProxyException("The proxy destination requires a username and password for authentication.  If you received this error attempting to connect to the Tor network provide an string empty value for ProxyUserName and ProxyPassword.");
            }

            if (acceptedAuthMethod == SOCKS5_AUTH_METHOD_USERNAME_PASSWORD)
            {

                // USERNAME / PASSWORD SERVER REQUEST
                // Once the SOCKS V5 server has started, and the client has selected the
                // Username/Password Authentication protocol, the Username/Password
                // subnegotiation begins.  This begins with the client producing a
                // Username/Password request:
                //
                //       +----+------+----------+------+----------+
                //       |VER | ULEN |  UNAME   | PLEN |  PASSWD  |
                //       +----+------+----------+------+----------+
                //       | 1  |  1   | 1 to 255 |  1   | 1 to 255 |
                //       +----+------+----------+------+----------+

                // create a data structure (binary array) containing credentials
                // to send to the proxy server which consists of clear username and password data
                byte[] credentials = new byte[ProxyUserName.Length + ProxyPassword.Length + 3];

                // for SOCKS5 username/password authentication the VER field must be set to 0x01
                //  http://en.wikipedia.org/wiki/SOCKS
                //      field 1: version number, 1 byte (must be 0x01)"
                credentials[0] = 0x01;
                credentials[1] = (byte)ProxyUserName.Length;
                Array.Copy(ASCIIEncoding.ASCII.GetBytes(ProxyUserName), 0, credentials, 2, ProxyUserName.Length);
                credentials[ProxyUserName.Length + 2] = (byte)ProxyPassword.Length;
                Array.Copy(ASCIIEncoding.ASCII.GetBytes(ProxyPassword), 0, credentials, ProxyUserName.Length + 3, ProxyPassword.Length);

                // USERNAME / PASSWORD SERVER RESPONSE
                // The server verifies the supplied UNAME and PASSWD, and sends the
                // following response:
                //
                //   +----+--------+
                //   |VER | STATUS |
                //   +----+--------+
                //   | 1  |   1    |
                //   +----+--------+
                //
                // A STATUS field of X'00' indicates success. If the server returns a
                // `failure' (STATUS value other than X'00') status, it MUST close the
                // connection.

                // transmit credentials to the proxy server
                await stream.WriteAsync(credentials, 0, credentials.Length, cancellationToken);

                // read the response from the proxy server
                byte[] crResponse = new byte[2];
                await stream.ReadAsync(crResponse, 0, crResponse.Length, cancellationToken);

                // check to see if the proxy server accepted the credentials
                if (crResponse[1] != 0)
                {
                    Dispose();
                    throw new ProxyException("Proxy authentification failure!  The proxy server has reported that the userid and/or password is not valid.");
                }
            }
        }

        private byte GetDestAddressType(string host)
        {
            IPAddress ipAddr = null;

            bool result = IPAddress.TryParse(host, out ipAddr);

            if (!result)
            {
                return SOCKS5_ADDRTYPE_DOMAIN_NAME;
            }

            switch (ipAddr.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    return SOCKS5_ADDRTYPE_IPV4;
                case AddressFamily.InterNetworkV6:
                    return SOCKS5_ADDRTYPE_IPV6;
                default:
                    throw new ProxyException(string.Format(CultureInfo.InvariantCulture, "The host addess {0} of type '{1}' is not a supported address type.  The supported types are InterNetwork and InterNetworkV6.", host, Enum.GetName(typeof(AddressFamily), ipAddr.AddressFamily)));
            }

        }

        private byte[] GetDestAddressBytes(byte addressType, string host)
        {
            switch (addressType)
            {
                case SOCKS5_ADDRTYPE_IPV4:
                case SOCKS5_ADDRTYPE_IPV6:
                    return IPAddress.Parse(host).GetAddressBytes();
                case SOCKS5_ADDRTYPE_DOMAIN_NAME:
                    //  create a byte array to hold the host name bytes plus one byte to store the length
                    byte[] bytes = new byte[host.Length + 1];
                    //  if the address field contains a fully-qualified domain name.  The first
                    //  octet of the address field contains the number of octets of name that
                    //  follow, there is no terminating NUL octet.
                    bytes[0] = Convert.ToByte(host.Length);
                    Encoding.ASCII.GetBytes(host).CopyTo(bytes, 1);
                    return bytes;
                default:
                    return null;
            }
        }

        private byte[] GetDestPortBytes(int value)
        {
            byte[] array = new byte[2];
            array[0] = Convert.ToByte(value / 256);
            array[1] = Convert.ToByte(value % 256);
            return array;
        }

        private async Task SendCommandAsync(byte command, string destinationHost, int destinationPort, CancellationToken cancellationToken)
        {
            NetworkStream stream = Client.GetStream();

            byte addressType = GetDestAddressType(destinationHost);
            byte[] destAddr = GetDestAddressBytes(addressType, destinationHost);
            byte[] destPort = GetDestPortBytes(destinationPort);

            //  The connection request is made up of 6 bytes plus the
            //  length of the variable address byte array
            //
            //  +----+-----+-------+------+----------+----------+
            //  |VER | CMD |  RSV  | ATYP | DST.ADDR | DST.PORT |
            //  +----+-----+-------+------+----------+----------+
            //  | 1  |  1  | X'00' |  1   | Variable |    2     |
            //  +----+-----+-------+------+----------+----------+
            //
            // * VER protocol version: X'05'
            // * CMD
            //   * CONNECT X'01'
            //   * BIND X'02'
            //   * UDP ASSOCIATE X'03'
            // * RSV RESERVED
            // * ATYP address itemType of following address
            //   * IP V4 address: X'01'
            //   * DOMAINNAME: X'03'
            //   * IP V6 address: X'04'
            // * DST.ADDR desired destination address
            // * DST.PORT desired destination port in network octet order            

            byte[] request = new byte[4 + destAddr.Length + 2];
            request[0] = SOCKS5_VERSION_NUMBER;
            request[1] = command;
            request[2] = SOCKS5_RESERVED;
            request[3] = addressType;
            destAddr.CopyTo(request, 4);
            destPort.CopyTo(request, 4 + destAddr.Length);

            // send connect request.
            await stream.WriteAsync(request, 0, request.Length, cancellationToken);

            //  PROXY SERVER RESPONSE
            //  +----+-----+-------+------+----------+----------+
            //  |VER | REP |  RSV  | ATYP | BND.ADDR | BND.PORT |
            //  +----+-----+-------+------+----------+----------+
            //  | 1  |  1  | X'00' |  1   | Variable |    2     |
            //  +----+-----+-------+------+----------+----------+
            //
            // * VER protocol version: X'05'
            // * REP Reply field:
            //   * X'00' succeeded
            //   * X'01' general SOCKS server failure
            //   * X'02' connection not allowed by ruleset
            //   * X'03' Network unreachable
            //   * X'04' Host unreachable
            //   * X'05' Connection refused
            //   * X'06' TTL expired
            //   * X'07' Command not supported
            //   * X'08' Address itemType not supported
            //   * X'09' to X'FF' unassigned
            // RSV RESERVED
            // ATYP address itemType of following address

            byte[] response = new byte[255];

            // read proxy server response
            await stream.ReadAsync(response, 0, response.Length, cancellationToken);

            byte replyCode = response[1];

            //  evaluate the reply code for an error condition
            if (replyCode != SOCKS5_CMD_REPLY_SUCCEEDED)
            {
                HandleProxyCommandError(response, destinationHost, destinationPort);
            }
        }

        private void HandleProxyCommandError(byte[] response, string destinationHost, int destinationPort)
        {
            string proxyErrorText;
            byte replyCode = response[1];
            byte addrType = response[3];
            string addr = "";
            short port = 0;

            switch (addrType)
            {
                case SOCKS5_ADDRTYPE_DOMAIN_NAME:
                    int addrLen = Convert.ToInt32(response[4]);
                    byte[] addrBytes = new byte[addrLen];
                    for (int i = 0; i < addrLen; i++)
                    {
                        addrBytes[i] = response[i + 5];
                    }
                    addr = ASCIIEncoding.ASCII.GetString(addrBytes);
                    byte[] portBytesDomain = new byte[2];
                    portBytesDomain[0] = response[6 + addrLen];
                    portBytesDomain[1] = response[5 + addrLen];
                    port = BitConverter.ToInt16(portBytesDomain, 0);
                    break;

                case SOCKS5_ADDRTYPE_IPV4:
                    byte[] ipv4Bytes = new byte[4];
                    for (int i = 0; i < 4; i++)
                    {
                        ipv4Bytes[i] = response[i + 4];
                    }
                    IPAddress ipv4 = new IPAddress(ipv4Bytes);
                    addr = ipv4.ToString();
                    byte[] portBytesIpv4 = new byte[2];
                    portBytesIpv4[0] = response[9];
                    portBytesIpv4[1] = response[8];
                    port = BitConverter.ToInt16(portBytesIpv4, 0);
                    break;

                case SOCKS5_ADDRTYPE_IPV6:
                    byte[] ipv6Bytes = new byte[16];
                    for (int i = 0; i < 16; i++)
                    {
                        ipv6Bytes[i] = response[i + 4];
                    }
                    IPAddress ipv6 = new IPAddress(ipv6Bytes);
                    addr = ipv6.ToString();
                    byte[] portBytesIpv6 = new byte[2];
                    portBytesIpv6[0] = response[21];
                    portBytesIpv6[1] = response[20];
                    port = BitConverter.ToInt16(portBytesIpv6, 0);
                    break;
            }


            switch (replyCode)
            {
                case SOCKS5_CMD_REPLY_GENERAL_SOCKS_SERVER_FAILURE:
                    proxyErrorText = "a general socks destination failure occurred";
                    break;
                case SOCKS5_CMD_REPLY_CONNECTION_NOT_ALLOWED_BY_RULESET:
                    proxyErrorText = "the connection is not allowed by proxy destination rule set";
                    break;
                case SOCKS5_CMD_REPLY_NETWORK_UNREACHABLE:
                    proxyErrorText = "the network was unreachable";
                    break;
                case SOCKS5_CMD_REPLY_HOST_UNREACHABLE:
                    proxyErrorText = "the host was unreachable";
                    break;
                case SOCKS5_CMD_REPLY_CONNECTION_REFUSED:
                    proxyErrorText = "the connection was refused by the remote network";
                    break;
                case SOCKS5_CMD_REPLY_TTL_EXPIRED:
                    proxyErrorText = "the time to live (TTL) has expired";
                    break;
                case SOCKS5_CMD_REPLY_COMMAND_NOT_SUPPORTED:
                    proxyErrorText = "the command issued by the proxy client is not supported by the proxy destination";
                    break;
                case SOCKS5_CMD_REPLY_ADDRESS_TYPE_NOT_SUPPORTED:
                    proxyErrorText = "the address type specified is not supported";
                    break;
                default:
                    proxyErrorText = string.Format(CultureInfo.InvariantCulture, "an unknown SOCKS reply with the code value '{0}' was received", replyCode.ToString(CultureInfo.InvariantCulture));
                    break;
            }
            string responseText = response != null ? ArrayUtils.HexEncode(response) : "";
            string exceptionMsg = string.Format(CultureInfo.InvariantCulture, "proxy error: {0} for destination host {1} port number {2}.  Server response (hex): {3}.", proxyErrorText, destinationHost, destinationPort, responseText);

            throw new ProxyException(exceptionMsg);
        }
    }
}