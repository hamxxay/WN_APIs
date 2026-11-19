using System.Text.Json;
using WorkNest.Application.DTOs.RentSchedule;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services.BookingSchedules
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

        private readonly IRentScheduleGate? _gate;

        public BookingScheduleService(IRentScheduleBookingRepository repo, IRentScheduleGate? gate = null) { _repo = repo; _gate = gate; }

        private async Task<IAsyncDisposable?> LockAsync(int bookingId, CancellationToken ct) =>
            _gate == null ? null : await _gate.AcquireAsync(bookingId, ct);

        /// <summary>The stored rows; a booking without any first gets its quotation's rows copied if the conversion could not (throws if that fails).</summary>
        private async Task<IReadOnlyList<BookingScheduleRow>> StoredRowsAsync(int bookingId, CancellationToken ct)
        {
            var rows = await _repo.GetRowsAsync(bookingId, ct);
            if (rows.Count > 0) return rows;
            if (await _repo.EnsureBookingScheduleFromQuotationAsync(bookingId, ct) > 0) return await _repo.GetRowsAsync(bookingId, ct);
            return rows;
        }

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
            var stored = await StoredRowsAsync(bookingId, ct);
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

            if (caller.IsAdmin || caller.IsSuperAdmin)
            {
                await using (await LockAsync(bookingId, ct) ?? NoLock.Instance)
                {
                    var locked = await LoadAsync(bookingId, ct);   // under the lock: no invoice is being built from rows we are about to change
                    var (savedRows, saveError) = BookingScheduleMath.Apply(locked.Rows, rows, locked.Info.TaxPercent);
                    if (savedRows == null) throw new BookingScheduleRuleException(saveError!);
                    await _repo.SaveRowsAsync(bookingId, savedRows, ct);
                }
                return new SaveBookingScheduleResult { PendingApproval = false, Schedule = await GetAsync(bookingId, ct) };
            }

            var l = await LoadAsync(bookingId, ct);
            var (applied, error) = BookingScheduleMath.Apply(l.Rows, rows, l.Info.TaxPercent);
            if (applied == null) throw new BookingScheduleRuleException(error!);

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

        public async Task<IReadOnlyList<BookingScheduleListItem>> ListBookingsAsync(BookingScheduleCaller caller, string? search, bool activeOnly, CancellationToken ct = default)
        {
            await EnsureSetUpAsync(ct);
            // Location-bound staff (the sales manager included) see their own locations only; no location = nothing.
            IReadOnlyCollection<int>? locations = caller.IsSuperAdmin ? null : (caller.LocationIds ?? (IReadOnlyCollection<int>)Array.Empty<int>());
            var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            if (term is { Length: > 100 }) term = term[..100];
            return await _repo.ListBookingsAsync(term, locations, activeOnly, ct);
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
            string json = JsonSerializer.Serialize(inputs.OrderBy(r => r.RowNo).Select(r => new BookingScheduleInputRow
            {
                RowNo = r.RowNo, DiscountType = BookingScheduleMath.NormalizeDiscountType(r.DiscountType), DiscountValue = r.DiscountValue, SecurityDeposit = r.SecurityDeposit
            }), Json);
            await using (await LockAsync(change.BookingId, ct) ?? NoLock.Instance)
            {
                var l = await LoadAsync(change.BookingId, ct);
                var (applied, error) = BookingScheduleMath.Apply(l.Rows, inputs, l.Info.TaxPercent);
                if (applied == null) throw new BookingScheduleRuleException(error!);

                // Write the table first, then mark the change Approved: a failed save leaves the change Pending, never "Approved" with nothing applied.
                await _repo.SaveRowsAsync(change.BookingId, applied, ct);
                if (!await _repo.DecideChangeAsync(changeId, Approved, caller.UserId, cleanNote.Length > 0 ? cleanNote : null, json, ct))
                    throw new BookingScheduleRuleException(NotPendingMessage);
            }

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

        private sealed class NoLock : IAsyncDisposable
        {
            public static readonly NoLock Instance = new();
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private static string CleanNote(string? note) => (note ?? "").Trim();

        // ---- invoicing -----------------------------------------------------------------------------------------------------------

        public async Task<ScheduleInvoiceSource?> GetFirstInvoiceSourceAsync(int bookingId, CancellationToken ct = default)
        {
            if (!await IsSetUpAsync(ct)) return null;
            var rows = await StoredRowsAsync(bookingId, ct);
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
            var rows = (await StoredRowsAsync(bookingId, ct)).ToList();
            if (rows.Count == 0) return (false, null);
            var info = await _repo.GetBillingInfoAsync(bookingId, ct);
            if (info == null || info.IsMeetingRoom) return (false, null);
            var next = BookingScheduleMath.NextUninvoiced(rows);
            if (next == null) return (true, null);
            var group = new List<BookingScheduleRow> { next };
            return (true, new ScheduleInvoiceSource { Booking = info, Rows = group, Figures = BookingScheduleMath.Figures(group), AnyRowInvoiced = rows.Any(r => r.Locked) });
        }

        public Task<int?> FindLiveInvoiceForPeriodAsync(int bookingId, DateTime start, DateTime end, CancellationToken ct = default) =>
            _repo.FindLiveInvoiceForPeriodAsync(bookingId, start, end, ct);

        public Task<bool> MarkInvoicedAsync(int bookingId, IReadOnlyCollection<int> rowNos, int invoiceId, CancellationToken ct = default) =>
            _repo.MarkRowsInvoicedAsync(bookingId, rowNos, invoiceId, ct);
    }
}
