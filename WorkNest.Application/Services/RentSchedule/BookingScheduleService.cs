using System.Text.Json;
using WorkNest.Application.DTOs.RentSchedule;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services.RentSchedule
{
    /// <summary><see cref="IBookingScheduleService"/> over <see cref="IRentScheduleBookingRepository"/> (script CQ_01_rent_schedule).</summary>
    public sealed class BookingScheduleService : IBookingScheduleService
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string SentBack = "SentBack";
        public const int NoteMaxLength = 500;
        public const string MonthlySpacesOnly = "A rent schedule is only for monthly spaces (private office, shared space), not meeting rooms.";
        public const string AlreadyPendingMessage = "This booking already has a rent schedule change waiting for the sales manager.";
        public const string NotPendingMessage = "This change has already been decided.";
        public const string NoteRequiredMessage = "A note is required when sending a change back.";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly IRentScheduleBookingRepository _repo;

        public BookingScheduleService(IRentScheduleBookingRepository repo) { _repo = repo; }

        public async Task<bool> IsSetUpAsync(CancellationToken ct = default)
        {
            try { return await _repo.IsSetUpAsync(ct); } catch (OperationCanceledException) { throw; } catch (Exception) { return false; }
        }

        private async Task EnsureSetUpAsync(CancellationToken ct)
        {
            if (!await IsSetUpAsync(ct)) throw new BookingScheduleNotSetUpException();
        }

        // ---- the table -----------------------------------------------------------------------------------------------------------

        private sealed record Loaded(BookingBillingInfo Info, bool HasSchedule, List<BookingScheduleRow> Rows);

        private async Task<Loaded> LoadAsync(int bookingId, CancellationToken ct)
        {
            var info = await _repo.GetBillingInfoAsync(bookingId, ct) ?? throw new BookingScheduleNotFoundException();
            if (info.IsMeetingRoom) throw new BookingScheduleRuleException(MonthlySpacesOnly);
            var stored = await _repo.GetRowsAsync(bookingId, ct);
            if (stored.Count > 0) return new Loaded(info, true, stored.ToList());

            if (info.StartOn is null || info.EndOn is null)
                throw new BookingScheduleRuleException("The booking has no start and end date, so there is nothing to schedule.");
            var invoiced = await _repo.GetInvoicedPeriodsAsync(bookingId, ct);
            var rows = BookingScheduleMath.Project(new BookingProjectionInput
            {
                ContractStart = info.StartOn.Value, ContractEnd = info.EndOn.Value,
                MonthlyRent = ResolveMonthlyRent(info), Seats = info.Seats, SupportRate = info.SupportRate, TaxPercent = info.TaxPercent,
                DiscountPercentage = info.DiscountPercentage, DiscountAmount = info.DiscountAmount,
                BillingPeriodMonths = info.BillingPeriodMonths,
                InitialDeposit = info.SecurityDepositRequired > 0 ? info.SecurityDepositRequired : info.ResolvedSecurityDeposit,
                Invoiced = invoiced
            });
            return new Loaded(info, false, rows);
        }

        /// <summary>The monthly rent the invoices use (WN_GetBookingInvoiceDetails.MonthlyRent; when 0, the subtotal / total over the contract months, as GenerateNextInvoiceCoreAsync).</summary>
        public static decimal ResolveMonthlyRent(BookingBillingInfo info)
        {
            if (info.MonthlyRent > 0) return info.MonthlyRent;
            int months = info.StartOn.HasValue && info.EndOn.HasValue && (info.EndOn.Value - info.StartOn.Value).TotalDays > 20
                ? Math.Max(1, (int)Math.Round((info.EndOn.Value - info.StartOn.Value).TotalDays / 30.4375)) : 12;
            if (info.SubtotalAmount > 0) return BookingScheduleMath.Round2(info.SubtotalAmount / months);
            if (info.TotalAmount > 0) return BookingScheduleMath.Round2(info.TotalAmount / months);
            return 0m;
        }

        private static BookingScheduleView ToView(Loaded l)
        {
            decimal monthly = ResolveMonthlyRent(l.Info);
            int seats = Math.Max(1, l.Info.Seats);
            return new BookingScheduleView
            {
                Rows = l.Rows.OrderBy(r => r.RowNo).ToList(),
                Totals = BookingScheduleMath.Totals(l.Rows),
                TaxPercent = l.Info.TaxPercent, SupportRate = l.Info.SupportRate, Seats = seats,
                PerSeatPrice = BookingScheduleMath.Round2(monthly / seats),
                DefaultSecurityDeposit = l.Info.SecurityDepositRequired > 0 ? l.Info.SecurityDepositRequired : l.Info.ResolvedSecurityDeposit
            };
        }

        private static BookingScheduleView ToView(BookingBillingInfo info, List<BookingScheduleRow> rows) =>
            ToView(new Loaded(info, true, rows));

        public async Task<BookingScheduleResponse> GetAsync(int bookingId, CancellationToken ct = default)
        {
            await EnsureSetUpAsync(ct);
            var l = await LoadAsync(bookingId, ct);
            var pending = await _repo.GetPendingChangeAsync(bookingId, ct);
            return new BookingScheduleResponse
            {
                HasSchedule = l.HasSchedule,
                PendingChange = pending == null ? null : new BookingSchedulePendingChange { Id = pending.Id, RequestedBy = pending.RequestedByName, RequestedAt = pending.RequestedAt },
                Schedule = ToView(l)
            };
        }

        // ---- saving --------------------------------------------------------------------------------------------------------------

        public async Task<SaveBookingScheduleResult> SaveAsync(BookingScheduleCaller caller, int bookingId, IReadOnlyList<BookingScheduleInputRow>? rows, CancellationToken ct = default)
        {
            await EnsureSetUpAsync(ct);
            if (!caller.IsAdmin && !caller.IsSuperAdmin && !caller.IsSalesExecutive)
                throw new BookingScheduleForbiddenException("You cannot change a booking's rent schedule.");

            var l = await LoadAsync(bookingId, ct);
            var (applied, error) = BookingScheduleMath.Apply(l.Rows, rows, l.Info.TaxPercent);
            if (applied == null) throw new BookingScheduleRuleException(error!);

            if (caller.IsAdmin || caller.IsSuperAdmin)
            {
                await _repo.SaveRowsAsync(bookingId, applied, ct);
                return new SaveBookingScheduleResult { PendingApproval = false, Schedule = await GetAsync(bookingId, ct) };
            }

            // Sales executive: a pending change for the sales manager (one per booking). Only the input values are kept.
            if (await _repo.GetPendingChangeAsync(bookingId, ct) != null)
                throw new BookingScheduleConflictException(AlreadyPendingMessage);
            string json = JsonSerializer.Serialize(rows!.OrderBy(r => r.RowNo).Select(r => new BookingScheduleInputRow
            {
                RowNo = r.RowNo, DiscountType = BookingScheduleMath.NormalizeDiscountType(r.DiscountType), DiscountValue = r.DiscountValue, SecurityDeposit = r.SecurityDeposit
            }), Json);
            var id = await _repo.InsertChangeAsync(bookingId, caller.UserId, json, ct);
            if (id == null) throw new BookingScheduleConflictException(AlreadyPendingMessage);
            return new SaveBookingScheduleResult { PendingApproval = true, ChangeId = id };
        }

        // ---- the manager's queue -------------------------------------------------------------------------------------------------

        private static void EnsureMayAct(BookingScheduleCaller caller, BookingScheduleChange change)
        {
            if (caller.IsSuperAdmin) return;
            if (!caller.IsSalesManager) throw new BookingScheduleForbiddenException("Only a sales manager or a super admin can decide a rent schedule change.");
            bool mine = change.RequestedByManagerId == caller.UserId
                || (change.LocationId is > 0 && caller.LocationIds != null && caller.LocationIds.Contains(change.LocationId.Value));
            if (!mine) throw new BookingScheduleForbiddenException("This change is not routed to you.");
        }

        private static T Fill<T>(T dto, BookingScheduleChange c) where T : BookingScheduleChangeRowDto
        {
            dto.Id = c.Id; dto.BookingId = c.BookingId; dto.CustomerName = c.CustomerName; dto.SpaceName = c.SpaceName;
            dto.LocationId = c.LocationId; dto.LocationName = c.LocationName; dto.RequestedById = c.RequestedById;
            dto.RequestedBy = c.RequestedByName; dto.RequestedAt = c.RequestedAt; dto.Status = c.Status;
            dto.DecidedBy = c.DecidedByName; dto.DecidedAt = c.DecidedAt; dto.Note = c.Note;
            return dto;
        }

        public async Task<IReadOnlyList<BookingScheduleChangeRowDto>> ListChangesAsync(BookingScheduleCaller caller, bool pendingOnly, CancellationToken ct = default)
        {
            await EnsureSetUpAsync(ct);
            var list = await _repo.ListChangesAsync(pendingOnly, caller.IsSuperAdmin ? null : caller.UserId, caller.IsSuperAdmin ? null : caller.LocationIds, ct);
            return list.Select(c => Fill(new BookingScheduleChangeRowDto(), c)).ToList();
        }

        public async Task<BookingScheduleChangeDetailDto> GetChangeAsync(BookingScheduleCaller caller, int changeId, CancellationToken ct = default)
        {
            await EnsureSetUpAsync(ct);
            var change = await _repo.GetChangeAsync(changeId, ct) ?? throw new BookingScheduleNotFoundException("Change not found.");
            EnsureMayAct(caller, change);
            return await BuildDetailAsync(change, ct);
        }

        private async Task<BookingScheduleChangeDetailDto> BuildDetailAsync(BookingScheduleChange change, CancellationToken ct)
        {
            var detail = Fill(new BookingScheduleChangeDetailDto(), change);
            Loaded l;
            try { l = await LoadAsync(change.BookingId, ct); }
            catch (BookingScheduleRuleException ex) { detail.Problem = ex.Message; return detail; }
            detail.Current = ToView(l);
            var rows = ParseRows(change.RowsJson);
            var (applied, error) = BookingScheduleMath.Apply(l.Rows, rows, l.Info.TaxPercent);
            if (applied != null) detail.Requested = ToView(l.Info, applied); else detail.Problem = error;
            return detail;
        }

        private static List<BookingScheduleInputRow> ParseRows(string json)
        {
            try { return JsonSerializer.Deserialize<List<BookingScheduleInputRow>>(json, Json) ?? new(); }
            catch (JsonException) { return new(); }
        }

        public async Task<(string Message, BookingScheduleChangeDetailDto Detail)> ApproveAsync(BookingScheduleCaller caller, int changeId,
            IReadOnlyList<BookingScheduleInputRow>? editedRows, string? note, CancellationToken ct = default)
        {
            await EnsureSetUpAsync(ct);
            var change = await _repo.GetChangeAsync(changeId, ct) ?? throw new BookingScheduleNotFoundException("Change not found.");
            EnsureMayAct(caller, change);
            if (!string.Equals(change.Status, Pending, StringComparison.OrdinalIgnoreCase)) throw new BookingScheduleRuleException(NotPendingMessage);
            string cleanNote = CleanNote(note);
            if (cleanNote.Length > NoteMaxLength) throw new BookingScheduleRuleException($"The note is limited to {NoteMaxLength} characters.");

            IReadOnlyList<BookingScheduleInputRow> inputs = editedRows is { Count: > 0 } ? editedRows : ParseRows(change.RowsJson);
            var l = await LoadAsync(change.BookingId, ct);
            var (applied, error) = BookingScheduleMath.Apply(l.Rows, inputs, l.Info.TaxPercent);
            if (applied == null) throw new BookingScheduleRuleException(error!);

            // Decide first (only a Pending change can be), then write the table.
            string json = JsonSerializer.Serialize(inputs.OrderBy(r => r.RowNo).Select(r => new BookingScheduleInputRow
            {
                RowNo = r.RowNo, DiscountType = BookingScheduleMath.NormalizeDiscountType(r.DiscountType), DiscountValue = r.DiscountValue, SecurityDeposit = r.SecurityDeposit
            }), Json);
            if (!await _repo.DecideChangeAsync(changeId, Approved, caller.UserId, cleanNote.Length > 0 ? cleanNote : null, json, ct))
                throw new BookingScheduleRuleException(NotPendingMessage);
            await _repo.SaveRowsAsync(change.BookingId, applied, ct);

            var fresh = await _repo.GetChangeAsync(changeId, ct) ?? change;
            return ("Approved. The booking now bills from this rent schedule.", await BuildDetailAsync(fresh, ct));
        }

        public async Task<BookingScheduleChangeDetailDto> SendBackAsync(BookingScheduleCaller caller, int changeId, string? note, CancellationToken ct = default)
        {
            await EnsureSetUpAsync(ct);
            string cleanNote = CleanNote(note);
            if (cleanNote.Length == 0) throw new BookingScheduleRuleException(NoteRequiredMessage);
            if (cleanNote.Length > NoteMaxLength) throw new BookingScheduleRuleException($"The note is limited to {NoteMaxLength} characters.");
            var change = await _repo.GetChangeAsync(changeId, ct) ?? throw new BookingScheduleNotFoundException("Change not found.");
            EnsureMayAct(caller, change);
            if (!string.Equals(change.Status, Pending, StringComparison.OrdinalIgnoreCase)) throw new BookingScheduleRuleException(NotPendingMessage);
            if (!await _repo.DecideChangeAsync(changeId, SentBack, caller.UserId, cleanNote, null, ct))
                throw new BookingScheduleRuleException(NotPendingMessage);
            var fresh = await _repo.GetChangeAsync(changeId, ct) ?? change;
            return await BuildDetailAsync(fresh, ct);
        }

        private static string CleanNote(string? note) => (note ?? "").Trim();

        // ---- invoicing -----------------------------------------------------------------------------------------------------------

        public async Task<ScheduleInvoiceSource?> GetFirstInvoiceSourceAsync(int bookingId, CancellationToken ct = default)
        {
            if (!await IsSetUpAsync(ct)) return null;
            var rows = await _repo.GetRowsAsync(bookingId, ct);
            if (rows.Count == 0) return null;
            var info = await _repo.GetBillingInfoAsync(bookingId, ct);
            if (info?.StartOn == null || info.IsMeetingRoom) return null;
            var group = BookingScheduleMath.FirstInvoiceGroup(rows.ToList(), info.StartOn.Value, info.BillingPeriodMonths);
            if (group.Count == 0) return null;
            return new ScheduleInvoiceSource { Booking = info, Rows = group, Figures = BookingScheduleMath.Figures(group) };
        }

        public async Task<(bool HasSchedule, ScheduleInvoiceSource? Next)> GetNextInvoiceSourceAsync(int bookingId, CancellationToken ct = default)
        {
            if (!await IsSetUpAsync(ct)) return (false, null);
            var rows = (await _repo.GetRowsAsync(bookingId, ct)).ToList();
            if (rows.Count == 0) return (false, null);
            var info = await _repo.GetBillingInfoAsync(bookingId, ct);
            if (info == null || info.IsMeetingRoom) return (false, null);
            var next = BookingScheduleMath.NextUninvoiced(rows);
            if (next == null) return (true, null);
            var group = new List<BookingScheduleRow> { next };
            return (true, new ScheduleInvoiceSource { Booking = info, Rows = group, Figures = BookingScheduleMath.Figures(group), AnyRowInvoiced = rows.Any(r => r.Locked) });
        }

        public Task<bool> MarkInvoicedAsync(int bookingId, IReadOnlyCollection<int> rowNos, int invoiceId, CancellationToken ct = default) =>
            _repo.MarkRowsInvoicedAsync(bookingId, rowNos, invoiceId, ct);
    }
}
