using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using WorkNest.API.Extensions;
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
                // A stored procedure refusing on purpose (RAISERROR / THROW 50000+, e.g. "Space is already booked for
                // the selected date range and shift.") wrote that text for users: show it instead of a generic 500.
                var refusal = ProcedureMessage(ex);
                if (refusal != null)
                {
                    _logger.LogWarning("{Method} {Path} refused by the database: {Message}",
                        context.Request.Method, context.Request.Path, refusal);
                    await WriteErrorResponseAsync(context, refusal, HttpStatusCode.BadRequest);
                    return;
                }

                _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                    context.Request.Method, context.Request.Path);
                await WriteErrorResponseAsync(context, "An unexpected error occurred. Please try again later.");
            }
        }

        /// <summary>The first user-defined error (number 50000 and up) raised by a stored procedure, if any.</summary>
        private static string? ProcedureMessage(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is not SqlException sql) continue;
                foreach (SqlError err in sql.Errors)
                    if (err.Number >= 50000 && !string.IsNullOrWhiteSpace(err.Message) && !UserErrorText.IsTechnical(err.Message))
                        return err.Message;
            }
            return null;
        }

        private static async Task WriteErrorResponseAsync(HttpContext context, string message,
            HttpStatusCode status = HttpStatusCode.InternalServerError)
        {
            context.Response.StatusCode  = (int)status;
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
