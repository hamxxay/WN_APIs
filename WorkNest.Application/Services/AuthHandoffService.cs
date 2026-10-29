using Microsoft.Extensions.Logging;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Cross-site sign-in (api/auth/handoff, api/auth/handoff/redeem). The code is single use and short-lived
    /// (AuthHandoffRules); the database keeps only its hash (dbo.WN_AuthHandoff, script AUTH_HANDOFF_01_login_handoff), so
    /// it works with any number of API servers. A redeemed code gets a brand-new session from AuthService.HandoffLoginAsync
    /// (a new token; the signed-in site's own token is never passed on).
    /// </summary>
    public class AuthHandoffService : IAuthHandoffService
    {
        private const string InvalidMessage = "This sign-in link has expired or was already used. Please sign in.";

        private readonly IAuthHandoffRepository _repo;
        private readonly IDbRepository _db;
        private readonly IAuthService _auth;
        private readonly ILogger<AuthHandoffService> _logger;

        public AuthHandoffService(IAuthHandoffRepository repo, IDbRepository db, IAuthService auth, ILogger<AuthHandoffService> logger)
        {
            _repo = repo;
            _db = db;
            _auth = auth;
            _logger = logger;
        }

        public async Task<AuthHandoffResult> CreateAsync(string email, string? target, CancellationToken ct = default)
        {
            var site = AuthHandoffRules.NormalizeTarget(target);
            if (site is null) return new(400, ApiResponse.Fail("target must be \"cms\" or \"web\"."));

            var row = await _db.GetUserByEmailAsync(email);
            if (row is null || !row.TryGetValue("Id", out var idObj) || idObj is null)
                return new(401, ApiResponse.Fail("Sign in required."));
            var role = Roles.FromRow(row);
            if (!AuthHandoffRules.MayOpen(role, site))
                return new(403, ApiResponse.Fail("The staff portal is for WorkNest staff."));

            var code = AuthHandoffRules.NewCode();
            await _repo.CreateAsync(Convert.ToInt32(idObj), AuthHandoffRules.HashCode(code)!, site, AuthHandoffRules.TtlSeconds, ct);
            return new(200, ApiResponse.Ok(new { code, expiresInSeconds = AuthHandoffRules.TtlSeconds, target = site }));
        }

        public async Task<AuthHandoffResult> RedeemAsync(string? code, string? target, CancellationToken ct = default)
        {
            var site = AuthHandoffRules.NormalizeTarget(target);
            if (site is null) return new(400, ApiResponse.Fail("target must be \"cms\" or \"web\"."));
            var hash = AuthHandoffRules.HashCode(code);
            if (hash is null) return new(401, ApiResponse.Fail(InvalidMessage));

            var redemption = await _repo.RedeemAsync(hash, ct);
            var reason = AuthHandoffRules.RejectReason(redemption, site);
            if (reason != null)
            {
                _logger.LogWarning("Refused a sign-in hand-over to {Target} for user {UserId}: {Reason}", site, redemption?.UserId, reason);
                return new(401, ApiResponse.Fail(InvalidMessage));
            }

            // The role may have changed since the code was made (e.g. no longer staff): the CMS stays staff-only.
            var row = await _db.GetUserByEmailAsync(redemption!.Email!);
            if (row != null && !AuthHandoffRules.MayOpen(Roles.FromRow(row), site))
            {
                _logger.LogWarning("Refused a sign-in hand-over to {Target} for user {UserId}: not staff", site, redemption.UserId);
                return new(403, ApiResponse.Fail("The staff portal is for WorkNest staff."));
            }

            var result = await _auth.HandoffLoginAsync(redemption.Email!);
            if (result.IsSuccessful) return new(200, result);
            return new(WorkNest.Application.DTOs.Auth.SignInRefusal.CodeOf(result) is null ? 401 : 403, result);
        }
    }
}
