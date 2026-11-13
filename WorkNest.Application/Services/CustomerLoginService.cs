using System.Net;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Temporary passwords for customers (AUTH_TEMP_01). The readable temporary password is stored on WN_Users.TempPassword
    /// (owner decision, so staff can look it up until the customer sets their own), next to its PBKDF2 hash that login uses.
    /// It is returned in the response and e-mailed; it is never written to a log.
    /// </summary>
    public class CustomerLoginService : ICustomerLoginService
    {
        public const string LoginUrl = "https://worknestpk.com/login";
        public const string NoEmailMessage = "This customer has no email.";

        private readonly ICustomerLoginRepository _repo;
        private readonly IRecordScopeRepository _scope;
        private readonly IEmailService _email;
        private readonly ILogger<CustomerLoginService> _logger;

        public CustomerLoginService(ICustomerLoginRepository repo, IRecordScopeRepository scope, IEmailService email, ILogger<CustomerLoginService> logger)
        {
            _repo = repo;
            _scope = scope;
            _email = email;
            _logger = logger;
        }

        private sealed record Outcome(TempPasswordIssued? Issued, int Status, string? Error);

        public async Task<bool> HasLoginWithoutLocationAsync(int customerId, CancellationToken ct = default)
        {
            var locations = await _scope.GetLocationIdsAsync(RecordKind.Customer, customerId.ToString());
            if (locations is { Count: > 0 }) return false; // has a location: RecordScope already decided
            var info = (await _repo.GetStatusAsync(new[] { customerId }, ct)).FirstOrDefault();
            return info != null && info.Status != CustomerLoginInfo.None;
        }

        public async Task<(int StatusCode, ApiResponse Response)> IssueAsync(int customerId, CancellationToken ct = default)
        {
            try
            {
                var info = (await _repo.GetStatusAsync(new[] { customerId }, ct)).FirstOrDefault();
                if (info is null) return (404, ApiResponse.Fail("Customer not found."));
                var o = await IssueForAsync(info, ct);
                return o.Issued is null
                    ? (o.Status, ApiResponse.Fail(o.Error ?? "Could not create the temporary password."))
                    : (200, ApiResponse.Ok(o.Issued, o.Issued.Emailed ? "Temporary password created and emailed." : "Temporary password created; the email could not be sent."));
            }
            catch (CustomerLoginMissingException ex)
            {
                return (503, ApiResponse.Fail(ex.Message));
            }
        }

        public async Task<(TempPasswordIssued? Login, string? Error)> TryIssueAsync(int customerId, CancellationToken ct = default)
        {
            try
            {
                var info = (await _repo.GetStatusAsync(new[] { customerId }, ct)).FirstOrDefault();
                if (info is null) return (null, "Customer not found.");
                var o = await IssueForAsync(info, ct);
                return (o.Issued, o.Error);
            }
            catch (CustomerLoginMissingException ex)
            {
                return (null, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Temporary password for new customer {CustomerId} failed", customerId);
                return (null, "The temporary password could not be created.");
            }
        }

        public async Task<(int StatusCode, ApiResponse Response)> IssueBulkAsync(CancellationToken ct = default)
        {
            List<CustomerLoginInfo> all;
            try
            {
                all = await _repo.GetStatusAsync(null, ct);
            }
            catch (CustomerLoginMissingException ex)
            {
                return (503, ApiResponse.Fail(ex.Message));
            }

            var created = new List<object>();
            var skipped = new List<object>();
            foreach (var info in all)
            {
                string? reason = null;
                if (string.IsNullOrWhiteSpace(info.Email)) reason = NoEmailMessage;
                else if (!info.CustomerActive) reason = "The customer is inactive.";
                else if (info.Status == CustomerLoginInfo.Active) reason = "The login is already active.";
                else if (info.UserId.HasValue && !info.HasCustomerUser) reason = "This email belongs to a staff account.";
                else if (info.UserId.HasValue && !info.UserActive) reason = "The login is turned off.";
                if (reason != null)
                {
                    skipped.Add(new { customerId = info.CustomerId, name = info.DisplayName, reason });
                    continue;
                }

                try
                {
                    var o = await IssueForAsync(info, ct);
                    if (o.Issued is null)
                        skipped.Add(new { customerId = info.CustomerId, name = info.DisplayName, reason = o.Error ?? "Could not create the temporary password." });
                    else
                        created.Add(new { customerId = info.CustomerId, name = info.DisplayName, email = o.Issued.Email, tempPassword = o.Issued.TempPassword, emailed = o.Issued.Emailed });
                }
                catch (Exception ex) when (ex is not CustomerLoginMissingException)
                {
                    _logger.LogError(ex, "Temporary password for customer {CustomerId} failed", info.CustomerId);
                    skipped.Add(new { customerId = info.CustomerId, name = info.DisplayName, reason = "The temporary password could not be created." });
                }
                catch (CustomerLoginMissingException ex)
                {
                    return (503, ApiResponse.Fail(ex.Message));
                }
            }
            return (200, ApiResponse.Ok(new { created, skipped }, $"{created.Count} temporary password(s) created, {skipped.Count} skipped."));
        }

        private async Task<Outcome> IssueForAsync(CustomerLoginInfo info, CancellationToken ct)
        {
            var email = info.Email?.Trim();
            if (string.IsNullOrEmpty(email)) return new Outcome(null, 400, NoEmailMessage);

            var user = await _repo.EnsureLoginUserAsync(info.CustomerId, ct);
            if (user is null) return new Outcome(null, 400, NoEmailMessage);
            if (user.Value.RoleId != Roles.GeneralId) return new Outcome(null, 400, "This email belongs to a staff account, not a customer login.");
            if (!user.Value.IsActive) return new Outcome(null, 400, "This customer's login is turned off.");

            var temp = PasswordRules.GenerateTemp();
            if (!await _repo.SetTempPasswordAsync(user.Value.UserId, PasswordHasher.Hash(temp), temp, ct))
                return new Outcome(null, 400, "This email belongs to a staff account, not a customer login.");

            // Never log the password: only that one was issued.
            _logger.LogInformation("Temporary password issued for customer {CustomerId}", info.CustomerId);
            var emailed = await SendAsync(info, email, temp);
            return new Outcome(new TempPasswordIssued(info.CustomerId, email, temp, emailed), 200, null);
        }

        private async Task<bool> SendAsync(CustomerLoginInfo info, string email, string temp)
        {
            try
            {
                await _email.SendTypedEmailAsync(EmailTypes.CustomerLogin, email, "Your WorkNest login", BuildEmailHtml(info.FirstName ?? info.DisplayName, email, temp), "WorkNest Reports");
                return true;
            }
            catch (EmailHeldException)
            {
                return false; // Email Sending is off: held in Pending emails, goes out when it is turned on
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Login email to customer {CustomerId} was not sent", info.CustomerId);
                return false;
            }
        }

        public static string BuildEmailHtml(string? firstName, string email, string tempPassword)
        {
            string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
            var googleNote = PasswordRules.CanUseGoogle(email)
                ? "<p>Your email is a Gmail address, so you can also sign in with <b>Continue with Google</b> and skip the password.</p>"
                : "<p>If you use a Gmail address you can also sign in with <b>Continue with Google</b>.</p>";
            return $@"<div style=""font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#222;line-height:1.5"">
<p>Hello {E(firstName)},</p>
<p>A WorkNest login has been created for you.</p>
<p>Email: <b>{E(email)}</b><br/>Temporary password: <b style=""font-size:17px;letter-spacing:1px"">{E(tempPassword)}</b></p>
<p>Sign in at <a href=""{LoginUrl}"">{LoginUrl}</a>. You will be asked to choose your own password the first time you sign in.</p>
{googleNote}
<p>WorkNest</p></div>";
        }

        public async Task EnrichAsync(IEnumerable<IDictionary<string, object?>> rows, bool includeTempPassword, CancellationToken ct = default)
        {
            var list = rows.ToList();
            if (list.Count == 0) return;
            foreach (var row in list)
            {
                var email = row.TryGetValue("Email", out var e) ? e?.ToString() : null;
                row["canUseGoogle"] = PasswordRules.CanUseGoogle(email);
                row["loginStatus"] = null;
                row["tempPassword"] = null;
            }

            var ids = list.Select(r => r.TryGetValue("Id", out var v) && v is not null && int.TryParse(v.ToString(), out var id) ? id : 0)
                          .Where(i => i > 0).Distinct().ToList();
            if (ids.Count == 0) return;
            Dictionary<int, CustomerLoginInfo> byId;
            try
            {
                byId = (await _repo.GetStatusAsync(ids, ct)).ToDictionary(i => i.CustomerId);
            }
            catch (CustomerLoginMissingException)
            {
                return; // AUTH_TEMP_01 not run: loginStatus stays null
            }

            foreach (var row in list)
            {
                if (!row.TryGetValue("Id", out var v) || v is null || !int.TryParse(v.ToString(), out var id)) continue;
                var status = byId.TryGetValue(id, out var info) ? info.Status : CustomerLoginInfo.None;
                row["loginStatus"] = status;
                row["tempPassword"] = includeTempPassword && status == CustomerLoginInfo.Pending ? info!.TempPassword : null;
            }
        }
    }
}
