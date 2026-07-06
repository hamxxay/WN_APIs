namespace WorkNest.API.Middleware
{
    /// <summary>
    /// Logs every incoming request and outgoing response using Serilog.
    /// </summary>
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next   = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            _logger.LogInformation("Incoming {Method} {Path}{Query}",
                context.Request.Method,
                context.Request.Path,
                context.Request.QueryString);

            await _next(context);

            _logger.LogInformation("Outgoing {StatusCode} for {Method} {Path}",
                context.Response.StatusCode,
                context.Request.Method,
                context.Request.Path);
        }
    }
}
