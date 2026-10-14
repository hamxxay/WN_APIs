using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// The roles as stored in dbo.WN_Roles (names shown to people come from its DisplayName, never from code).
    /// Read through dbo.WN_Roles_GetList (script SM_01) and cached for a few minutes.
    /// </summary>
    public interface IRoleService
    {
        /// <summary>
        /// Active roles [{ id, roleName, displayName }] ordered by Id (GET api/roles). Throws
        /// <see cref="RolesDatabaseUpdateMissingException"/> before the script SM_01 has run.
        /// </summary>
        Task<ApiResponse> GetRolesAsync();

        /// <summary>WN_Roles.DisplayName of a role (e.g. "CEO", "Sales Manager"); null when unknown or not readable yet.</summary>
        Task<string?> GetDisplayNameAsync(int roleId);
    }

    /// <summary>dbo.WN_Roles_GetList (script SM_01) is not on this database yet.</summary>
    public sealed class RolesDatabaseUpdateMissingException : Exception
    {
        public const string UserMessage = "The role list needs the database update SM_01.";

        public RolesDatabaseUpdateMissingException() : base(UserMessage)
        {
        }
    }
}
