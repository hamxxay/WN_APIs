using System.Text.RegularExpressions;

namespace WorkNest.API.Extensions
{
    /// <summary>
    /// Turns technical error text (SQL Server / .NET messages such as "Invalid column name", "Cannot find either
    /// column or the user-defined function", "Object reference not set", internal file paths, or IP addresses)
    /// into a message fit for the screen.
    /// Business messages (validation, RAISERROR text written for users) are left exactly as they are.
    /// </summary>
    public static class UserErrorText
    {
        public const string Generic = "Something went wrong on our side. Please try again, or contact support if it keeps happening.";

        private static readonly Regex FilePathRegex = new(
            @"(?:[a-zA-Z]:[/\\][^\s""'<>]+|/(?:var|tmp|home|app|etc|opt|usr|bin|lib)/[^\s""'<>]+|\\\\[a-zA-Z0-9_.-]+\\[^\s""'<>]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex IpAddressRegex = new(
            @"\b(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?\b",
            RegexOptions.Compiled);

        // Specific friendly messages, checked first (first match wins).
        private static readonly (string Pattern, string Message)[] Known =
        {
            ("Violation of PRIMARY KEY", "This record already exists."),
            ("Violation of UNIQUE KEY", "This record already exists."),
            ("Cannot insert duplicate key", "This record already exists."),
            ("REFERENCE constraint", "This item is linked to other records, so it can't be changed or removed."),
            ("FOREIGN KEY constraint", "This item is linked to other records, so it can't be changed or removed."),
            ("Timeout expired", "The server took too long to respond. Please try again."),
            ("Execution Timeout", "The server took too long to respond. Please try again."),
            ("deadlock", "The system was busy. Please try again."),
            ("would be truncated", "One of the values entered is too long."),
            ("Cannot insert the value NULL", "A required value is missing."),
            ("A network-related", "The database could not be reached. Please try again shortly."),
            ("Login failed for user", "The database could not be reached. Please try again shortly."),
            ("A connection was successfully established", "The database could not be reached. Please try again shortly."),
            ("actively refused", "The external service could not be reached. Please try again shortly."),
            ("Connection refused", "The external service could not be reached. Please try again shortly."),
            ("timed out after", "The service took too long to respond. Please try again."),
            ("HttpRequestException", "The external service could not be reached. Please try again shortly."),
            ("SocketException", "The external service could not be reached. Please try again shortly."),
            ("Console API", "The network management service could not be reached. Please try again shortly."),
            ("UniFi:ApiKey", "The network management service is not configured properly."),
            ("No UniFi console", "The network management service could not be reached. Please try again shortly."),
            ("Could not find file", "The requested document or template could not be found."),
            ("Could not find a part of the path", "The requested document or template could not be found."),
            ("Access to the path", "Access to the requested file was denied."),
            ("UnauthorizedAccessException", "Access to the requested file was denied."),
            ("FileNotFoundException", "The requested document or template could not be found."),
            ("DirectoryNotFoundException", "The requested document or template could not be found."),
        };

        // Text that only ever comes from SQL Server or .NET internals, never from a message written for users.
        private static readonly string[] Technical =
        {
            "Invalid column name", "Invalid object name", "Cannot find either column", "user-defined function",
            "Could not find stored procedure", "Procedure or function", "expects parameter", "too many arguments",
            "Must declare the scalar variable", "Incorrect syntax near", "Ambiguous column name", "is not a recognized",
            "Conversion failed", "Error converting data type", "Arithmetic overflow", "Divide by zero",
            "multi-part identifier", "Column name or number of supplied values", "Invalid length parameter",
            "Subquery returned more than 1 value", "Operand type clash", "statement conflicted", "permission was denied",
            "Transaction count after EXECUTE", "Object reference not set", "Sequence contains", "Index was outside",
            "Input string was not in a correct format", "Unable to cast object", "Nullable object must have a value",
            "The given key", "Value cannot be null", "Specified argument was out of the range", "SqlException",
            "Microsoft.Data.SqlClient", "System.Data", "System.NullReference", "System.InvalidCast", "stack trace",
            "WebException", "Network is unreachable", "Host is unreachable", "No route to host", "Unknown WAN",
            "System.IO", "IOException", "PathTooLongException", "QuestPDF", "SkiaSharp", "HarfBuzzSharp",
            "PDF generation failed"
        };

        /// <summary>True when <paramref name="message"/> is technical text that should not reach the screen.</summary>
        public static bool IsTechnical(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            foreach (var (pattern, _) in Known)
                if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var pattern in Technical)
                if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return true;
            if (FilePathRegex.IsMatch(message)) return true;
            if (IpAddressRegex.IsMatch(message)) return true;
            return false;
        }

        /// <summary>The message to show: a friendly one for technical text, otherwise the message unchanged.</summary>
        public static string? ForUser(string? message)
        {
            if (!IsTechnical(message)) return message;
            foreach (var (pattern, friendly) in Known)
                if (message!.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return friendly;
            if (FilePathRegex.IsMatch(message!))
                return "The requested document or template could not be accessed.";
            if (IpAddressRegex.IsMatch(message!))
                return "The service could not be reached. Please try again shortly.";
            return Generic;
        }
    }
}
