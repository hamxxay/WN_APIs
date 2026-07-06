using WorkNest.Application.DTOs.User;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>User management operations.</summary>
    public interface IUserService
    {
        Task<IEnumerable<object>> GetAllUsersAsync();
        Task<ApiResponse> GetUserByIdAsync(string id);
        Task<ApiResponse> GetUserHistoryAsync(string id);
        Task<ApiResponse> CreateUserAsync(UserCreateRequest request);
        Task<ApiResponse> UpdateUserAsync(string id, UserUpdateRequest request);
        Task<ApiResponse> DeleteUserAsync(string id);
        Task<ApiResponse> ActivateUserAsync(string id);
        Task<ApiResponse> DeactivateUserAsync(string id);
        Task<ApiResponse> UpdateUserRoleAsync(string id, UserRoleUpdateRequest request);
    }
}
