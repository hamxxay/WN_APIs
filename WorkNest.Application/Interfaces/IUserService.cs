using WorkNest.Application.DTOs.User;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IUserService
    {
        Task<(IEnumerable<object> Items, int Total)> GetUsersAsync(int page, int limit, string? search, int? locationId = null);
        Task<ApiResponse> GetUserByIdAsync(int id);
        Task<ApiResponse> GetUserHistoryAsync(int id);
        Task<ApiResponse> CreateUserAsync(UserCreateRequest request, int? actorId);
        Task<ApiResponse> UpdateUserAsync(int id, UserUpdateRequest request);
        Task<ApiResponse> DeleteUserAsync(int id);
        Task<ApiResponse> ActivateUserAsync(int id);
        Task<ApiResponse> DeactivateUserAsync(int id);
        Task<ApiResponse> UpdateUserRoleAsync(int id, UserRoleUpdateRequest request);
    }
}
