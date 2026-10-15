namespace WorkNest.Application.DTOs.Auth
{
    /// <summary>Signed-in user's own editable profile fields (PUT api/auth/me).</summary>
    public class UpdateMyProfileRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Phone { get; set; }
    }

    public class UserSyncRequest
    {
        /// <summary>Firebase ID token (user.getIdToken()) — proves the person signed in with Firebase.</summary>
        public string? FirebaseIdToken { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public string? PasswordHash { get; set; }
        public int? RoleId { get; set; }
        public int? CompanyId { get; set; }
    }

    public class UserRegisterRequest
    {
        /// <summary>Firebase ID token (user.getIdToken()) — proves the person signed in with Firebase.</summary>
        public string? FirebaseIdToken { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? Password { get; set; }
        public string? Name { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public int? RoleId { get; set; }
        public int? CompanyId { get; set; }
    }

    public class UserLoginRequest
    {
        /// <summary>Firebase ID token (user.getIdToken()) — proves the person signed in with Firebase.</summary>
        public string? FirebaseIdToken { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? Password { get; set; }
        public string? Name { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public int? CompanyId { get; set; }
    }

    public class GoogleLoginRequest
    {
        /// <summary>Firebase ID token (user.getIdToken()) — proves the person signed in with Firebase.</summary>
        public string? FirebaseIdToken { get; set; }
        public string IdToken { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public int? CompanyId { get; set; }

        /// <summary>
        /// True (or missing, for the website / CMS / older apps) = the person signed in just now: the sign-in is recorded
        /// in the login history and the user / customer rows are refreshed. False = a silent token refresh (app start,
        /// hourly Firebase refresh): only the token is returned. A first sign-in (no WN_Users row yet) is always set up.
        /// </summary>
        public bool? Interactive { get; set; }

        /// <summary>Interactive unless the caller said false.</summary>
        public bool IsInteractive => Interactive != false;
    }

    /// <summary>
    /// Why a sign-in was refused with HTTP 403. Sent as { isSuccessful: false, message, data: { code } } so the website
    /// and app can tell the cases apart (e.g. offer to resend the verification email for EMAIL_NOT_VERIFIED).
    /// </summary>
    public sealed record SignInRefusal(string Code, string Message)
    {
        public const string EmailNotVerifiedCode = "EMAIL_NOT_VERIFIED";
        public const string AccountDisabledCode = "ACCOUNT_DISABLED";

        /// <summary>A Firebase account whose email nobody has proven to own (and no earlier sign-in to vouch for it).</summary>
        public static SignInRefusal EmailNotVerified(string email) => new(EmailNotVerifiedCode,
            $"Please verify your email address first. Open the link we sent to {email}, then sign in again.");

        /// <summary>The WN_Users row exists but IsActive = 0.</summary>
        public static SignInRefusal AccountDisabled() => new(AccountDisabledCode,
            "Your account is turned off. Please contact WorkNest.");

        /// <summary>The 403 body: { isSuccessful: false, message, data: { code } }.</summary>
        public object ToBody() => new { isSuccessful = false, message = Message, data = new { code = Code } };

        /// <summary>The refusal code an AuthService result carries in data.code (null when it carries none).</summary>
        public static string? CodeOf(WorkNest.Common.Responses.ApiResponse? response)
        {
            if (response is null || response.IsSuccessful || response.Data is null) return null;
            if (response.Data is SignInRefusalData d) return d.Code;
            return null;
        }
    }

    /// <summary>A WN_Users row as the sign-in checks see it (any IsActive).</summary>
    public sealed record UserSignInStatus(int Id, bool IsActive, string? PasswordHash);

    /// <summary>data of a refused AuthService result (serialises as { "code": "..." }).</summary>
    public sealed class SignInRefusalData
    {
        public string Code { get; init; } = string.Empty;
    }

    public class AuthResponse
    {
        public string? Id { get; set; }
        public string? Email { get; set; }
        public List<string>? Roles { get; set; }
        public string? Token { get; set; }
    }

    public class MeResponse
    {
        public string? Id { get; set; }
        public string? Email { get; set; }
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? Role { get; set; }
    }
}
