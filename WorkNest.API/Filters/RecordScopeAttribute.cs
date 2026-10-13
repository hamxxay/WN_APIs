using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WorkNest.API.Extensions;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Filters
{
    /// <summary>
    /// Keeps location-bound admins / sales executives to records of their own locations on endpoints that take a
    /// record id in the route. The first route value found among <paramref name="routeKeys"/> identifies the record;
    /// a record of another location answers 404 (as if it did not exist). Records with no location yet (e.g. a new
    /// customer without a booking) stay open to everyone. Super admins, receptionists and customers are not affected
    /// (customers are checked for ownership by each action).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class RecordScopeAttribute : Attribute, IAsyncActionFilter
    {
        private readonly RecordKind _kind;
        private readonly string[] _routeKeys;

        public RecordScopeAttribute(RecordKind kind, params string[] routeKeys)
        {
            _kind = kind;
            _routeKeys = routeKeys.Length > 0 ? routeKeys : new[] { "id", "publicId" };
        }

        /// <summary>Read the keys from the query string (e.g. <c>?customerId=12</c>) instead of the route.</summary>
        public bool FromQuery { get; set; }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;
            var key = _routeKeys
                .Select(k => FromQuery
                    ? (http.Request.Query.TryGetValue(k, out var q) ? q.ToString() : null)
                    : (context.RouteData.Values.TryGetValue(k, out var v) ? v?.ToString() : null))
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (key != null && !await AllowsAsync(http, _kind, key))
            {
                context.Result = new NotFoundObjectResult(ApiResponse.Fail($"{_kind} not found."));
                return;
            }
            await next();
        }

        /// <summary>
        /// False when the caller is a location-bound admin / sales executive and the record belongs only to other
        /// locations. For ids that arrive in a request body, where the attribute can't see them.
        /// </summary>
        public static async Task<bool> AllowsAsync(HttpContext http, RecordKind kind, string key)
        {
            var user = http.User;
            if (!user.IsLocationBoundRole()) return true;
            var repo = http.RequestServices.GetRequiredService<IRecordScopeRepository>();
            var locations = await repo.GetLocationIdsAsync(kind, key);
            var mine = user.GetLocationIds();
            return locations == null || locations.Count == 0 || locations.Any(mine.Contains);
        }
    }
}
