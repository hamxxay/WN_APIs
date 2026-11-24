using WorkNest.Application.DTOs.Transfer;

namespace WorkNest.Application.Services.Transfers
{
    /// <summary>Who is calling a transfer endpoint (resolved by the controller). LocationIds null = every location.</summary>
    public sealed record TransferCaller(int? UserId, bool IsSuperAdmin, bool IsAdmin, bool IsSalesManager, bool IsSalesExecutive, IReadOnlyCollection<int>? LocationIds);

    /// <summary>The role rules of the transfer redesign (pure).</summary>
    public static class TransferAccess
    {
        /// <summary>Every transfer a sales executive requests waits for the sales manager; admin / super admin transfer directly.</summary>
        public static bool NeedsApproval(TransferCaller c) => c.IsSalesExecutive && !c.IsAdmin && !c.IsSuperAdmin && !c.IsSalesManager;

        /// <summary>Location scope: null list = everything, an empty list = nothing.</summary>
        public static bool InScope(TransferCaller c, int? locationId) =>
            c.LocationIds == null || (locationId != null && c.LocationIds.Contains(locationId.Value));

        public static bool CanDecide(TransferCaller c) => c.IsSuperAdmin || c.IsAdmin || c.IsSalesManager;

        public static bool CanSee(TransferCaller c, TransferRequestRecord r)
        {
            if (CanDecide(c)) return InScope(c, r.LocationId);
            return c.IsSalesExecutive && c.UserId != null && r.RequestedById == c.UserId;
        }
    }
}
