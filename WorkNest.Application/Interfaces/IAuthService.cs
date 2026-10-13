using WorkNest.Application.DTOs.Auth;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Authentication and user identity operations.</summary>
    public interface IAuthService
    {
        Task<ApiResponse> SyncUserAsync(UserSyncRequest request);
        Task<ApiResponse> RegisterAsync(UserRegisterRequest request);
        /// <param name="firebaseVerified">True only when the caller verified the request's Firebase ID token.</param>
        Task<ApiResponse> LoginAsync(UserLoginRequest request, bool firebaseVerified = false);
        Task<ApiResponse> GoogleLoginAsync(GoogleLoginRequest request);
        Task<ApiResponse> GetMeAsync(string email);
        Task<ApiResponse> UpdateMeAsync(string email, UpdateMyProfileRequest request);
        ApiResponse Logout();
    }
}
