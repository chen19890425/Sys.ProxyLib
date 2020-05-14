using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Sys.ProxyLib.Http
{
    internal class HttpConnectionResponseContent : HttpContent
    {
        private readonly HttpConnection _connection;
        private Stream _responseStream;

        public HttpConnectionResponseContent(HttpConnection connection)
        {
            _connection = connection;
        }

        public void ResolveResponseStream(bool chunked, string contentEncoding)
        {
            if (_responseStream != null)
            {
                throw new InvalidOperationException("Called multiple times");
            }
            if (chunked)
            {
                _responseStream = new ChunkedReadStream(_connection.Transport);
            }
            else if (Headers.ContentLength.HasValue)
            {
                _responseStream = new ContentLengthReadStream(_connection.Transport, Headers.ContentLength.Value);
            }
            else
            {
                _responseStream = _connection.Transport;
            }

            if (!string.IsNullOrEmpty(contentEncoding))
            {
                switch (contentEncoding.ToLower())
                {
                    case "gzip":
                        _responseStream = new GzipDecompressStreamWrapper(_responseStream, false);
                        Headers.Remove("Content-Encoding");
                        break;
                    case "deflate":
                        _responseStream = new DeflateDecompressStreamWrapper(_responseStream, false);
                        Headers.Remove("Content-Encoding");
                        break;
                }
            }
        }

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext context)
        {
            using (_connection.PooledClient)
            {
                await _responseStream.CopyToAsync(stream);
            }
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return Task.FromResult(_responseStream);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            _responseStream.Dispose();
        }
    }
}