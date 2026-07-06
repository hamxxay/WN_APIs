namespace WorkNest.Application.Interfaces
{
    /// <summary>JWT token generation and validation.</summary>
    public interface IJwtService
    {
        /// <summary>Generates a signed JWT for the given user.</summary>
        string GenerateToken(string userId, string email, string role);

        /// <summary>Validates a token and returns the email claim, or null if invalid.</summary>
        string? ValidateToken(string token);
    }
}
