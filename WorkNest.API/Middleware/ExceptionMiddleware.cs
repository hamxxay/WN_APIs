using System.Net;
using System.Text.Json;
using WorkNest.Common.Responses;

namespace WorkNest.API.Middleware
{
    /// <summary>
    /// Global exception middleware.
    /// Catches all unhandled exceptions, logs them via Serilog,
    /// and returns a standardized ApiResponse JSON — never exposes stack traces.
    /// </summary>
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;

        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
        {
            _next   = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                    context.Request.Method, context.Request.Path);
                await WriteErrorResponseAsync(context, ex.Message);
            }
        }

        private static async Task WriteErrorResponseAsync(HttpContext context, string message)
        {
            context.Response.StatusCode  = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var response = ApiResponse.Fail(message);
            var json     = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
        }
    }
}
