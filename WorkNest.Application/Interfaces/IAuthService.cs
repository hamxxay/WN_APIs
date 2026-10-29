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
        /// <summary>
        /// Signs in a user handed over from the other WorkNest site (api/auth/handoff/redeem): the same response as
        /// LoginAsync. Called only after the one-time code was checked.
        /// </summary>
        Task<ApiResponse> HandoffLoginAsync(string email);
        /// <summary>
        /// Whether a verified Firebase token may sign in as its email: null = yes, else the refusal to send as a 403
        /// (EMAIL_NOT_VERIFIED / ACCOUNT_DISABLED). Called for every endpoint that takes a Firebase token.
        /// </summary>
        Task<SignInRefusal?> CheckFirebaseSignInAsync(string email, bool emailVerified);
        Task<ApiResponse> GetMeAsync(string email);
        Task<ApiResponse> UpdateMeAsync(string email, UpdateMyProfileRequest request);
        ApiResponse Logout();
    }
}
