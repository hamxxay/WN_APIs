using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WorkNest.Common.Constants;

namespace WorkNest.API.Filters
{
    /// <summary>
    /// Marks the endpoints a signed-in user may call while they still must set a new password (JWT claim pwd_change = 1):
    /// GET api/auth/me, POST api/auth/change-password and POST api/auth/logout. Anonymous endpoints are always open.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class AllowWhilePasswordChangeRequiredAttribute : Attribute
    {
    }

    /// <summary>
    /// Global filter (AUTH_TEMP_01): while the token carries pwd_change = 1 (a customer who signed in with the temporary
    /// password staff handed out), every endpoint not marked [AllowWhilePasswordChangeRequired] or anonymous answers
    /// 403 { isSuccessful: false, message: "Please set a new password first.", data: { code: "PASSWORD_CHANGE_REQUIRED" } }.
    /// </summary>
    public sealed class PasswordChangeRequiredFilter : IAsyncAuthorizationFilter
    {
        public static bool IsRequired(System.Security.Claims.ClaimsPrincipal? user) =>
            user?.Identity?.IsAuthenticated == true && user.FindFirst(PasswordChange.ClaimType)?.Value == "1";

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (!IsRequired(context.HttpContext.User)) return Task.CompletedTask;

            var metadata = context.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<AllowWhilePasswordChangeRequiredAttribute>().Any() || metadata.OfType<IAllowAnonymous>().Any())
                return Task.CompletedTask;

            context.Result = new ObjectResult(new
            {
                isSuccessful = false,
                message = PasswordChange.Message,
                data = new { code = PasswordChange.Code }
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return Task.CompletedTask;
        }
    }
}
