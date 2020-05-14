using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sys.ProxyLib.Proxy.Exceptions;

namespace Sys.ProxyLib.Proxy
{
    public class HttpProxyClient : ProxyClientBase
    {
        private string _proxyUsername;
        private string _proxyPassword;
        private HttpResponseCodes _respCode;
        private string _respText;

        internal const int HTTP_PROXY_DEFAULT_PORT = 8080;
        private const string HTTP_PROXY_CONNECT_CMD = "CONNECT {0}:{1} HTTP/1.0\r\nHost: {0}:{1}\r\n\r\n";
        private const string HTTP_PROXY_AUTHENTICATE_CMD = "CONNECT {0}:{1} HTTP/1.0\r\nHost: {0}:{1}\r\nProxy-Authorization: Basic {2}\r\n\r\n";

        private const int WAIT_FOR_DATA_INTERVAL = 50; // 50 ms
        private const int WAIT_FOR_DATA_TIMEOUT = 15000; // 15 seconds
        private const string PROXY_NAME = "HTTP";

        private enum HttpResponseCodes
        {
            None = 0,
            Continue = 100,
            SwitchingProtocols = 101,
            OK = 200,
            Created = 201,
            Accepted = 202,
            NonAuthoritiveInformation = 203,
            NoContent = 204,
            ResetContent = 205,
            PartialContent = 206,
            MultipleChoices = 300,
            MovedPermanetly = 301,
            Found = 302,
            SeeOther = 303,
            NotModified = 304,
            UserProxy = 305,
            TemporaryRedirect = 307,
            BadRequest = 400,
            Unauthorized = 401,
            PaymentRequired = 402,
            Forbidden = 403,
            NotFound = 404,
            MethodNotAllowed = 405,
            NotAcceptable = 406,
            ProxyAuthenticantionRequired = 407,
            RequestTimeout = 408,
            Conflict = 409,
            Gone = 410,
            PreconditionFailed = 411,
            RequestEntityTooLarge = 413,
            RequestURITooLong = 414,
            UnsupportedMediaType = 415,
            RequestedRangeNotSatisfied = 416,
            ExpectationFailed = 417,
            InternalServerError = 500,
            NotImplemented = 501,
            BadGateway = 502,
            ServiceUnavailable = 503,
            GatewayTimeout = 504,
            HTTPVersionNotSupported = 505
        }

        public HttpProxyClient(string proxyHost, int proxyPort = HTTP_PROXY_DEFAULT_PORT, string proxyUsername = null, string proxyPassword = null)
            : base(proxyHost, proxyPort)
        {
            _proxyUsername = proxyUsername;
            _proxyPassword = proxyPassword;
        }

        public override string ProxyName => PROXY_NAME;

        protected override async Task SendProxyCommandAsync(string destinationHost, int destinationPort, CancellationToken cancellationToken = default(CancellationToken))
        {
            await SendConnectionCommandAsync(destinationHost, destinationPort, cancellationToken);
        }

        private async Task SendConnectionCommandAsync(string host, int port, CancellationToken cancellationToken)
        {
            NetworkStream stream = Client.GetStream();

            string connectCmd = CreateCommandString(host, port);

            byte[] request = ASCIIEncoding.ASCII.GetBytes(connectCmd);

            // send the connect request
            await stream.WriteAsync(request, 0, request.Length, cancellationToken);

            // wait for the proxy server to respond
            await WaitForDataAsync(stream, host, port, cancellationToken);

            // PROXY SERVER RESPONSE
            // =======================================================================
            //HTTP/1.0 200 Connection Established<CR><LF>
            //[.... other HTTP header lines ending with <CR><LF>..
            //ignore all of them]
            //<CR><LF>    // Last Empty Line

            // create an byte response array  
            byte[] response = new byte[Client.ReceiveBufferSize];
            StringBuilder sbuilder = new StringBuilder();
            int bytes = 0;
            long total = 0;

            do
            {
                bytes = await stream.ReadAsync(response, 0, Client.ReceiveBufferSize, cancellationToken);
                total += bytes;
                sbuilder.Append(ASCIIEncoding.UTF8.GetString(response, 0, bytes));
            } while (stream.DataAvailable);

            ParseResponse(sbuilder.ToString());

            //  evaluate the reply code for an error condition
            if (_respCode != HttpResponseCodes.OK)
            {
                HandleProxyCommandError(Client, host, port);
            }
        }

        private string CreateCommandString(string host, int port)
        {
            string connectCmd;
            if (!string.IsNullOrEmpty(_proxyUsername))
            {
                //  gets the user/pass into base64 encoded string in the form of [username]:[password]
                string auth = Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", _proxyUsername, _proxyPassword)));

                // PROXY SERVER REQUEST
                // =======================================================================
                //CONNECT starksoft.com:443 HTTP/1.0<CR><LF>
                //HOST starksoft.com:443<CR><LF>
                //Proxy-Authorization: username:password<CR><LF>
                //              NOTE: username:password string will be base64 encoded as one 
                //                        concatenated string
                //[... other HTTP header lines ending with <CR><LF> if required]>
                //<CR><LF>    // Last Empty Line
                connectCmd = string.Format(CultureInfo.InvariantCulture, HTTP_PROXY_AUTHENTICATE_CMD, host, port.ToString(CultureInfo.InvariantCulture), auth);
            }
            else
            {
                // PROXY SERVER REQUEST
                // =======================================================================
                //CONNECT starksoft.com:443 HTTP/1.0 <CR><LF>
                //HOST starksoft.com:443<CR><LF>
                //[... other HTTP header lines ending with <CR><LF> if required]>
                //<CR><LF>    // Last Empty Line
                connectCmd = string.Format(CultureInfo.InvariantCulture, HTTP_PROXY_CONNECT_CMD, host, port.ToString(CultureInfo.InvariantCulture));
            }

            return connectCmd;
        }

        private void HandleProxyCommandError(TcpClient tcpClient, string host, int port)
        {
            string msg;

            switch (_respCode)
            {
                case HttpResponseCodes.None:
                    msg = string.Format(CultureInfo.InvariantCulture, "Proxy destination {0} on port {1} failed to return a recognized HTTP response code.  Server response: {2}", host, port, _respText);
                    break;

                case HttpResponseCodes.BadGateway:
                    //HTTP/1.1 502 Proxy Error (The specified Secure Sockets Layer (SSL) port is not allowed. ISA Server is not configured to allow SSL requests from this port. Most Web browsers use port 443 for SSL requests.)
                    msg = string.Format(CultureInfo.InvariantCulture, "Proxy destination {0} on port {1} responded with a 502 code - Bad Gateway.  If you are connecting to a Microsoft ISA destination please refer to knowledge based article Q283284 for more information.  Server response: {2}", host, port, _respText);
                    break;

                default:
                    msg = string.Format(CultureInfo.InvariantCulture, "Proxy destination {0} on port {1} responded with a {2} code - {3}", host, port, ((int)_respCode).ToString(CultureInfo.InvariantCulture), _respText);
                    break;
            }

            //  throw a new application exception 
            throw new ProxyException(msg);
        }

        private async Task WaitForDataAsync(NetworkStream stream, string host, int port, CancellationToken cancellationToken)
        {
            int sleepTime = 0;
            while (!stream.DataAvailable)
            {
                await Task.Delay(WAIT_FOR_DATA_INTERVAL, cancellationToken);
                sleepTime += WAIT_FOR_DATA_INTERVAL;
                if (sleepTime > WAIT_FOR_DATA_TIMEOUT)
                {
                    throw new ProxyException(String.Format("A timeout while waiting for the proxy server at {0} on port {1} to respond.", host, port));
                }
            }
        }

        private void ParseResponse(string response)
        {
            string[] data = null;

            //  get rid of the LF character if it exists and then split the string on all CR
            data = response.Replace('\n', ' ').Split('\r');

            ParseCodeAndText(data[0]);
        }

        private void ParseCodeAndText(string line)
        {
            int begin = 0;
            int end = 0;
            string val = null;

            if (line.IndexOf("HTTP") == -1)
            {
                throw new ProxyException(String.Format("No HTTP response received from proxy destination.  Server response: {0}.", line));
            }

            begin = line.IndexOf(" ") + 1;
            end = line.IndexOf(" ", begin);

            val = line.Substring(begin, end - begin);
            int code = 0;

            if (!int.TryParse(val, out code))
            {
                throw new ProxyException(string.Format("An invalid response code was received from proxy destination.  Server response: {0}.", line));
            }

            _respCode = (HttpResponseCodes)code;
            _respText = line.Substring(end + 1).Trim();
        }
    }
}