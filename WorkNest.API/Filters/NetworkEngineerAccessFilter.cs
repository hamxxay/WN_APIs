using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WorkNest.API.Extensions;
using WorkNest.Common.Responses;

namespace WorkNest.API.Filters
{
    /// <summary>
    /// Marks the endpoints a Network Engineer account may call besides the anonymous ones (sign-in, Google sign-in,
    /// sign-out): the Network pages (api/unifi/*, api/gwn/*), the signed-in user's own profile (GET / PUT api/auth/me,
    /// api/account/*), the app's push token (POST api/mobile/device-token) and the location and space lists the Network
    /// pages read (GET api/location, GET api/space; own locations only). Everything else answers 403 to a Network
    /// Engineer, see <see cref="NetworkEngineerAccessFilter"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class NetworkEngineerAllowedAttribute : Attribute
    {
    }

    /// <summary>
    /// Global filter, the Network Engineer twin of <see cref="AccessOfficerAccessFilter"/>: a Network Engineer (role
    /// "network_engineer") only accesses network configuration and monitoring pages. Endpoints restricted by role already
    /// refuse a Network Engineer (the role is in none of them); this also refuses the endpoints open to any signed-in user
    /// ([Authorize] without roles, e.g. customer screens, uploads, bookings, attendants), unless marked [NetworkEngineerAllowed].
    /// Anonymous endpoints stay open, as they are without a token. Other roles are not affected.
    /// </summary>
    public sealed class NetworkEngineerAccessFilter : IAsyncAuthorizationFilter
    {
        public const string Message = "The Network Engineer account can only open Network pages.";

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (!context.HttpContext.User.IsNetworkEngineer()) return Task.CompletedTask;

            var metadata = context.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<NetworkEngineerAllowedAttribute>().Any() || metadata.OfType<IAllowAnonymous>().Any())
                return Task.CompletedTask;

            context.Result = new ObjectResult(ApiResponse.Fail(Message)) { StatusCode = StatusCodes.Status403Forbidden };
            return Task.CompletedTask;
        }
    }
}
