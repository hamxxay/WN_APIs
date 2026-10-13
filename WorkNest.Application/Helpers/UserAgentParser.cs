namespace WorkNest.Application.Helpers
{
    public static class UserAgentParser
    {
        public static string ParseSummary(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return "Unknown device";

            var ua = userAgent.Trim();

            // Detect OS
            string os = "Unknown OS";
            if (ua.Contains("Windows NT 10.0", StringComparison.OrdinalIgnoreCase)) os = "Windows 10/11";
            else if (ua.Contains("Windows NT 6.3", StringComparison.OrdinalIgnoreCase)) os = "Windows 8.1";
            else if (ua.Contains("Windows NT 6.1", StringComparison.OrdinalIgnoreCase)) os = "Windows 7";
            else if (ua.Contains("Windows", StringComparison.OrdinalIgnoreCase)) os = "Windows";
            else if (ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase)) os = "iPhone (iOS)";
            else if (ua.Contains("iPad", StringComparison.OrdinalIgnoreCase)) os = "iPad (iOS)";
            else if (ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) || ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase)) os = "macOS";
            else if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase)) os = "Android";
            else if (ua.Contains("Linux", StringComparison.OrdinalIgnoreCase)) os = "Linux";
            else if (ua.Contains("CrOS", StringComparison.OrdinalIgnoreCase)) os = "ChromeOS";

            // Detect Browser
            string browser = "Browser";
            if (ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) || ua.Contains("Edge/", StringComparison.OrdinalIgnoreCase)) browser = "Edge";
            else if (ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) && !ua.Contains("Edg", StringComparison.OrdinalIgnoreCase)) browser = "Chrome";
            else if (ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase)) browser = "Firefox";
            else if (ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) && !ua.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) browser = "Safari";
            else if (ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase) || ua.Contains("Opera", StringComparison.OrdinalIgnoreCase)) browser = "Opera";
            else if (ua.Contains("PostmanRuntime", StringComparison.OrdinalIgnoreCase)) browser = "Postman";
            else if (ua.Contains("curl", StringComparison.OrdinalIgnoreCase)) browser = "cURL";

            return $"{browser} on {os}";
        }
    }
}
