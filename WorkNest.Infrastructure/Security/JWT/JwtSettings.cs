namespace WorkNest.Infrastructure.Security.JWT
{
    /// <summary>
    /// Strongly-typed JWT settings bound from appsettings.json JwtSettings section.
    /// </summary>
    public class JwtSettings
    {
        public string SecretKey { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public int ExpiryMinutes { get; set; } = 1440;
    }
}
