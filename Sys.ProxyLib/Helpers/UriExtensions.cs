using System;

namespace Sys.ProxyLib.Helpers
{
    internal static class UriExtensions
    {
        public static bool IsHttps(this Uri uri)
        {
            return string.Equals("https", uri.Scheme, StringComparison.OrdinalIgnoreCase);
        }
    }
}