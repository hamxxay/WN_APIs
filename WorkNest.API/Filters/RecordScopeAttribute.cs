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
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RecordScopeAttribute : Attribute, IAsyncActionFilter
    {
        private readonly RecordKind _kind;
        private readonly string[] _routeKeys;

        public RecordScopeAttribute(RecordKind kind, params string[] routeKeys)
        {
            _kind = kind;
            _routeKeys = routeKeys.Length > 0 ? routeKeys : new[] { "id", "publicId" };
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;
            if (user.IsLocationBoundRole())
            {
                var key = _routeKeys.Select(k => context.RouteData.Values.TryGetValue(k, out var v) ? v?.ToString() : null)
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                if (key != null)
                {
                    var repo = context.HttpContext.RequestServices.GetRequiredService<IRecordScopeRepository>();
                    var locations = await repo.GetLocationIdsAsync(_kind, key);
                    var mine = user.GetLocationIds();
                    if (locations != null && locations.Count > 0 && !locations.Any(mine.Contains))
                    {
                        context.Result = new NotFoundObjectResult(ApiResponse.Fail($"{_kind} not found."));
                        return;
                    }
                }
            }
            await next();
        }
    }
}
