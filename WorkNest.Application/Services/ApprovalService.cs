using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Approvals;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>What the controller maps to a status code: Ok 200, Invalid 400, Forbidden 403, NotFound 404, Conflict 409, Unavailable 503.</summary>
    public enum ApprovalOutcome { Ok, Invalid, Forbidden, NotFound, Conflict, Unavailable }

    public sealed record ApprovalResult<T>(ApprovalOutcome Outcome, T? Data, string? Message)
    {
        public bool IsOk => Outcome == ApprovalOutcome.Ok;
        public static ApprovalResult<T> Ok(T data) => new(ApprovalOutcome.Ok, data, null);
        public static ApprovalResult<T> Fail(ApprovalOutcome outcome, string message) => new(outcome, default, message);
    }

    /// <summary>A saved request. <paramref name="EmailSent"/> is false when the e-mail to the approver failed (the request is then Expired, to send again).</summary>
    public sealed record ApprovalCreated(int Id, bool EmailSent);

    public sealed record ApprovalCreateInput(int LocationId, int CategoryId, string? Title, string? Details, string? Reason);

    /// <summary>The signed-in caller as the service needs to know them. <paramref name="CanRaise"/>: sales executive, admin or super admin.</summary>
    public sealed record ApprovalCaller(int UserId, bool IsSuperAdmin, bool CanRaise, IReadOnlyCollection<int> LocationIds);

    /// <summary>A request as the signed-in screens show it (the contract's RequestDetail).</summary>
    public sealed record ApprovalDetailView(ApprovalRequestDetail Request, bool CanMarkDone, bool CanResend);

    public sealed record ApprovalListView(IReadOnlyList<ApprovalRequestRow> Items, int TotalCount, int Page, int PageSize);

    /// <summary>What the New request form needs: the active categories, whether an approver e-mail is set, whether the caller may raise requests, and where.</summary>
    public sealed record ApprovalMetaView(IReadOnlyList<ApprovalCategory> Categories, bool ApproverEmailSet, bool CanCreate, IReadOnlyList<ApprovalLocation> Locations);

    /// <summary>The public page's request (the contract's PublicRequest): no ids, no e-mail addresses.</summary>
    public sealed record ApprovalPublicView(string? LocationName, string CategoryName, string Title, string Details, string? Reason, string Status,
        string? RequestedByName, DateTime RequestedAt, DateTime? DecidedAt, string? DecisionNote, bool CanDecide);

    /// <summary>
    /// Approvals: raise, the approver's one-time link, Mark Done, send again and expiry (script APPROVAL_01). The decision is
    /// one database update, so a link cannot be used twice; e-mails go out only after it, each in its own try / catch, so a
    /// mail failure never undoes or fails a decision. Tokens are never logged.
    /// </summary>
    public sealed class ApprovalService
    {
        public const string LinkUsedMessage = "This link has already been used or has expired.";
        public const string NoApproverMessage = "No approver e-mail is set yet. Ask a super admin to set it in Approval Settings.";
        public const string EmailFailedMessage = "Saved, but the email to the approver failed. Use Send again.";
        public const int DefaultPageSize = 25;
        public const int MaxPage = 10000;
        public const int MaxPageSize = 100;

        private readonly IApprovalRepository _repo;
        private readonly IReportRepository _reports;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;
        private readonly ILogger<ApprovalService> _logger;
        /// <summary>Pakistan time (<c>IBusinessClock.Now</c>): token expiry, "is the link still open" and everything stored are in it.</summary>
        private readonly Func<DateTime> _now;

        public ApprovalService(IApprovalRepository repo, IReportRepository reports, IEmailService email, IConfiguration config, ILogger<ApprovalService> logger, IBusinessClock clock)
            : this(repo, reports, email, config, logger, () => clock.Now) { }

        /// <summary>For tests: a chosen clock. All times are Pakistan time, like the database columns of APPROVAL_01.</summary>
        public ApprovalService(IApprovalRepository repo, IReportRepository reports, IEmailService email, IConfiguration config, ILogger<ApprovalService> logger, Func<DateTime> now)
        {
            _repo = repo; _reports = reports; _email = email; _config = config; _logger = logger; _now = now;
        }

        private string BaseUrl => ApprovalEmail.BaseUrl(_config["Approvals:CmsBaseUrl"]);

        private static ApprovalResult<T> Missing<T>() => ApprovalResult<T>.Fail(ApprovalOutcome.Unavailable, ApprovalsMissingException.UserMessage);

        // ---- raise -------------------------------------------------------------------------------------------------

        /// <summary>Stores a Pending request and e-mails the approver the link. Returns the new id. When that e-mail fails the request is Expired (so it can be sent again) and the result is a success whose message is <see cref="EmailFailedMessage"/> and EmailSent false.</summary>
        public async Task<ApprovalResult<ApprovalCreated>> CreateAsync(ApprovalCreateInput input, int userId, CancellationToken ct = default)
        {
            try
            {
                var invalid = ApprovalRules.ValidateRequest(input.Title, input.Details, input.Reason);
                if (invalid != null) return ApprovalResult<ApprovalCreated>.Fail(ApprovalOutcome.Invalid, invalid);
                if (!(await _repo.GetCategoriesAsync(true, ct)).Any(c => c.Id == input.CategoryId))
                    return ApprovalResult<ApprovalCreated>.Fail(ApprovalOutcome.Invalid, "Choose a category.");
                if (!(await _repo.GetLocationsAsync(new[] { input.LocationId }, ct)).Any(l => l.Id == input.LocationId))
                    return ApprovalResult<ApprovalCreated>.Fail(ApprovalOutcome.Invalid, "Choose a location.");

                var link = await NewLinkAsync(ct);
                if (link.Failure != null) return ApprovalResult<ApprovalCreated>.Fail(ApprovalOutcome.Invalid, link.Failure);

                var id = await _repo.CreateRequestAsync(new ApprovalNewRequest(input.LocationId, input.CategoryId, input.Title!.Trim(), input.Details!.Trim(),
                    ApprovalRules.TrimOrNull(input.Reason), userId, link.Approver!, link.Hash!, link.ExpiresAt), ct);
                // The request is saved: from here a cancelled call or a failed read must not leave it Pending with no e-mail.
                ApprovalRequestDetail? detail = null;
                try { detail = await _repo.GetRequestAsync(id, CancellationToken.None); }
                catch (Exception ex) { _logger.LogError(ex, "Approval {Id}: could not read the new request to e-mail the approver.", id); }
                return await SendLinkAsync(id, detail, link, "approver link");
            }
            catch (ApprovalsMissingException) { return Missing<ApprovalCreated>(); }
        }

        /// <summary>A new one-time link for the approver: token, its hash, expiry and the approver address (or why there is none).</summary>
        private sealed record NewLink(string? Token, byte[]? Hash, DateTime ExpiresAt, string? Approver, string? Failure);

        private async Task<NewLink> NewLinkAsync(CancellationToken ct)
        {
            var settings = await _repo.GetSettingsAsync(ct);
            var approver = settings?.ApproverEmail?.Trim();
            if (!ApprovalRules.ValidEmail(approver)) return new NewLink(null, null, default, null, NoApproverMessage);
            var token = ApprovalRules.NewToken();
            return new NewLink(token, ApprovalRules.HashToken(token), _now().AddDays(ExpiryDays(settings)), approver, null);
        }

        /// <summary>E-mails the approver the link. A real send failure expires the request again so it never sits Pending with a link nobody got; an e-mail held by Email Sending off is not a failure.</summary>
        private async Task<ApprovalResult<ApprovalCreated>> SendLinkAsync(int id, ApprovalRequestDetail? detail, NewLink link, string what)
        {
            var sent = detail == null
                ? SendResult.Failed
                : await SendAsync(link.Approver, ApprovalEmail.ToApprover(BaseUrl, link.Token!, ToMail(detail), link.ExpiresAt), what, id);
            if (sent != SendResult.Failed) return ApprovalResult<ApprovalCreated>.Ok(new ApprovalCreated(id, true));
            try { await _repo.ExpireNowAsync(id, CancellationToken.None); }
            catch (Exception ex) { _logger.LogError(ex, "Approval {Id}: could not expire the request after its approver e-mail failed.", id); }
            // The request is saved, so this is a success (200) that says the e-mail did not go.
            return new ApprovalResult<ApprovalCreated>(ApprovalOutcome.Ok, new ApprovalCreated(id, false), EmailFailedMessage);
        }

        // ---- the approver's page -----------------------------------------------------------------------------------

        /// <summary>Reads the request of a link. Changes nothing, so a mail scanner opening it decides nothing.</summary>
        public async Task<ApprovalResult<ApprovalPublicView>> GetPublicAsync(string? token, CancellationToken ct = default)
        {
            try
            {
                var hash = ApprovalRules.HashToken(token);
                var found = hash == null ? null : await _repo.GetRequestByTokenAsync(hash, ct);
                return found == null ? NotFoundLink<ApprovalPublicView>() : ApprovalResult<ApprovalPublicView>.Ok(ToPublic(found));
            }
            catch (ApprovalsMissingException) { return Missing<ApprovalPublicView>(); }
        }

        /// <summary>
        /// Approves or rejects by link. One database update decides: a used or expired link gets nothing (409) and sends no
        /// e-mail. Afterwards the person who made the request is told and, on approval, the assigned people (or the super
        /// admins when nobody is assigned).
        /// </summary>
        public async Task<ApprovalResult<ApprovalPublicView>> DecideAsync(string? token, string? decision, string? note, CancellationToken ct = default)
        {
            try
            {
                if (decision != ApprovalStatuses.Approved && decision != ApprovalStatuses.Rejected)
                    return ApprovalResult<ApprovalPublicView>.Fail(ApprovalOutcome.Invalid, "Decision must be Approved or Rejected.");
                var tooLong = ApprovalRules.ValidateNote(note);
                if (tooLong != null) return ApprovalResult<ApprovalPublicView>.Fail(ApprovalOutcome.Invalid, tooLong);
                var hash = ApprovalRules.HashToken(token);
                if (hash == null) return NotFoundLink<ApprovalPublicView>();

                var decided = await _repo.DecideAsync(hash, decision, ApprovalRules.TrimOrNull(note), ct);
                if (decided == null)
                    return await _repo.GetRequestByTokenAsync(hash, ct) == null
                        ? NotFoundLink<ApprovalPublicView>()
                        : ApprovalResult<ApprovalPublicView>.Fail(ApprovalOutcome.Conflict, LinkUsedMessage);

                // The decision is saved: from here on nothing may fail it (no cancellation, no 500 from a failed re-read or e-mail).
                await NotifyDecisionAsync(decided.Id, decision);
                try
                {
                    var fresh = await _repo.GetRequestByTokenAsync(hash, CancellationToken.None);
                    if (fresh != null) return ApprovalResult<ApprovalPublicView>.Ok(ToPublic(fresh));
                }
                catch (Exception ex) { _logger.LogError(ex, "Approval {Id}: the decision was saved but the request could not be read again.", decided.Id); }
                return ApprovalResult<ApprovalPublicView>.Ok(new ApprovalPublicView(null, "", "", "", null, decision, null, _now(), _now(), ApprovalRules.TrimOrNull(note), false));
            }
            catch (ApprovalsMissingException) { return Missing<ApprovalPublicView>(); }
        }

        private async Task NotifyDecisionAsync(int id, string decision)
        {
            try
            {
                var d = await _repo.GetRequestAsync(id, CancellationToken.None);
                if (d == null) return;
                var r = ToMail(d);
                var people = decision == ApprovalStatuses.Approved ? d.Assignees.Where(a => ApprovalRules.ValidEmail(a.Email)).ToList() : new List<ApprovalRequestAssignee>();
                await SendAsync(d.RequestedByEmail, ApprovalEmail.Decision(BaseUrl, r, decision, d.DecisionNote, people.Count > 0), "decision", id);
                if (decision != ApprovalStatuses.Approved) return;

                if (people.Count == 0)
                {
                    // Nobody is assigned, or nobody assigned has an address: the super admins hear about it.
                    foreach (var to in (await _reports.GetSuperAdminEmailsAsync()).Distinct(StringComparer.OrdinalIgnoreCase))
                        await SendAsync(to, ApprovalEmail.NobodyAssigned(BaseUrl, r), "nobody assigned", id);
                    return;
                }
                foreach (var p in people)
                    await SendAsync(p.Email, ApprovalEmail.Assigned(BaseUrl, r, d.DecisionNote), "assigned person", id);
            }
            catch (Exception ex) { _logger.LogError(ex, "Approval {Id}: the decision e-mails could not be prepared.", id); }
        }

        // ---- after approval ----------------------------------------------------------------------------------------

        /// <summary>Marks an Approved request Done. Only the super admin or a person the request was sent to may (403).</summary>
        public async Task<ApprovalResult<int>> MarkDoneAsync(int id, int userId, bool isSuperAdmin, string? note, CancellationToken ct = default)
        {
            try
            {
                var tooLong = ApprovalRules.ValidateNote(note);
                if (tooLong != null) return ApprovalResult<int>.Fail(ApprovalOutcome.Invalid, tooLong);
                var d = await _repo.GetRequestAsync(id, ct);
                if (d == null) return ApprovalResult<int>.Fail(ApprovalOutcome.NotFound, "Request not found.");
                if (!ApprovalRules.MayMarkDone(isSuperAdmin, userId, d.Assignees.Select(a => a.UserId)))
                    return ApprovalResult<int>.Fail(ApprovalOutcome.Forbidden, "Only a person this request was sent to, or a super admin, can mark it Done.");
                if (d.Status != ApprovalStatuses.Approved || !await _repo.MarkDoneAsync(id, userId, ApprovalRules.TrimOrNull(note), ct))
                    return ApprovalResult<int>.Fail(ApprovalOutcome.Conflict, "Only an approved request can be marked Done.");

                // Marked Done: a failed re-read must not turn the saved change into an error.
                var done = d;
                try { done = await _repo.GetRequestAsync(id, CancellationToken.None) ?? d; }
                catch (Exception ex) { _logger.LogError(ex, "Approval {Id}: marked Done but the request could not be read again.", id); }
                await SendAsync(d.RequestedByEmail, ApprovalEmail.Done(BaseUrl, ToMail(d), done.DoneByName, done.DoneNote), "done", id);
                return ApprovalResult<int>.Ok(id);
            }
            catch (ApprovalsMissingException) { return Missing<int>(); }
        }

        /// <summary>Makes an Expired request Pending again with a new link, e-mailed to the approver. The caller's scope is checked by the controller.</summary>
        public async Task<ApprovalResult<ApprovalCreated>> ResendAsync(int id, CancellationToken ct = default)
        {
            try
            {
                var d = await _repo.GetRequestAsync(id, ct);
                if (d == null) return ApprovalResult<ApprovalCreated>.Fail(ApprovalOutcome.NotFound, "Request not found.");
                if (d.Status != ApprovalStatuses.Expired) return ApprovalResult<ApprovalCreated>.Fail(ApprovalOutcome.Conflict, "Only an expired request can be sent again.");
                var link = await NewLinkAsync(ct);
                if (link.Failure != null) return ApprovalResult<ApprovalCreated>.Fail(ApprovalOutcome.Invalid, link.Failure);
                if (!await _repo.ResendRequestAsync(id, link.Approver!, link.Hash!, link.ExpiresAt, ct))
                    return ApprovalResult<ApprovalCreated>.Fail(ApprovalOutcome.Conflict, "Only an expired request can be sent again.");
                return await SendLinkAsync(id, d, link, "approver link (resend)");
            }
            catch (ApprovalsMissingException) { return Missing<ApprovalCreated>(); }
        }

        /// <summary>Expires the requests past their link and e-mails each person who made one. Returns how many expired. Throws <see cref="ApprovalsMissingException"/> before the script.</summary>
        public async Task<int> ExpireDueAsync(CancellationToken ct = default)
        {
            var ids = await _repo.ExpireDueAsync(ct);
            foreach (var id in ids)
            {
                try
                {
                    var d = await _repo.GetRequestAsync(id, CancellationToken.None);
                    if (d != null) await SendAsync(d.RequestedByEmail, ApprovalEmail.Expired(BaseUrl, ToMail(d)), "expired", id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Approval {Id}: could not send the expired e-mail.", id); }
            }
            return ids.Count;
        }

        // ---- reading for the signed-in screens ---------------------------------------------------------------------

        /// <summary>One request, if the caller's locations include it or it was sent to them (403 otherwise).</summary>
        public async Task<ApprovalResult<ApprovalDetailView>> GetAsync(int id, ApprovalCaller caller, CancellationToken ct = default)
        {
            try
            {
                var d = await _repo.GetRequestAsync(id, ct);
                if (d == null) return ApprovalResult<ApprovalDetailView>.Fail(ApprovalOutcome.NotFound, "Request not found.");
                if (!MaySee(d, caller)) return ApprovalResult<ApprovalDetailView>.Fail(ApprovalOutcome.Forbidden, "This request belongs to a location you aren't assigned to.");
                return ApprovalResult<ApprovalDetailView>.Ok(new ApprovalDetailView(d, CanMarkDone(d, caller), CanResend(d, caller)));
            }
            catch (ApprovalsMissingException) { return Missing<ApprovalDetailView>(); }
        }

        /// <summary>The list: every location for a super admin, else the caller's locations (only when they can raise requests) plus what was sent to them. Paging stays sane past the last page.</summary>
        public async Task<ApprovalResult<ApprovalListView>> ListAsync(ApprovalCaller caller, string? status, int? categoryId, int page, int pageSize, CancellationToken ct = default)
        {
            try
            {
                string? s = null;
                if (!string.IsNullOrWhiteSpace(status))
                {
                    s = new[] { ApprovalStatuses.Pending, ApprovalStatuses.Approved, ApprovalStatuses.Rejected, ApprovalStatuses.Done, ApprovalStatuses.Expired }
                        .FirstOrDefault(x => string.Equals(x, status.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (s == null) return ApprovalResult<ApprovalListView>.Fail(ApprovalOutcome.Invalid, "Unknown status.");
                }
                page = Math.Clamp(page, 1, MaxPage);
                pageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
                var scope = caller.IsSuperAdmin ? null : ScopeOf(caller);
                var result = await _repo.GetRequestsAsync(scope, caller.UserId, s, categoryId, page, pageSize, ct);
                var total = result.TotalCount;
                // TotalCount rides on the rows, so past the last page it would read 0: take it from the first page.
                if (result.Items.Count == 0 && page > 1)
                    total = (await _repo.GetRequestsAsync(scope, caller.UserId, s, categoryId, 1, 1, ct)).TotalCount;
                return ApprovalResult<ApprovalListView>.Ok(new ApprovalListView(result.Items, total, page, pageSize));
            }
            catch (ApprovalsMissingException) { return Missing<ApprovalListView>(); }
        }

        /// <summary>Active categories, whether an approver e-mail is set, whether the caller may raise requests, and their locations (all for a super admin).</summary>
        public async Task<ApprovalResult<ApprovalMetaView>> MetaAsync(ApprovalCaller caller, CancellationToken ct = default)
        {
            try
            {
                var categories = await _repo.GetCategoriesAsync(true, ct);
                var settings = await _repo.GetSettingsAsync(ct);
                var locations = await _repo.GetLocationsAsync(caller.IsSuperAdmin ? null : ScopeOf(caller), ct);
                return ApprovalResult<ApprovalMetaView>.Ok(new ApprovalMetaView(categories, ApprovalRules.ValidEmail(settings?.ApproverEmail), caller.CanRaise, locations));
            }
            catch (ApprovalsMissingException) { return Missing<ApprovalMetaView>(); }
        }

        /// <summary>The locations whose requests the caller sees: their own when they can raise requests (sales executive, admin); network engineers and access officers see only what was sent to them.</summary>
        private static IReadOnlyCollection<int> ScopeOf(ApprovalCaller caller) => caller.CanRaise ? caller.LocationIds : Array.Empty<int>();

        /// <summary>A super admin sees all; a caller who can raise sees their locations; anyone sees what was sent to them.</summary>
        public static bool MaySee(ApprovalRequestDetail d, ApprovalCaller caller) =>
            caller.IsSuperAdmin || (caller.CanRaise && caller.LocationIds.Contains(d.LocationId)) || d.Assignees.Any(a => a.UserId == caller.UserId);

        public static bool CanMarkDone(ApprovalRequestDetail d, ApprovalCaller caller) =>
            d.Status == ApprovalStatuses.Approved && ApprovalRules.MayMarkDone(caller.IsSuperAdmin, caller.UserId, d.Assignees.Select(a => a.UserId));

        /// <summary>Expired, and the caller may raise requests at its location (an assigned person who cannot raise may not).</summary>
        public static bool CanResend(ApprovalRequestDetail d, ApprovalCaller caller) =>
            d.Status == ApprovalStatuses.Expired && caller.CanRaise && (caller.IsSuperAdmin || caller.LocationIds.Contains(d.LocationId));

        // ---- helpers -----------------------------------------------------------------------------------------------

        private static int ExpiryDays(ApprovalSettings? s) => s is { LinkExpiryDays: > 0 } ? s.LinkExpiryDays : 7;

        private static ApprovalResult<T> NotFoundLink<T>() => ApprovalResult<T>.Fail(ApprovalOutcome.NotFound, "This link isn't valid.");

        private ApprovalPublicView ToPublic(ApprovalPublicRequest p) =>
            new(p.LocationName, p.CategoryName, p.Title, p.Details, p.Reason, p.Status, p.RequestedByName, p.RequestedAt, p.DecidedAt, p.DecisionNote,
                ApprovalRules.IsOpen(p.Status, p.TokenExpiresAt, _now()));

        private static ApprovalEmailRequest ToMail(ApprovalRequestDetail d) =>
            new(d.Id, d.LocationName, d.CategoryName, d.Title, d.Details, d.Reason, d.RequestedByName, d.RequestedAt);

        private enum SendResult { Sent, Held, Skipped, Failed }

        /// <summary>
        /// Sends one e-mail and never throws. Held (Email Sending off, kept in Pending emails) is not a failure; a blank or invalid
        /// address is skipped; anything else, an <see cref="EmailSendingOffException"/> included (nothing was kept), is Failed.
        /// The link is never logged.
        /// </summary>
        private async Task<SendResult> SendAsync(string? to, (string Subject, string Html) mail, string what, int requestId)
        {
            if (!ApprovalRules.ValidEmail(to)) { _logger.LogWarning("Approval {Id}: no e-mail address for the {What} e-mail; skipped.", requestId, what); return SendResult.Skipped; }
            try { await _email.SendApprovalEmailAsync(to!.Trim(), mail.Subject, mail.Html, ApprovalEmail.SenderName); return SendResult.Sent; }
            catch (EmailHeldException)
            {
                _logger.LogInformation("Approval {Id}: the {What} e-mail is held in Pending emails (Email Sending is off).", requestId, what);
                return SendResult.Held;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Approval {Id}: the {What} e-mail could not be sent.", requestId, what);
                return SendResult.Failed;
            }
        }
    }
}
