using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WorkNest.API.Extensions;
using WorkNest.Common.Responses;

namespace WorkNest.API.Filters
{
    /// <summary>
    /// Marks the endpoints a Receptionist account may call besides the anonymous ones: the front-desk endpoints
    /// api/reception/* and the existing endpoints the desk needs (tour inquiries, complaints, WhatsApp inbox, customer
    /// lookup, payments / receipts, door access by fingerprint / face, attendants, network status, attendance, the
    /// signed-in user's own profile and the app's push token). Everything else answers 403 to a Receptionist, see
    /// <see cref="ReceptionistAccessFilter"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ReceptionistAllowedAttribute : Attribute
    {
    }

    /// <summary>
    /// Global filter, the Receptionist twin of <see cref="SalesManagerAccessFilter"/>: a user whose role is "receptionist"
    /// is refused (403) on every endpoint not marked [ReceptionistAllowed]. Several controllers still list "receptionist"
    /// in their Roles strings (left-overs from before the role existed); this filter makes those safe. Anonymous
    /// endpoints stay open, as they are without a token. Other roles are not affected.
    /// </summary>
    public sealed class ReceptionistAccessFilter : IAsyncAuthorizationFilter
    {
        public const string Message = "The Receptionist account cannot use this part of WorkNest.";

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (!context.HttpContext.User.IsReceptionist()) return Task.CompletedTask;

            var metadata = context.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<ReceptionistAllowedAttribute>().Any() || metadata.OfType<IAllowAnonymous>().Any())
                return Task.CompletedTask;

            context.Result = new ObjectResult(ApiResponse.Fail(Message)) { StatusCode = StatusCodes.Status403Forbidden };
            return Task.CompletedTask;
        }
    }
}
