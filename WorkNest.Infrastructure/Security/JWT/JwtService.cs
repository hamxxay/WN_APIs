using System.Linq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Security.JWT
{
    /// <summary>
    /// Generates and validates JWT access tokens.
    /// Stores userId, email, and role as claims.
    /// </summary>
    public class JwtService : IJwtService
    {
        private readonly JwtSettings _settings;

        public JwtService(IOptions<JwtSettings> settings)
        {
            _settings = settings.Value;
        }

        /// <summary>Generates a signed JWT for the given user identity.</summary>
        public string GenerateToken(string userId, string email, string role, int? locationId = null, IEnumerable<int>? locationIds = null)
        {
            if (string.IsNullOrWhiteSpace(_settings.SecretKey))
                throw new InvalidOperationException("JwtSettings:SecretKey is not configured.");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var now = DateTimeOffset.UtcNow;
            var claimsList = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId ?? string.Empty),
                new Claim(ClaimTypes.NameIdentifier, userId ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Email, email ?? string.Empty),
                new Claim(ClaimTypes.Email, email ?? string.Empty),
                new Claim(ClaimTypes.Role, role ?? string.Empty),
                new Claim("role", role ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim(JwtRegisteredClaimNames.Nbf, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            };

            if (locationId.HasValue)
            {
                claimsList.Add(new Claim("location_id", locationId.Value.ToString()));
                claimsList.Add(new Claim("LocationId", locationId.Value.ToString()));
            }
            // Every location an Admin / Sales Executive is assigned to (WN_UserLocations); location_id is the primary.
            var allLocations = (locationIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct().ToList();
            if (allLocations.Count > 0)
            {
                claimsList.Add(new Claim("location_ids", string.Join(",", allLocations)));
            }

            var expiryMinutes = _settings.ExpiryMinutes > 0 ? _settings.ExpiryMinutes : 1440;

            var token = new JwtSecurityToken(
                issuer: string.IsNullOrWhiteSpace(_settings.Issuer) ? null : _settings.Issuer,
                audience: string.IsNullOrWhiteSpace(_settings.Audience) ? null : _settings.Audience,
                claims: claimsList,
                expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>Validates a token and returns the email claim, or null if invalid.</summary>
        public string? ValidateToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(_settings.SecretKey))
                return null;

            try
            {
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
                var handler = new JwtSecurityTokenHandler();
                var result = handler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = !string.IsNullOrWhiteSpace(_settings.Issuer),
                    ValidIssuer = _settings.Issuer,
                    ValidateAudience = !string.IsNullOrWhiteSpace(_settings.Audience),
                    ValidAudience = _settings.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out _);

                return result.FindFirst(JwtRegisteredClaimNames.Email)?.Value 
                    ?? result.FindFirst(ClaimTypes.Email)?.Value;
            }
            catch
            {
                return null;
            }
        }
    }
}
