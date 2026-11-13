using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Announcement;
using WorkNest.Application.DTOs.Quotation;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Quotation discount approval (contract of 13 Nov 2026): a sales executive's send of a quotation with a manual discount waits for
    /// the executive's sales manager (WN_Users.ManagerId); the manager approves (optionally editing the discount within the cap), which
    /// sends the quotation to the customer, or sends it back with a note. Every step is written to WN_QuotationHistory. Before the script
    /// QAPR_01 has run nothing is held (old behaviour) and the approval calls answer <see cref="QuotationApprovalNotSetUpException"/>.
    /// </summary>
    public sealed class QuotationApprovalService : IQuotationApprovalService
    {
        public const string ReminderAfterHours = "24";
        public const string EmailSenderName = "WorkNest Quotation Approvals";

        private readonly IQuotationApprovalRepository _repo;
        private readonly IQuotationService _quotations;
        private readonly IDbRepository _db;
        private readonly IAnnouncementService _announcements;
        private readonly IEmailService _email;
        private readonly IConfiguration? _config;

        public QuotationApprovalService(IQuotationApprovalRepository repo, IQuotationService quotations, IDbRepository db,
            IAnnouncementService announcements, IEmailService email, IConfiguration? config = null)
        {
            _repo = repo;
            _quotations = quotations;
            _db = db;
            _announcements = announcements;
            _email = email;
            _config = config;
        }

        private async Task<bool> SetUpAsync(CancellationToken ct)
        {
            try { return await _repo.IsSetUpAsync(ct); } catch (OperationCanceledException) { throw; } catch (Exception) { return false; }
        }

        private async Task EnsureSetUpAsync(CancellationToken ct)
        {
            if (!await SetUpAsync(ct)) throw new QuotationApprovalNotSetUpException();
        }

        private async Task<string?> NameOfAsync(int? userId)
        {
            if (userId is not > 0) return null;
            try { var u = await _db.GetUserByIdAsync(userId.Value); return u != null && u.TryGetValue("Name", out var n) ? n?.ToString() : null; }
            catch (Exception) { return null; }
        }

        // ---- send routing -------------------------------------------------------------------------------------------------------

        public async Task<QuotationApprovalHold?> HoldForApprovalAsync(int quotationId, int? actorUserId, bool actorIsSalesExecutive, CancellationToken ct = default)
        {
            if (!actorIsSalesExecutive) return null;               // admins, super admins, sales managers send directly
            if (!await SetUpAsync(ct)) return null;                // before QAPR_01: old behaviour

            var header = await _db.GetQuotationByIdAsync(quotationId);
            if (header == null || !QuotationApprovalRules.HasManualDiscount(header)) return null;   // an Offer alone is not a discount here

            var info = await _repo.GetInfoAsync(quotationId, ct);
            if (string.Equals(info?.ApprovalStatus, QuotationApprovalRules.Approved, StringComparison.OrdinalIgnoreCase)) return null;   // the manager already approved it

            if (actorUserId is not > 0)
                throw new InvalidOperationException("Your account could not be verified. Sign in again.");

            var map = await _db.GetUserManagerMapAsync(new[] { actorUserId.Value }) ?? new Dictionary<int, (int? ManagerId, string? ManagerName)>();
            if (!map.TryGetValue(actorUserId.Value, out var mgr) || mgr.ManagerId is not > 0)
                throw new InvalidOperationException(QuotationApprovalRules.NoManagerMessage);

            string? managerName = mgr.ManagerName ?? await NameOfAsync(mgr.ManagerId);
            if (string.Equals(info?.ApprovalStatus, QuotationApprovalRules.PendingManager, StringComparison.OrdinalIgnoreCase))
                return Hold(info!.ManagerName ?? managerName);   // already waiting: nothing new to record or send

            bool resubmit = string.Equals(info?.ApprovalStatus, QuotationApprovalRules.SentBack, StringComparison.OrdinalIgnoreCase);
            if (!await _repo.SubmitAsync(quotationId, mgr.ManagerId.Value, DateTime.UtcNow, ct))
                return Hold(managerName);

            var (type, value) = QuotationApprovalRules.ManualDiscountOf(header);
            await _repo.AddHistoryAsync(new QuotationHistoryWrite
            {
                QuotationId = quotationId,
                ByUserId = actorUserId,
                ByName = await NameOfAsync(actorUserId),
                Action = resubmit ? QuotationApprovalRules.ActionResubmitted : QuotationApprovalRules.ActionSubmitted,
                ToDiscount = QuotationApprovalRules.DiscountLabel(type, value),
                Note = info?.DiscountReason
            }, ct);

            await NotifyManagerWaitingAsync(quotationId, mgr.ManagerId.Value, actorUserId.Value, reminder: false);
            return Hold(managerName);
        }

        private static QuotationApprovalHold Hold(string? managerName) => new()
        {
            Message = QuotationApprovalRules.SentToManagerMessage(managerName),
            ApprovalStatus = QuotationApprovalRules.PendingManager
        };

        public async Task RecordSentAsync(int quotationId, int? actorUserId, CancellationToken ct = default)
        {
            try
            {
                if (!await SetUpAsync(ct)) return;
                await _repo.AddHistoryAsync(new QuotationHistoryWrite
                {
                    QuotationId = quotationId, ByUserId = actorUserId, ByName = await NameOfAsync(actorUserId),
                    Action = QuotationApprovalRules.ActionSentToCustomer
                }, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { /* the quotation was sent; the history line is best effort */ }
        }

        public async Task RecordNewVersionAsync(int sourceQuotationId, int newQuotationId, int? actorUserId, CancellationToken ct = default)
        {
            try
            {
                if (!await SetUpAsync(ct)) return;
                await _repo.CopyReasonToNewVersionAsync(sourceQuotationId, newQuotationId, ct);
                await _repo.AddHistoryAsync(new QuotationHistoryWrite
                {
                    QuotationId = newQuotationId, ByUserId = actorUserId, ByName = await NameOfAsync(actorUserId),
                    Action = QuotationApprovalRules.ActionEdited,
                    Note = "New version made from an earlier version of this quotation."
                }, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { }
        }

        // ---- the manager's list / detail ----------------------------------------------------------------------------------------

        private static void EnsureMayAct(QuotationApprovalCaller caller, QuotationApprovalInfo info)
        {
            if (caller.IsSuperAdmin) return;
            if (info.ManagerUserId != caller.UserId)
                throw new QuotationApprovalForbiddenException(QuotationApprovalRules.NotYoursMessage);
            if (caller.LocationIds != null && info.LocationId is > 0 && !caller.LocationIds.Contains(info.LocationId.Value))
                throw new QuotationApprovalForbiddenException("This quotation is not in one of your locations.");
        }

        public async Task<IReadOnlyList<QuotationApprovalRowDto>> ListAsync(QuotationApprovalCaller caller, bool pendingOnly, CancellationToken ct = default)
        {
            await EnsureSetUpAsync(ct);
            var infos = await _repo.ListAsync(caller.IsSuperAdmin ? null : caller.UserId, pendingOnly, caller.IsSuperAdmin ? null : caller.LocationIds, ct);
            var rows = new List<QuotationApprovalRowDto>();
            foreach (var info in infos)
            {
                var row = await BuildRowAsync(info, new QuotationApprovalRowDto());
                if (row != null) rows.Add(row);
            }
            return rows;
        }

        public async Task<QuotationApprovalDetailDto> GetAsync(QuotationApprovalCaller caller, int quotationId, CancellationToken ct = default)
        {
            await EnsureSetUpAsync(ct);
            var info = await _repo.GetInfoAsync(quotationId, ct);
            if (info == null || string.IsNullOrEmpty(info.ApprovalStatus)) throw new QuotationApprovalNotFoundException();
            EnsureMayAct(caller, info);
            return await BuildDetailAsync(info, ct);
        }

        private async Task<QuotationApprovalDetailDto> BuildDetailAsync(QuotationApprovalInfo info, CancellationToken ct)
        {
            var detail = await BuildRowAsync(info, new QuotationApprovalDetailDto()) ?? throw new QuotationApprovalNotFoundException();
            detail.History = (await _repo.GetHistoryAsync(info.QuotationId, ct)).ToList();
            return detail;
        }

        private async Task<T?> BuildRowAsync<T>(QuotationApprovalInfo info, T row) where T : QuotationApprovalRowDto
        {
            var q = await _quotations.GetQuotationByIdAsync(info.QuotationId);
            var header = await _db.GetQuotationByIdAsync(info.QuotationId);
            if (q == null || header == null) return null;
            var (type, value) = QuotationApprovalRules.ManualDiscountOf(header);
            decimal amount = header.TryGetValue("DiscountAmount", out var da) && da is not null && da is not DBNull ? Convert.ToDecimal(da, CultureInfo.InvariantCulture) : 0m;
            decimal subtotal = q.SubtotalAmount;
            row.QuotationId = q.Id;
            row.QuotationNumber = q.QuotationNumber;
            row.CustomerName = q.CustomerName;
            row.Company = q.CustomerCompany;
            row.SpaceName = q.SpaceName;
            row.LocationName = q.LocationName;
            row.ExecutiveName = info.ExecutiveName;
            row.BasePrice = q.MonthlyBasePrice is > 0 ? q.MonthlyBasePrice.Value : subtotal;
            row.DiscountType = type;
            row.DiscountValue = value;
            row.DiscountAmount = amount;
            row.FinalAmount = Math.Max(0m, subtotal - amount);
            row.CapPercent = await _quotations.GetDiscountCapPercentAsync(info.QuotationId);
            row.DiscountReason = info.DiscountReason;
            row.SubmittedAt = info.SubmittedAt;
            row.ApprovalStatus = info.ApprovalStatus;
            return row;
        }

        // ---- approve / send back ------------------------------------------------------------------------------------------------

        public async Task<(string Message, QuotationApprovalDetailDto Row)> ApproveAsync(QuotationApprovalCaller caller, int quotationId,
            ApproveQuotationRequest request, CancellationToken ct = default)
        {
            await EnsureSetUpAsync(ct);
            request ??= new ApproveQuotationRequest();
            var info = await _repo.GetInfoAsync(quotationId, ct) ?? throw new QuotationApprovalNotFoundException();
            EnsureMayAct(caller, info);
            if (!string.Equals(info.ApprovalStatus, QuotationApprovalRules.PendingManager, StringComparison.OrdinalIgnoreCase))
                throw new QuotationApprovalRuleException(QuotationApprovalRules.NotPendingMessage);

            string note = QuotationApprovalRules.Clean(request.Note);
            if (note.Length > QuotationApprovalRules.NoteMaxLength) throw new QuotationApprovalRuleException(QuotationApprovalRules.NoteTooLongMessage);
            string? byName = await NameOfAsync(caller.UserId);

            // 1. The optional edit of the discount (within the offering-type cap), before anything is decided.
            if (request.DiscountValue.HasValue || !string.IsNullOrWhiteSpace(request.DiscountType))
            {
                var header = await _db.GetQuotationByIdAsync(quotationId) ?? throw new QuotationApprovalNotFoundException();
                var (curType, curValue) = QuotationApprovalRules.ManualDiscountOf(header);
                string newType = string.IsNullOrWhiteSpace(request.DiscountType) ? curType : QuotationApprovalRules.NormalizeType(request.DiscountType);
                decimal newValue = request.DiscountValue ?? curValue;
                var (from, to, changed) = await _quotations.ApplyDiscountEditAsync(quotationId, newType, newValue, caller.UserId);
                if (changed)
                    await _repo.AddHistoryAsync(new QuotationHistoryWrite
                    {
                        QuotationId = quotationId, ByUserId = caller.UserId, ByName = byName,
                        Action = QuotationApprovalRules.ActionDiscountEdited, FromDiscount = from, ToDiscount = to,
                        Note = note.Length > 0 ? note : null
                    }, ct);
            }

            // 2. Sent to the customer through the existing send path (status Sent), then the decision is recorded.
            await _quotations.SendQuotationAsync(quotationId, caller.UserId);
            if (!await _repo.DecideAsync(quotationId, QuotationApprovalRules.Approved, caller.UserId, note.Length > 0 ? note : null, DateTime.UtcNow, ct))
                throw new QuotationApprovalRuleException(QuotationApprovalRules.NotPendingMessage);
            await _repo.AddHistoryAsync(new QuotationHistoryWrite
            {
                QuotationId = quotationId, ByUserId = caller.UserId, ByName = byName,
                Action = QuotationApprovalRules.ActionApproved, Note = note.Length > 0 ? note : null
            }, ct);
            await _repo.AddHistoryAsync(new QuotationHistoryWrite
            {
                QuotationId = quotationId, ByUserId = caller.UserId, ByName = byName,
                Action = QuotationApprovalRules.ActionSentToCustomer
            }, ct);

            // 3. The quotation e-mail to the customer (the normal path). The approval stands even if the e-mail cannot go.
            string message = "Approved and sent to the customer.";
            try { await _quotations.SendQuotationEmailAsync(quotationId, null); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message = $"Approved and marked as sent, but the e-mail to the customer could not be sent: {ex.Message}";
            }

            await NotifyExecutiveAsync(info, caller.UserId, byName, approved: true, note);

            var fresh = await _repo.GetInfoAsync(quotationId, ct) ?? info;
            return (message, await BuildDetailAsync(fresh, ct));
        }

        public async Task<QuotationApprovalDetailDto> SendBackAsync(QuotationApprovalCaller caller, int quotationId, SendBackQuotationRequest request, CancellationToken ct = default)
        {
            await EnsureSetUpAsync(ct);
            string note = QuotationApprovalRules.Clean(request?.Note);
            if (note.Length == 0) throw new QuotationApprovalRuleException(QuotationApprovalRules.NoteRequiredMessage);
            if (note.Length > QuotationApprovalRules.NoteMaxLength) throw new QuotationApprovalRuleException(QuotationApprovalRules.NoteTooLongMessage);

            var info = await _repo.GetInfoAsync(quotationId, ct) ?? throw new QuotationApprovalNotFoundException();
            EnsureMayAct(caller, info);
            if (!string.Equals(info.ApprovalStatus, QuotationApprovalRules.PendingManager, StringComparison.OrdinalIgnoreCase))
                throw new QuotationApprovalRuleException(QuotationApprovalRules.NotPendingMessage);

            if (!await _repo.DecideAsync(quotationId, QuotationApprovalRules.SentBack, caller.UserId, note, DateTime.UtcNow, ct))
                throw new QuotationApprovalRuleException(QuotationApprovalRules.NotPendingMessage);
            string? byName = await NameOfAsync(caller.UserId);
            await _repo.AddHistoryAsync(new QuotationHistoryWrite
            {
                QuotationId = quotationId, ByUserId = caller.UserId, ByName = byName,
                Action = QuotationApprovalRules.ActionSentBack, Note = note
            }, ct);

            await NotifyExecutiveAsync(info, caller.UserId, byName, approved: false, note);
            var fresh = await _repo.GetInfoAsync(quotationId, ct) ?? info;
            return await BuildDetailAsync(fresh, ct);
        }

        // ---- history --------------------------------------------------------------------------------------------------------------

        public async Task<IReadOnlyList<QuotationHistoryEntryDto>?> GetHistoryAsync(int quotationId, int? callerUserId, bool callerSeesAll, CancellationToken ct = default)
        {
            if (!await SetUpAsync(ct)) return new List<QuotationHistoryEntryDto>();
            var info = await _repo.GetInfoAsync(quotationId, ct);
            if (info == null) return null;
            var history = await _repo.GetHistoryAsync(quotationId, ct);
            if (callerSeesAll) return history;
            if (callerUserId is not > 0) return null;
            // an executive / manager sees the history of a quotation they made, were asked to approve, or acted on
            bool involved = info.CreatedByUserId == callerUserId || info.ManagerUserId == callerUserId || info.DecidedByUserId == callerUserId;
            return involved ? history : null;
        }

        // ---- notifications ----------------------------------------------------------------------------------------------------------

        private string CmsLink() => ApprovalEmail.BaseUrl(_config?["Approvals:CmsBaseUrl"]) + "/admin/quotation-approvals";

        private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        private async Task NotifyAsync(int toUserId, int createdByUserId, string title, string body, string subject, string html)
        {
            try
            {
                await _announcements.CreateAnnouncementAsync(new CreateAnnouncementRequest
                {
                    Title = title, Body = body, Type = "Alert", TargetScope = "CustomList", CustomUserIds = new List<int> { toUserId }
                }, createdByUserId);
            }
            catch (Exception) { /* the in-app alert is best effort */ }
            try
            {
                var user = await _db.GetUserByIdAsync(toUserId);
                var to = user != null && user.TryGetValue("Email", out var em) ? em?.ToString() : null;
                if (!string.IsNullOrWhiteSpace(to)) await _email.SendTypedEmailAsync(EmailTypes.QuotationApproval, to, subject, html, EmailSenderName);
            }
            catch (Exception) { /* switch off / SMTP down: the in-app alert and the Quotation approvals page still show it */ }
        }

        private async Task NotifyManagerWaitingAsync(int quotationId, int managerUserId, int executiveUserId, bool reminder)
        {
            try
            {
                var q = await _quotations.GetQuotationByIdAsync(quotationId);
                var header = await _db.GetQuotationByIdAsync(quotationId);
                var info = await _repo.GetInfoAsync(quotationId);
                if (q == null || header == null) return;
                var (type, value) = QuotationApprovalRules.ManualDiscountOf(header);
                string label = QuotationApprovalRules.DiscountLabel(type, value);
                string number = q.QuotationNumber ?? $"#{quotationId}";
                string exec = info?.ExecutiveName ?? await NameOfAsync(executiveUserId) ?? "A sales executive";
                string title = reminder ? $"Reminder: quotation {number} is waiting for your approval" : $"Quotation {number} needs your approval";
                string body = $"{exec} asked for a {label} discount for {q.CustomerName} ({q.SpaceName}, {q.LocationName}). Reason: {info?.DiscountReason}";
                string html = $"<div style='font-family:Arial,sans-serif;max-width:600px'><h2>{E(title)}</h2>" +
                              $"<p>{E(exec)} wants to send quotation <strong>{E(number)}</strong> to {E(q.CustomerName)} " +
                              $"({E(q.SpaceName)}, {E(q.LocationName)}) with a discount of <strong>{E(label)}</strong>.</p>" +
                              $"<p><strong>Reason:</strong><br>{E(info?.DiscountReason).Replace("\n", "<br>")}</p>" +
                              $"<p><a href='{E(CmsLink())}'>Open Quotation approvals</a></p></div>";
                await NotifyAsync(managerUserId, executiveUserId, title, body, $"[WorkNest] {title}", html);
            }
            catch (Exception) { /* notifications never fail the send */ }
        }

        private async Task NotifyExecutiveAsync(QuotationApprovalInfo info, int managerUserId, string? managerName, bool approved, string note)
        {
            try
            {
                int? execId = info.SubmittedByUserId ?? info.CreatedByUserId;
                if (execId is not > 0) return;
                var q = await _quotations.GetQuotationByIdAsync(info.QuotationId);
                string number = q?.QuotationNumber ?? $"#{info.QuotationId}";
                string by = managerName ?? "Your sales manager";
                string title = approved ? $"Quotation {number} approved and sent" : $"Quotation {number} sent back to you";
                string body = approved
                    ? $"{by} approved quotation {number} and it was sent to {q?.CustomerName}.{(note.Length > 0 ? " Note: " + note : "")}"
                    : $"{by} sent quotation {number} back. Note: {note}";
                string html = $"<div style='font-family:Arial,sans-serif;max-width:600px'><h2>{E(title)}</h2><p>{E(body)}</p>" +
                              (approved ? "" : "<p>Edit the quotation if needed and send it again for approval.</p>") + "</div>";
                await NotifyAsync(execId.Value, managerUserId, title, body, $"[WorkNest] {title}", html);
            }
            catch (Exception) { }
        }

        // ---- reminders (a background service calls this every hour) --------------------------------------------------------------------

        public async Task<int> SendDueRemindersAsync(CancellationToken ct = default)
        {
            if (!await SetUpAsync(ct)) return 0;
            var now = DateTime.UtcNow;
            var due = await _repo.GetDueRemindersAsync(now.AddHours(-int.Parse(ReminderAfterHours, CultureInfo.InvariantCulture)), ct);
            int sent = 0;
            foreach (var info in due)
            {
                ct.ThrowIfCancellationRequested();
                if (info.ManagerUserId is not > 0) continue;
                await NotifyManagerWaitingAsync(info.QuotationId, info.ManagerUserId.Value, info.SubmittedByUserId ?? info.CreatedByUserId ?? info.ManagerUserId.Value, reminder: true);
                await _repo.MarkRemindedAsync(info.QuotationId, now, ct);   // once a day while waiting; never sends the quotation
                sent++;
            }
            return sent;
        }
    }
}
