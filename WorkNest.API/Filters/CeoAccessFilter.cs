using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WorkNest.API.Extensions;
using WorkNest.Common.Responses;

namespace WorkNest.API.Filters
{
    /// <summary>
    /// Marks the endpoints a CEO account may call besides the anonymous ones (sign-in, sign-out): the CEO dashboard
    /// api/ceo/*, api/dashboard/forecast, the signed-in user's own profile (GET / PUT api/auth/me) and the app's push
    /// token (POST api/mobile/device-token). Everything else answers 403 to a CEO, see <see cref="CeoAccessFilter"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class CeoAllowedAttribute : Attribute
    {
    }

    /// <summary>
    /// Global filter: a CEO (role "ceo") only views the CEO dashboard. Endpoints restricted by role already refuse a
    /// CEO (the role is in none of them); this also refuses the endpoints open to any signed-in user ([Authorize]
    /// without roles, e.g. customer screens, uploads, device tokens), unless marked [CeoAllowed]. Anonymous
    /// endpoints (sign-in, sign-out, public website data) stay open, as they are without a token.
    /// </summary>
    public sealed class CeoAccessFilter : IAsyncAuthorizationFilter
    {
        public const string Message = "The CEO account can only open the CEO dashboard.";

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (!context.HttpContext.User.IsCeo()) return Task.CompletedTask;

            var metadata = context.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<CeoAllowedAttribute>().Any() || metadata.OfType<IAllowAnonymous>().Any())
                return Task.CompletedTask;

            context.Result = new ObjectResult(ApiResponse.Fail(Message)) { StatusCode = StatusCodes.Status403Forbidden };
            return Task.CompletedTask;
        }
    }
}
