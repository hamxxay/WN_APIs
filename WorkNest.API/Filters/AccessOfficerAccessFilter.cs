using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WorkNest.API.Extensions;
using WorkNest.Common.Responses;

namespace WorkNest.API.Filters
{
    /// <summary>
    /// Marks the endpoints an Access Officer account may call besides the anonymous ones (sign-in, Google sign-in,
    /// sign-out, public website data such as api/location/all): the Access page api/access-officer/*, the signed-in
    /// user's own profile (GET / PUT api/auth/me) and the app's push token (POST api/mobile/device-token). Everything
    /// else answers 403 to an Access Officer, see <see cref="AccessOfficerAccessFilter"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class AccessOfficerAllowedAttribute : Attribute
    {
    }

    /// <summary>
    /// Global filter, the Access Officer twin of <see cref="SalesManagerAccessFilter"/>: an Access Officer (role
    /// "access_officer") only gives and removes door access on the Access page. Endpoints restricted by role already
    /// refuse an Access Officer (the role is in none of them); this also refuses the endpoints open to any signed-in user
    /// ([Authorize] without roles, e.g. customer screens, uploads, the Attendants page's plain [Authorize] actions), unless
    /// marked [AccessOfficerAllowed]. Anonymous endpoints stay open, as they are without a token. Other roles are not
    /// affected.
    /// </summary>
    public sealed class AccessOfficerAccessFilter : IAsyncAuthorizationFilter
    {
        public const string Message = "The Access Officer account can only open the Access page.";

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (!context.HttpContext.User.IsAccessOfficer()) return Task.CompletedTask;

            var metadata = context.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<AccessOfficerAllowedAttribute>().Any() || metadata.OfType<IAllowAnonymous>().Any())
                return Task.CompletedTask;

            context.Result = new ObjectResult(ApiResponse.Fail(Message)) { StatusCode = StatusCodes.Status403Forbidden };
            return Task.CompletedTask;
        }
    }
}
