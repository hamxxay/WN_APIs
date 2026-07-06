namespace WorkNest.Common.Responses
{
    /// <summary>
    /// Universal API response wrapper.
    /// Mirrors the Python ok() / error response format exactly:
    /// { isSuccessful, message, data } for success
    /// { isSuccessful, message, errors } for failure
    /// Every endpoint in the solution returns this type.
    /// </summary>
    public class ApiResponse<T>
    {
        /// <summary>Indicates whether the operation succeeded.</summary>
        public bool IsSuccessful { get; set; }

        /// <summary>Human-readable message describing the result.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Response payload. Null on failure.</summary>
        public T? Data { get; set; }

        /// <summary>Validation or error details. Populated only on failure.</summary>
        public IEnumerable<string>? Errors { get; set; }

        // ── Factory helpers ───────────────────────────────────────────────────

        /// <summary>Creates a successful response with data and an optional message.</summary>
        public static ApiResponse<T> Ok(T data, string message = "Success") =>
            new() { IsSuccessful = true, Message = message, Data = data };

        /// <summary>Creates a successful response with no data payload.</summary>
        public static ApiResponse<T> Ok(string message = "Success") =>
            new() { IsSuccessful = true, Message = message };

        /// <summary>Creates a failure response with a message and optional error list.</summary>
        public static ApiResponse<T> Fail(string message, IEnumerable<string>? errors = null) =>
            new() { IsSuccessful = false, Message = message, Errors = errors };
    }

    /// <summary>
    /// Non-generic convenience alias used when the response carries no typed payload
    /// (e.g. delete, status-update, logout endpoints).
    /// </summary>
    public class ApiResponse : ApiResponse<object>
    {
        /// <summary>Creates a successful response with an object payload.</summary>
        public static ApiResponse Ok(object? data, string message = "Success") =>
            new() { IsSuccessful = true, Message = message, Data = data };

        /// <summary>Creates a successful response with no payload.</summary>
        public new static ApiResponse Ok(string message = "Success") =>
            new() { IsSuccessful = true, Message = message };

        /// <summary>Creates a failure response.</summary>
        public new static ApiResponse Fail(string message, IEnumerable<string>? errors = null) =>
            new() { IsSuccessful = false, Message = message, Errors = errors };
    }
}
