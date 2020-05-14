using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Sys.ProxyLib.Http
{
    internal class DeflateDecompressStreamWrapper : DeflateStream
    {
        public DeflateDecompressStreamWrapper(Stream stream, bool leaveOpen)
            : base(stream, CompressionMode.Decompress, leaveOpen)
        {

        }

        public override int Read(byte[] array, int offset, int count)
        {
            byte[] buffer = new byte[64];
            int length = base.Read(array, offset, count);

            if (length == 0)
            {
                while (true)
                {
                    var len = BaseStream.Read(buffer, 0, buffer.Length);

                    if (len == 0)
                    {
                        break;
                    }
                }
            }

            return length;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            byte[] array = new byte[64];
            int length = await base.ReadAsync(buffer, offset, count, cancellationToken);

            if (length == 0)
            {
                while (true)
                {
                    var len = await BaseStream.ReadAsync(array, 0, array.Length);

                    if (len == 0)
                    {
                        break;
                    }
                }
            }

            return length;
        }
    }
}