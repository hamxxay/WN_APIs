using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WorkNest.API.Extensions;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Filters
{
    /// <summary>Marks a UniFi endpoint that any staff member may call, whatever their location (e.g. the scope check itself).</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AnyUnifiLocationAttribute : Attribute { }

    /// <summary>
    /// The UniFi network belongs to the locations saved on Network Overview (WN_UNIFI_Settings "LocationIds").
    /// Admins and sales executives with none of those locations get 403; super admins always pass. While no
    /// location is saved, everyone passes (as before).
    /// </summary>
    public sealed class UnifiLocationScopeFilter : IAsyncActionFilter
    {
        private readonly IUnifiService _unifi;
        public UnifiLocationScopeFilter(IUnifiService unifi) => _unifi = unifi;

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;
            if (user.IsLocationBoundRole()
                && context.ActionDescriptor.EndpointMetadata.OfType<AnyUnifiLocationAttribute>().Any() == false
                && !await UnifiScope.AllowsAsync(_unifi, user))
            {
                context.Result = new ObjectResult(new { error = "The network belongs to another location." }) { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }
            await next();
        }
    }

    public static class UnifiScope
    {
        /// <summary>True when the user may see the UniFi network (super admin, no location saved, or a shared location).</summary>
        public static async Task<bool> AllowsAsync(IUnifiService unifi, System.Security.Claims.ClaimsPrincipal user)
        {
            if (!user.IsLocationBoundRole()) return true;
            var networkLocations = await unifi.GetLocationIdsAsync();
            if (networkLocations.Count == 0) return true;
            return user.GetLocationIds().Any(networkLocations.Contains);
        }
    }
}
