using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using WorkNest.API.Extensions;

namespace WorkNest.API.Filters
{
    /// <summary>
    /// Last stop before a failed response leaves the API: if its message is technical text (a SQL Server or .NET
    /// error, file path, IP address, or internal service error), the text is logged and
    /// replaced by a friendly message (<see cref="UserErrorText"/>). Business messages pass through unchanged.
    /// Applies to error statuses (4xx/5xx) and to 200 responses that report failure (isSuccessful/success = false).
    /// </summary>
    public sealed class FriendlyErrorResultFilter : IAsyncResultFilter
    {
        private static readonly string[] MessageKeys = {
            "message", "error", "errorMessage", "detail", "title",
            "error_description", "description", "exception", "reason", "statusText", "err"
        };
        private readonly ILogger<FriendlyErrorResultFilter> _logger;
        private readonly JsonSerializerOptions _json;

        public FriendlyErrorResultFilter(ILogger<FriendlyErrorResultFilter> logger, IOptions<JsonOptions> json)
        {
            _logger = logger;
            _json = json.Value.JsonSerializerOptions;
        }

        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is ObjectResult { Value: not null } result && IsFailure(result))
            {
                try { Sanitize(context, result); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not check the error message of {Path}", context.HttpContext.Request.Path); }
            }
            await next();
        }

        private static bool IsFailure(ObjectResult result)
        {
            if ((result.StatusCode ?? 200) >= 400) return true;
            foreach (var name in new[] { "IsSuccessful", "Success" })
            {
                var prop = result.Value!.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop?.PropertyType == typeof(bool) && prop.GetValue(result.Value) is false) return true;
            }
            return false;
        }

        private void Sanitize(ResultExecutingContext context, ObjectResult result)
        {
            if (result.Value is string text)
            {
                if (UserErrorText.IsTechnical(text))
                {
                    Log(context, text);
                    result.Value = UserErrorText.ForUser(text);
                }
                return;
            }

            if (JsonSerializer.SerializeToNode(result.Value, result.Value!.GetType(), _json) is not JsonNode node) return;
            bool changed = SanitizeNode(context, node);
            if (changed) result.Value = node;
        }

        private bool SanitizeNode(ResultExecutingContext context, JsonNode node)
        {
            bool changed = false;
            if (node is JsonObject obj)
            {
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    var child = obj[key];
                    if (child is JsonValue v && v.TryGetValue<string>(out var msg) && MessageKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (UserErrorText.IsTechnical(msg))
                        {
                            Log(context, msg);
                            obj[key] = UserErrorText.ForUser(msg);
                            changed = true;
                        }
                    }
                    else if (child != null)
                    {
                        changed |= SanitizeNode(context, child);
                    }
                }
            }
            else if (node is JsonArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    var item = arr[i];
                    if (item is JsonValue v && v.TryGetValue<string>(out var msg))
                    {
                        if (UserErrorText.IsTechnical(msg))
                        {
                            Log(context, msg);
                            arr[i] = UserErrorText.ForUser(msg);
                            changed = true;
                        }
                    }
                    else if (item != null)
                    {
                        changed |= SanitizeNode(context, item);
                    }
                }
            }
            return changed;
        }

        private void Log(ResultExecutingContext context, string original) =>
            _logger.LogError("Technical error hidden from the user on {Method} {Path}: {Error}",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path, original);
    }
}
