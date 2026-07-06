namespace WorkNest.Application.DTOs.Auth
{
    public class UserSyncRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
    }

    public class UserRegisterRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? Password { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
    }

    public class UserLoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? Password { get; set; }
    }

    public class GoogleLoginRequest
    {
        public string IdToken { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
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
