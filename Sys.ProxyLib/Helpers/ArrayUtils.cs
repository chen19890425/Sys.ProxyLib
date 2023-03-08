using System;
using System.Globalization;
using System.Text;

namespace Sys.ProxyLib.Helpers
{
    internal enum ParityOptions
    {
        Odd,
        Even
    };

    internal static class ArrayUtils
    {

        /// <summary>
        /// Encodes a byte array to a string in 2 character hex format.
        /// </summary>
        /// <param name="data">Array of bytes to convert.</param>
        /// <returns>String containing encoded bytes.</returns>
        /// <remarks>e.g. 0x55 ==> "55", also left pads with 0 so that 0x01 is "01" and not "1"</remarks>
        public static string HexEncode(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return HexEncode(data, false, data.Length);
        }

        /// <summary>
        /// Encodes a byte array to a string in 2 character hex format.
        /// </summary>
        /// <param name="data">Array of bytes to encode.</param>
        /// <param name="insertColonDelimiter">Insert colon as the delimiter between bytes.</param>
        /// <param name="length">Number of bytes to encode.</param>
        /// <returns>String containing encoded bytes.</returns>
        /// <remarks>e.g. 0x55 ==> "55", also left pads with 0 so that 0x01 is "01" and not "1"</remarks>
        public static string HexEncode(byte[] data, bool insertColonDelimiter, int length)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            StringBuilder buffer = new StringBuilder(length * 2);

            int len = data.Length;
            for (int i = 0; i < len; i++)
            {
                buffer.Append(data[i].ToString("x").PadLeft(2, '0')); //same as "%02X" in C
                if (insertColonDelimiter && i < len - 1)
                    buffer.Append(':');
            }
            return buffer.ToString();
        }

        /// <summary>
        /// Decodes a 2 character hex format string to a byte array.
        /// </summary>
        /// <param name="s">String containing hex values to decode..</param>
        /// <returns>Array of decoded bytes.</returns>
        /// <remarks>Input string may contain a ':' delimiter between each encoded byte pair.</remarks>
        public static byte[] HexDecode(string s)
        {
            return HexDecode(s, 0);
        }

        /// <summary>
        /// Decodes a 2 character hex format string to a byte array.
        /// </summary>
        /// <param name="s">String containing hex values to decode..</param>
        /// <param name="paddingBytes">Number of most significant byte padding to add.</param>
        /// <returns>Array of decoded bytes.</returns>
        /// <remarks>Input string may contain a ':' delimiter between each encoded byte pair.</remarks>
        public static byte[] HexDecode(string s, int paddingBytes)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            if (s.IndexOf(':') > -1)
                s = s.Replace(":", "");

            if ((s.Length % 2) != 0)
            {
                throw new FormatException("parameter 's' must have an even number of hex characters");
            }

            byte[] result = new byte[s.Length / 2 + paddingBytes];
            for (int i = 0; i < result.Length - paddingBytes; i++)
            {
                result[i] = byte.Parse(s.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier);
            }
            return result;
        }

        /// <summary>
        /// Copies a byte array by creating a new array and transferring the values.
        /// </summary>
        /// <param name="array">Byte array to clone.</param>
        /// <returns>Cloned array.</returns>
        public static byte[] Clone(byte[] array)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            byte[] buffer = new byte[array.Length];
            Array.Copy(array, buffer, buffer.Length);
            return buffer;
        }

        /// <summary>
        /// Retrieves a substring from this instance. The substring starts at a specified
        /// character position and has a specified length.
        /// </summary>
        /// <param name="array"></param>
        /// <param name="startIndex">The index of the start of the substring.</param>
        /// <param name="length">The number of characters in the substring.</param>
        /// <returns>
        /// A Byte array equivalent to the substring of length that begins
        /// at startIndex.
        ///</returns>
        public static byte[] Subarray(byte[] array, int startIndex, int length)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (length > array.Length)
            {
                throw new Exception("length exceeds size of array");
            }

            byte[] buf = new byte[length];
            Array.Copy(array, startIndex, buf, 0, length);
            return buf;
        }
    }
}