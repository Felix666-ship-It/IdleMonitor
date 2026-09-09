using System;
using System.Net;

namespace IdleMonitor
{
    internal static class ApiUrlValidator
    {
        public static bool TryValidate(string value, out string normalized, out string error)
        {
            normalized = (value ?? string.Empty).Trim();
            error = null;
            Uri uri;
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && !IsLocalHttp(uri)) ||
                string.IsNullOrEmpty(uri.Host))
            {
                error = "API 地址必须是有效的 HTTPS URL；仅允许 localhost 使用 HTTP。";
                return false;
            }
            if (uri.UserInfo.Length > 0)
            {
                error = "API 地址不能包含用户名或密码。";
                return false;
            }
            normalized = uri.AbsoluteUri.TrimEnd('/');
            return true;
        }

        private static bool IsLocalHttp(Uri uri)
        {
            return uri.Scheme == Uri.UriSchemeHttp &&
                (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                 IPAddress.TryParse(uri.Host, out IPAddress ip) && IPAddress.IsLoopback(ip));
        }
    }
}
