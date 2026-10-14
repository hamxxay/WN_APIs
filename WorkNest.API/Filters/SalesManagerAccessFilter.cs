using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WorkNest.API.Extensions;
using WorkNest.Common.Responses;

namespace WorkNest.API.Filters
{
    /// <summary>
    /// Marks the endpoints a Sales Manager account may call besides the anonymous ones (sign-in, Google sign-in,
    /// sign-out): the Sales Manager dashboard api/sales-manager/*, the signed-in user's own profile (GET / PUT
    /// api/auth/me) and the app's push token (POST api/mobile/device-token). Everything else answers 403 to a Sales
    /// Manager, see <see cref="SalesManagerAccessFilter"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class SalesManagerAllowedAttribute : Attribute
    {
    }

    /// <summary>
    /// Global filter, the Sales Manager twin of <see cref="CeoAccessFilter"/>: a Sales Manager (role "sales_manager")
    /// only views the Sales Manager dashboard. Endpoints restricted by role already refuse a Sales Manager (the role is
    /// in none of them); this also refuses the endpoints open to any signed-in user ([Authorize] without roles, e.g.
    /// customer screens, uploads), unless marked [SalesManagerAllowed]. Anonymous endpoints (sign-in, sign-out, public
    /// website data) stay open, as they are without a token. Other roles (the CEO included) are not affected.
    /// </summary>
    public sealed class SalesManagerAccessFilter : IAsyncAuthorizationFilter
    {
        public const string Message = "The Sales Manager account can only open the Sales Manager dashboard.";

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (!context.HttpContext.User.IsSalesManager()) return Task.CompletedTask;

            var metadata = context.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<SalesManagerAllowedAttribute>().Any() || metadata.OfType<IAllowAnonymous>().Any())
                return Task.CompletedTask;

            context.Result = new ObjectResult(ApiResponse.Fail(Message)) { StatusCode = StatusCodes.Status403Forbidden };
            return Task.CompletedTask;
        }
    }
}
