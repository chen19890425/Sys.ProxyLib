using System;

namespace Sys.ProxyLib.Helpers
{
    internal class ArrayBuilder
    {
        private byte[] _buffer;
        private long _index;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="size">The fixed size of the one-dimensional byte array to build.</param>
        public ArrayBuilder(long size)
        {
            _buffer = new byte[size];
        }

        /// <summary>
        /// Gets the length of the ArrayBuilder buffer in bytes.
        /// </summary>
        public int Length
        {
            get { return _buffer.Length; }
        }

        /// <summary>
        /// Appends bytes to the byte array.
        /// </summary>
        /// <param name="data">Bytes to append.</param>
        public void Append(params byte[] data)
        {
            Append(data, 0);
        }

        /// <summary>
        /// Appends bytes to the byte array at a specific starting index.
        /// </summary>
        /// <param name="data">Bytes to append.</param>
        /// <param name="startIndex">Starting point to append bytes.</param>
        public void Append(byte[] data, long startIndex)
        {
            if (_index + data.Length - startIndex > _buffer.Length)
            {
                throw new Exception(String.Format("Data is too large to append.  Current size is {0} bytes.", _buffer.Length.ToString()));
            }

            Array.Copy(data, startIndex, _buffer, _index, data.Length - startIndex);
            _index += data.Length - startIndex;
        }

        /// <summary>
        /// Returns the byte array.
        /// </summary>
        /// <returns>Array of bytes.</returns>
        public byte[] GetBytes()
        {
            // we don't want to return a pointer to our internal array 
            // because the user might then modify the array and in return
            // modify the private _buffer variable.  This is an issue.
            // So we need to return a copy of the array instead.
            byte[] copy = new byte[_buffer.Length];
            Array.Copy(_buffer, copy, _buffer.Length);
            return copy;
        }

        /// <summary>
        /// Clears the bytes array.  Any appended data will be lost.  The original byte array size is preserved.
        /// </summary>
        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _index = 0;
        }

        /// <summary>
        /// Creates a new byte array of the size specificed.  Any appended data will be lost.
        /// </summary>
        /// <param name="size">Size to rediminsion the array.</param>
        public void Redim(long size)
        {
            _buffer = new byte[size];
            _index = 0;
        }
    }
}