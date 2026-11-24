using System.Security.Claims;
using WorkNest.Application.Services.Transfers;

namespace WorkNest.API.Extensions
{
    public static class TransferCallerExtensions
    {
        /// <summary>The transfer endpoints' caller: highest role wins; location-bound roles carry their locations (empty = no access).</summary>
        public static TransferCaller ToTransferCaller(this ClaimsPrincipal user, int? userId)
        {
            bool super = user.IsSuperAdmin() || user.IsInRole("super_admin") || user.IsInRole("SuperAdmin");
            bool admin = !super && user.IsAdminOrSuperAdmin();
            bool manager = !super && !admin && user.IsSalesManager();
            bool exec = !super && !admin && !manager && (user.IsInRole("sales_executive") || user.IsInRole("SalesExecutive"));
            return new TransferCaller(userId, super, admin, manager, exec, user.IsLocationBoundRole() ? user.GetLocationIds() : null);
        }
    }
}
