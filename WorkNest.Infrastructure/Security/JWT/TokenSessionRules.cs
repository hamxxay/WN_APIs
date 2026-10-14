using System.Security.Cryptography;
using System.Text;
using WorkNest.Common.Constants;

namespace WorkNest.Infrastructure.Security.JWT
{
    /// <summary>The parts of a WN_Users row a signed-in token is checked against on each request.</summary>
    public sealed record AccountState(bool Exists, bool IsActive, string Role, int? LocationId)
    {
        public static readonly AccountState Missing = new(false, false, Roles.General, null);

        /// <summary>
        /// From a dbo.WN_Users_GetByEmail row (null = no active user with that email: the procedure only returns
        /// IsActive = 1, and deleting a user deactivates it). IsActive missing or NULL counts as active.
        /// </summary>
        public static AccountState FromUserRow(IDictionary<string, object?>? row)
        {
            if (row is null) return Missing;
            var isActive = !row.TryGetValue("IsActive", out var active) || active is null || ToBool(active);
            int? locationId = row.TryGetValue("LocationId", out var loc) && loc is not null
                && int.TryParse(Convert.ToString(loc, System.Globalization.CultureInfo.InvariantCulture), out var l) ? l : null;
            return new AccountState(true, isActive, Roles.FromRow(row), locationId);
        }

        private static bool ToBool(object value) => value switch
        {
            bool b => b,
            string s => s.Trim() is var t && (t == "1" || t.Equals("true", StringComparison.OrdinalIgnoreCase)),
            _ => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) != 0
        };
    }

    /// <summary>
    /// Whether a token that passed signature + expiry checks still matches the account behind it. A token carries
    /// the role and location of the moment it was issued (up to JwtSettings:ExpiryMinutes ago); a user who was
    /// deactivated, deleted, given another role or moved to another location must sign in again.
    /// </summary>
    public static class TokenSessionRules
    {
        /// <summary>Null when the token may be used; otherwise the reason it is refused (for the server log).</summary>
        public static string? RejectReason(AccountState account, string? tokenRole, string? tokenLocationId)
        {
            if (!account.Exists) return "No active user with this email.";
            if (!account.IsActive) return "The user is deactivated.";
            if (!string.Equals(CanonicalRole(tokenRole), account.Role, StringComparison.OrdinalIgnoreCase))
                return "The user's role has changed since this token was issued.";
            // Admins, Sales Executives and Sales Managers work at their location; a move must reach their next request.
            if (Roles.IsLocationBoundRole(account.Role))
            {
                int? tokenLocation = int.TryParse(tokenLocationId?.Trim(), out var l) ? l : null;
                if (tokenLocation != account.LocationId)
                    return "The user's location has changed since this token was issued.";
            }
            return null;
        }

        /// <summary>A role claim as Roles.FromRow writes it ("super_admin", "admin", "sales_executive", "ceo", "sales_manager", "general").</summary>
        public static string CanonicalRole(string? role) => Roles.MapRole(Roles.ParseRoleId(role));

        /// <summary>
        /// The key a signed-out token is remembered by: its jti (every token JwtService issues has one), or a
        /// SHA-256 of the whole token when it has none. Null when neither is available.
        /// </summary>
        public static string? SessionKey(string? jti, string? encodedToken)
        {
            if (!string.IsNullOrWhiteSpace(jti)) return "jti:" + jti.Trim();
            if (string.IsNullOrWhiteSpace(encodedToken)) return null;
            return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(encodedToken)));
        }
    }
}
