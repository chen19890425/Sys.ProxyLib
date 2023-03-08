using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace Sys.ProxyLib.Helpers
{
    internal static class CookieUtils
    {
        internal static void SetCookie(this CookieContainer cookies, HttpRequestMessage request, string value)
        {
            if (value == null || value.Length == 0)
            {
                return;
            }

            var dict = value.Split(';')
                .Select(str => str.Trim().Split('='))
                .ToDictionary(kv => kv[0], kv => kv.Length > 1 ? kv[1] : null, StringComparer.InvariantCultureIgnoreCase);

            var keyValue = dict.First();
            var expires = dict.TryGet("Expires")?.ToDateTime();

            if (expires == null && int.TryParse(dict.TryGet("Max-Age"), out var maxAge))
            {
                expires = DateTime.Now.AddSeconds(maxAge);
            }

            if (expires != null && expires < DateTime.Now)
            {
                var cookie = cookies.GetCookies(request.RequestUri)[keyValue.Key];

                if (cookie != null)
                {
                    cookie.Expired = true;
                }
            }
            else
            {
                var isHttps = request.RequestUri.IsHttps();

                var cookie = new Cookie()
                {
                    Name = keyValue.Key,
                    Value = keyValue.Value,
                    Domain = request.RequestUri.Host,
                    Path = dict.TryGet("Path") ?? "/",
                    Port = dict.TryGet("Port"),
                    Discard = dict.ContainsKey("Discard"),
                    Secure = isHttps ? true : dict.ContainsKey("Secure"),
                    HttpOnly = isHttps ? false : dict.ContainsKey("HttpOnly")
                };

                if (expires != null)
                {
                    cookie.Expires = expires.Value;
                }

                cookies.Add(cookie);
            }
        }

        private static TValue TryGet<TKey, TValue>(this IDictionary<TKey, TValue> target, TKey key)
        {
            TValue @value = default(TValue);
            if (target != null) target.TryGetValue(key, out @value);
            return @value;
        }

        private static DateTime? ToDateTime(this string target, DateTime? @default = null)
        {
            DateTime date;
            if (DateTime.TryParse(target, out date)) return date;
            return @default;
        }
    }
}