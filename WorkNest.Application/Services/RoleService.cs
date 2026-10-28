using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Roles from dbo.WN_Roles (via dbo.WN_Roles_GetList). The list is cached for 5 minutes (a role row added by a
    /// database script shows up within that time); while the procedure is missing the answer is re-checked every minute.
    /// </summary>
    public class RoleService : IRoleService
    {
        private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RetryMissingAfter = TimeSpan.FromMinutes(1);
        private static readonly SemaphoreSlim Lock = new(1, 1);
        private static List<RoleItem>? _roles;
        private static bool _missing;
        private static DateTime _loadedAt;

        private readonly IDbRepository _db;

        public RoleService(IDbRepository db)
        {
            _db = db;
        }

        /// <summary>One WN_Roles row: serialized { id, roleName, displayName }.</summary>
        public sealed record RoleItem(int Id, string RoleName, string DisplayName);

        public async Task<ApiResponse> GetRolesAsync()
        {
            var roles = await LoadAsync();
            if (roles == null) throw new RolesDatabaseUpdateMissingException();
            return ApiResponse.Ok(roles);
        }

        public async Task<string?> GetDisplayNameAsync(int roleId)
        {
            try
            {
                var roles = await LoadAsync();
                var name = roles?.FirstOrDefault(r => r.Id == roleId)?.DisplayName;
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
            catch
            {
                return null; // a display name must never break sign-in or the profile
            }
        }

        private async Task<List<RoleItem>?> LoadAsync()
        {
            if (Fresh()) return _roles;
            await Lock.WaitAsync();
            try
            {
                if (Fresh()) return _roles;
                var rows = await _db.GetRolesAsync();
                _missing = rows == null;
                var list = rows?.Select(r => new RoleItem(
                        Convert.ToInt32(r["Id"]),
                        Convert.ToString(r.TryGetValue("RoleName", out var n) ? n : null) ?? "",
                        Convert.ToString(r.TryGetValue("DisplayName", out var d) ? d : null) ?? ""))
                    .ToList();
                if (list != null && !list.Any(r => r.Id == Roles.NetworkEngineerId || string.Equals(r.RoleName, Roles.NetworkEngineer, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(new RoleItem(Roles.NetworkEngineerId, Roles.NetworkEngineer, "Network Engineer"));
                }
                _roles = list?.OrderBy(r => r.Id).ToList();
                _loadedAt = DateTime.UtcNow;
                return _roles;
            }
            finally
            {
                Lock.Release();
            }
        }

        private static bool Fresh() =>
            _loadedAt != default && DateTime.UtcNow - _loadedAt < (_missing ? RetryMissingAfter : CacheFor);
    }
}
