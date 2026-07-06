using WorkNest.Application.DTOs.Auth;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Authentication and user identity operations.</summary>
    public interface IAuthService
    {
        Task<ApiResponse> SyncUserAsync(UserSyncRequest request);
        Task<ApiResponse> RegisterAsync(UserRegisterRequest request);
        Task<ApiResponse> LoginAsync(UserLoginRequest request);
        Task<ApiResponse> GoogleLoginAsync(GoogleLoginRequest request);
        Task<ApiResponse> GetMeAsync(string email);
        ApiResponse Logout();
    }
}
