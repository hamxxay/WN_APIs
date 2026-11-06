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
        public const int DefaultPageSize = 25;
        public const int MaxPageSize = 100;

        private readonly IApprovalRepository _repo;
        private readonly IReportRepository _reports;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;
        private readonly ILogger<ApprovalService> _logger;
        private readonly Func<DateTime> _utcNow;

        public ApprovalService(IApprovalRepository repo, IReportRepository reports, IEmailService email, IConfiguration config, ILogger<ApprovalService> logger)
            : this(repo, reports, email, config, logger, () => DateTime.UtcNow) { }

        /// <summary>For tests: a chosen clock.</summary>
        public ApprovalService(IApprovalRepository repo, IReportRepository reports, IEmailService email, IConfiguration config, ILogger<ApprovalService> logger, Func<DateTime> utcNow)
        {
            _repo = repo; _reports = reports; _email = email; _config = config; _logger = logger; _utcNow = utcNow;
        }

        private string BaseUrl => ApprovalEmail.BaseUrl(_config["Approvals:CmsBaseUrl"]);

        private static ApprovalResult<T> Missing<T>() => ApprovalResult<T>.Fail(ApprovalOutcome.Unavailable, ApprovalsMissingException.UserMessage);

        // ---- raise -------------------------------------------------------------------------------------------------

        /// <summary>Stores a Pending request and e-mails the approver the link. Returns the new id.</summary>
        public async Task<ApprovalResult<int>> CreateAsync(ApprovalCreateInput input, int userId, CancellationToken ct = default)
        {
            try
            {
                var invalid = ApprovalRules.ValidateRequest(input.Title, input.Details, input.Reason);
                if (invalid != null) return ApprovalResult<int>.Fail(ApprovalOutcome.Invalid, invalid);
                if (!(await _repo.GetCategoriesAsync(true, ct)).Any(c => c.Id == input.CategoryId))
                    return ApprovalResult<int>.Fail(ApprovalOutcome.Invalid, "Choose a category.");
                if (!(await _repo.GetLocationsAsync(ct)).Any(l => l.Id == input.LocationId))
                    return ApprovalResult<int>.Fail(ApprovalOutcome.Invalid, "Choose a location.");

                var settings = await _repo.GetSettingsAsync(ct);
                var approver = settings?.ApproverEmail?.Trim();
                if (!ApprovalRules.ValidEmail(approver)) return ApprovalResult<int>.Fail(ApprovalOutcome.Invalid, NoApproverMessage);

                var token = ApprovalRules.NewToken();
                var expires = _utcNow().AddDays(ExpiryDays(settings));
                var id = await _repo.CreateRequestAsync(new ApprovalNewRequest(input.LocationId, input.CategoryId, input.Title!.Trim(), input.Details!.Trim(),
                    ApprovalRules.TrimOrNull(input.Reason), userId, approver!, ApprovalRules.HashToken(token)!, expires), ct);

                var detail = await _repo.GetRequestAsync(id, ct);
                if (detail != null)
                {
                    var mail = ApprovalEmail.ToApprover(BaseUrl, token, ToMail(detail), expires);
                    await SendAsync(approver, mail, "approver link", id);
                }
                return ApprovalResult<int>.Ok(id);
            }
            catch (ApprovalsMissingException) { return Missing<int>(); }
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

                await NotifyDecisionAsync(decided.Id, decision, ct);
                var fresh = await _repo.GetRequestByTokenAsync(hash, ct);
                return fresh == null ? NotFoundLink<ApprovalPublicView>() : ApprovalResult<ApprovalPublicView>.Ok(ToPublic(fresh));
            }
            catch (ApprovalsMissingException) { return Missing<ApprovalPublicView>(); }
        }

        private async Task NotifyDecisionAsync(int id, string decision, CancellationToken ct)
        {
            ApprovalRequestDetail? d;
            try { d = await _repo.GetRequestAsync(id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Approval {Id}: could not read the request to send its e-mails.", id); return; }
            if (d == null) return;
            var r = ToMail(d);
            await SendAsync(d.RequestedByEmail, ApprovalEmail.Decision(BaseUrl, r, decision, d.DecisionNote), "decision", id);
            if (decision != ApprovalStatuses.Approved) return;

            if (d.Assignees.Count == 0)
            {
                IReadOnlyList<string> admins;
                try { admins = await _reports.GetSuperAdminEmailsAsync(); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Approval {Id}: could not read the super admin e-mails.", id); return; }
                foreach (var to in admins.Distinct(StringComparer.OrdinalIgnoreCase))
                    await SendAsync(to, ApprovalEmail.NobodyAssigned(BaseUrl, r), "nobody assigned", id);
                return;
            }
            foreach (var p in d.Assignees)
                await SendAsync(p.Email, ApprovalEmail.Assigned(BaseUrl, r, d.DecisionNote), "assigned person", id);
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

                var done = await _repo.GetRequestAsync(id, ct) ?? d;
                await SendAsync(d.RequestedByEmail, ApprovalEmail.Done(BaseUrl, ToMail(d), done.DoneByName, done.DoneNote), "done", id);
                return ApprovalResult<int>.Ok(id);
            }
            catch (ApprovalsMissingException) { return Missing<int>(); }
        }

        /// <summary>Makes an Expired request Pending again with a new link, e-mailed to the approver. The caller's scope is checked by the controller.</summary>
        public async Task<ApprovalResult<int>> ResendAsync(int id, CancellationToken ct = default)
        {
            try
            {
                var d = await _repo.GetRequestAsync(id, ct);
                if (d == null) return ApprovalResult<int>.Fail(ApprovalOutcome.NotFound, "Request not found.");
                if (d.Status != ApprovalStatuses.Expired) return ApprovalResult<int>.Fail(ApprovalOutcome.Conflict, "Only an expired request can be sent again.");
                var settings = await _repo.GetSettingsAsync(ct);
                var approver = settings?.ApproverEmail?.Trim();
                if (!ApprovalRules.ValidEmail(approver)) return ApprovalResult<int>.Fail(ApprovalOutcome.Invalid, NoApproverMessage);

                var token = ApprovalRules.NewToken();
                var expires = _utcNow().AddDays(ExpiryDays(settings));
                if (!await _repo.ResendRequestAsync(id, approver!, ApprovalRules.HashToken(token)!, expires, ct))
                    return ApprovalResult<int>.Fail(ApprovalOutcome.Conflict, "Only an expired request can be sent again.");
                await SendAsync(approver, ApprovalEmail.ToApprover(BaseUrl, token, ToMail(d), expires), "approver link (resend)", id);
                return ApprovalResult<int>.Ok(id);
            }
            catch (ApprovalsMissingException) { return Missing<int>(); }
        }

        /// <summary>Expires the requests past their link and e-mails each person who made one. Returns how many expired. Throws <see cref="ApprovalsMissingException"/> before the script.</summary>
        public async Task<int> ExpireDueAsync(CancellationToken ct = default)
        {
            var ids = await _repo.ExpireDueAsync(ct);
            foreach (var id in ids)
            {
                try
                {
                    var d = await _repo.GetRequestAsync(id, ct);
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

        /// <summary>The list: every location for a super admin, else the caller's locations plus what was sent to them. Paging stays sane past the last page.</summary>
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
                if (page < 1) page = 1;
                pageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
                var scope = caller.IsSuperAdmin ? null : caller.LocationIds;
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
                var locations = (await _repo.GetLocationsAsync(ct)).Where(l => caller.IsSuperAdmin || caller.LocationIds.Contains(l.Id)).ToList();
                return ApprovalResult<ApprovalMetaView>.Ok(new ApprovalMetaView(categories, ApprovalRules.ValidEmail(settings?.ApproverEmail), caller.CanRaise, locations));
            }
            catch (ApprovalsMissingException) { return Missing<ApprovalMetaView>(); }
        }

        /// <summary>The caller's locations include the request, or it was sent to them. A super admin sees all.</summary>
        public static bool MaySee(ApprovalRequestDetail d, ApprovalCaller caller) =>
            caller.IsSuperAdmin || caller.LocationIds.Contains(d.LocationId) || d.Assignees.Any(a => a.UserId == caller.UserId);

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
                ApprovalRules.IsOpen(p.Status, p.TokenExpiresAt, _utcNow()));

        private static ApprovalEmailRequest ToMail(ApprovalRequestDetail d) =>
            new(d.Id, d.LocationName, d.CategoryName, d.Title, d.Details, d.Reason, d.RequestedByName, d.RequestedAt);

        /// <summary>Sends one e-mail; a blank or invalid address is skipped and a failure is logged (never the link), never thrown.</summary>
        private async Task SendAsync(string? to, (string Subject, string Html) mail, string what, int requestId)
        {
            if (!ApprovalRules.ValidEmail(to)) { _logger.LogWarning("Approval {Id}: no e-mail address for the {What} e-mail; skipped.", requestId, what); return; }
            try { await _email.SendHtmlReportAsync(to!.Trim(), mail.Subject, mail.Html); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Approval {Id}: the {What} e-mail could not be sent.", requestId, what);
            }
        }
    }
}
