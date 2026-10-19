using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using WorkNest.API.Extensions;
using WorkNest.Application.DTOs.CustomInvoice;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;

namespace WorkNest.API.Security
{
    /// <summary>
    /// Policy "CustomInvoiceAccess" on the custom invoice endpoints: a super admin, or an admin with the CustomInvoice
    /// permission (<see cref="ICustomInvoiceAccessService"/>: cached 30 seconds per user, refused before CI_01).
    /// </summary>
    public static class CustomInvoiceAccessPolicy
    {
        public const string Name = "CustomInvoiceAccess";

        /// <summary>The caller's roles from the JWT (role claim, any spelling the role list knows) and e-mail.</summary>
        public static CustomInvoiceCaller CallerOf(ClaimsPrincipal? user)
        {
            if (user?.Identity?.IsAuthenticated != true) return new CustomInvoiceCaller(false, false, null);
            var roleClaim = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
            var roleId = Roles.ParseRoleId(roleClaim, -1);
            var isSuperAdmin = roleId == Roles.SuperAdminId || user.IsInRole("super_admin") || user.IsInRole("SuperAdmin");
            var isAdmin = !isSuperAdmin && (roleId == Roles.AdminId || user.IsInRole("admin") || user.IsInRole("Admin"));
            return new CustomInvoiceCaller(isSuperAdmin, isAdmin, user.GetEmail());
        }
    }

    public sealed class CustomInvoiceAccessRequirement : IAuthorizationRequirement
    {
    }

    /// <summary>Succeeds for a super admin, or an admin with the CustomInvoice permission.</summary>
    public sealed class CustomInvoiceAccessHandler : AuthorizationHandler<CustomInvoiceAccessRequirement>
    {
        private readonly ICustomInvoiceAccessService _access;

        public CustomInvoiceAccessHandler(ICustomInvoiceAccessService access) => _access = access;

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CustomInvoiceAccessRequirement requirement)
        {
            var caller = CustomInvoiceAccessPolicy.CallerOf(context.User);
            if (!caller.IsSuperAdmin && !caller.IsAdmin) return;
            if ((await _access.CheckAsync(caller)).CanUse)
                context.Succeed(requirement);
        }
    }
}
