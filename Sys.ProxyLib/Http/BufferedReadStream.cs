using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sys.ProxyLib.Http
{
    internal class BufferedReadStream : Stream
    {
        private const byte CR = (byte)'\r';
        private const byte LF = (byte)'\n';

        private readonly Stream _inner;
        private readonly byte[] _buffer;
        private int _bufferOffset = 0;
        private int _bufferCount = 0;
        private bool _disposed;
        private bool _leaveOpen;

        public BufferedReadStream(Stream inner, int bufferSize = 1024, bool leaveOpen = false)
        {
            if (inner == null)
            {
                throw new ArgumentNullException(nameof(inner));
            }

            _inner = inner;
            _leaveOpen = leaveOpen;
            _buffer = new byte[bufferSize];
        }

        public ArraySegment<byte> BufferedData
        {
            get { return new ArraySegment<byte>(_buffer, _bufferOffset, _bufferCount); }
        }

        public override bool CanRead
        {
            get { return _inner.CanRead || _bufferCount > 0; }
        }

        public override bool CanSeek
        {
            get { return _inner.CanSeek; }
        }

        public override bool CanTimeout
        {
            get { return _inner.CanTimeout; }
        }

        public override bool CanWrite
        {
            get { return _inner.CanWrite; }
        }

        public override long Length
        {
            get { return _inner.Length; }
        }

        public override long Position
        {
            get { return _inner.Position - _bufferCount; }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Position must be positive.");
                }
                if (value == Position)
                {
                    return;
                }

                // Backwards?
                if (value <= _inner.Position)
                {
                    // Forward within the buffer?
                    var innerOffset = (int)(_inner.Position - value);
                    if (innerOffset <= _bufferCount)
                    {
                        // Yes, just skip some of the buffered data
                        _bufferOffset += innerOffset;
                        _bufferCount -= innerOffset;
                    }
                    else
                    {
                        // No, reset the buffer
                        _bufferOffset = 0;
                        _bufferCount = 0;
                        _inner.Position = value;
                    }
                }
                else
                {
                    // Forward, reset the buffer
                    _bufferOffset = 0;
                    _bufferCount = 0;
                    _inner.Position = value;
                }
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin)
            {
                Position = offset;
            }
            else if (origin == SeekOrigin.Current)
            {
                Position = Position + offset;
            }
            else // if (origin == SeekOrigin.End)
            {
                Position = Length + offset;
            }
            return Position;
        }

        public override void SetLength(long value)
        {
            _inner.SetLength(value);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;

                if (disposing && !_leaveOpen)
                {
                    _inner.Dispose();
                }
            }
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _inner.FlushAsync(cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBuffer(buffer, offset, count);

            // Drain buffer
            if (_bufferCount > 0)
            {
                int toCopy = Math.Min(_bufferCount, count);
                Buffer.BlockCopy(_buffer, _bufferOffset, buffer, offset, toCopy);
                _bufferOffset += toCopy;
                _bufferCount -= toCopy;
                return toCopy;
            }

            return _inner.Read(buffer, offset, count);
        }

        public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBuffer(buffer, offset, count);

            // Drain buffer
            if (_bufferCount > 0)
            {
                int toCopy = Math.Min(_bufferCount, count);
                Buffer.BlockCopy(_buffer, _bufferOffset, buffer, offset, toCopy);
                _bufferOffset += toCopy;
                _bufferCount -= toCopy;
                return toCopy;
            }

            return await _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public bool EnsureBuffered()
        {
            if (_bufferCount > 0)
            {
                return true;
            }
            // Downshift to make room
            _bufferOffset = 0;
            _bufferCount = _inner.Read(_buffer, 0, _buffer.Length);
            return _bufferCount > 0;
        }

        public async Task<bool> EnsureBufferedAsync(CancellationToken cancellationToken)
        {
            if (_bufferCount > 0)
            {
                return true;
            }
            // Downshift to make room
            _bufferOffset = 0;
            _bufferCount = await _inner.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken);
            return _bufferCount > 0;
        }

        public bool EnsureBuffered(int minCount)
        {
            if (minCount > _buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(minCount), minCount, "The value must be smaller than the buffer size: " + _buffer.Length.ToString());
            }
            while (_bufferCount < minCount)
            {
                // Downshift to make room
                if (_bufferOffset > 0)
                {
                    if (_bufferCount > 0)
                    {
                        Buffer.BlockCopy(_buffer, _bufferOffset, _buffer, 0, _bufferCount);
                    }
                    _bufferOffset = 0;
                }
                int read = _inner.Read(_buffer, _bufferOffset + _bufferCount, _buffer.Length - _bufferCount - _bufferOffset);
                _bufferCount += read;
                if (read == 0)
                {
                    return false;
                }
            }
            return true;
        }

        public async Task<bool> EnsureBufferedAsync(int minCount, CancellationToken cancellationToken)
        {
            if (minCount > _buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(minCount), minCount, "The value must be smaller than the buffer size: " + _buffer.Length.ToString());
            }
            while (_bufferCount < minCount)
            {
                // Downshift to make room
                if (_bufferOffset > 0)
                {
                    if (_bufferCount > 0)
                    {
                        Buffer.BlockCopy(_buffer, _bufferOffset, _buffer, 0, _bufferCount);
                    }
                    _bufferOffset = 0;
                }
                int read = await _inner.ReadAsync(_buffer, _bufferOffset + _bufferCount, _buffer.Length - _bufferCount - _bufferOffset, cancellationToken);
                _bufferCount += read;
                if (read == 0)
                {
                    return false;
                }
            }
            return true;
        }

        public string ReadLine()
        {
            CheckDisposed();
            using (var builder = new MemoryStream(200))
            {
                bool foundCR = false, foundCRLF = false;

                while (!foundCRLF && EnsureBuffered())
                {
                    ProcessLineChar(builder, ref foundCR, ref foundCRLF);
                }

                return DecodeLine(builder, foundCRLF);
            }
        }

        public async Task<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            CheckDisposed();
            using (var builder = new MemoryStream(200))
            {
                bool foundCR = false, foundCRLF = false;

                while (!foundCRLF && await EnsureBufferedAsync(cancellationToken))
                {
                    ProcessLineChar(builder, ref foundCR, ref foundCRLF);
                }

                return DecodeLine(builder, foundCRLF);
            }
        }

        private void ProcessLineChar(MemoryStream builder, ref bool foundCR, ref bool foundCRLF)
        {
            var b = _buffer[_bufferOffset];
            builder.WriteByte(b);
            _bufferOffset++;
            _bufferCount--;
            if (b == LF && foundCR)
            {
                foundCRLF = true;
                return;
            }
            foundCR = b == CR;
        }

        private string DecodeLine(MemoryStream builder, bool foundCRLF)
        {
            // Drop the final CRLF, if any
            var length = foundCRLF ? builder.Length - 2 : builder.Length;
            return Encoding.UTF8.GetString(builder.ToArray(), 0, (int)length);
        }

        private void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BufferedReadStream));
            }
        }

        private void ValidateBuffer(byte[] buffer, int offset, int count)
        {
            // Delegate most of our validation.
            var ignored = new ArraySegment<byte>(buffer, offset, count);
            if (count == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "The value must be greater than zero.");
            }
        }
    }
}