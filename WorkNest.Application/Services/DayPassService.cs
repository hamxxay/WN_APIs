using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.DayPass;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Day Pass: a paid pass for one space, one day and one window (docs/superpowers/specs/2026-11-26-day-pass-design.md).
    /// Location scope: allowedLocationIds null = every location, empty = nothing (as the Space Transfers).
    /// </summary>
    public class DayPassService : IDayPassService
    {
        private readonly IDayPassRepository _repo;
        private readonly IBusinessClock _clock;
        private readonly IEmailService _email;
        private readonly IDayPassReceiptPdf _pdf;
        private readonly IAttendantService _attendants;
        private readonly ILogger<DayPassService> _logger;

        public DayPassService(IDayPassRepository repo, IBusinessClock clock, IEmailService email, IDayPassReceiptPdf pdf,
            IAttendantService attendants, ILogger<DayPassService> logger)
        {
            _repo = repo; _clock = clock; _email = email; _pdf = pdf; _attendants = attendants; _logger = logger;
        }

        public Task<bool> IsInstalledAsync() => _repo.IsInstalledAsync();

        private static bool Allowed(IReadOnlyCollection<int>? allowed, int locationId) => allowed == null || allowed.Contains(locationId);
        private const string OtherLocation = "This is outside your location(s).";

        // ---- available spaces ---------------------------------------------------------------------

        public async Task<DayPassResult<List<DayPassSpace>>> AvailableSpacesAsync(string? date, string? window, int? locationId, IReadOnlyCollection<int>? allowedLocationIds)
        {
            if (!DayPassRules.TryParseDate(date, out var d)) return DayPassResult<List<DayPassSpace>>.Fail(400, "Choose the date (yyyy-MM-dd).");
            var w = DayPassRules.NormalizeWindow(window);
            if (w == null) return DayPassResult<List<DayPassSpace>>.Fail(400, "Choose the hours: full, morning or evening.");
            if (d.Date < _clock.Today) return DayPassResult<List<DayPassSpace>>.Fail(400, "A day pass cannot be for a day that has passed.");
            if (locationId is > 0 && !Allowed(allowedLocationIds, locationId.Value)) return DayPassResult<List<DayPassSpace>>.Fail(403, OtherLocation);

            IReadOnlyCollection<int>? locs = locationId is > 0 ? new[] { locationId.Value } : allowedLocationIds;
            if (locs != null && locs.Count == 0) return DayPassResult<List<DayPassSpace>>.Success(new List<DayPassSpace>());
            var usage = await _repo.GetSpaceUsageAsync(d, w, locs, null);
            return DayPassResult<List<DayPassSpace>>.Success(usage.Select(ToSpace).Where(s => s.SeatsFree > 0).ToList());
        }

        private static DayPassSpace ToSpace(DayPassSpaceUsage u)
        {
            var type = DayPassRules.SpaceTypeOf(u.CategoryCode);
            var cap = Math.Max(u.Capacity, 1);
            int inUse = DayPassRules.IsShared(type) ? Math.Min(u.SeatsInUse, cap) : (u.BookingCount > 0 ? cap : 0);
            var (free, whole) = DayPassRules.Availability(type, cap, inUse);
            return new DayPassSpace
            {
                SpaceId = u.SpaceId, SpaceName = u.SpaceName, SpaceType = type, LocationId = u.LocationId, LocationName = u.LocationName,
                Capacity = cap, SeatsFree = free, WholeSpaceFree = whole, ListPrice = u.ListPrice
            };
        }

        // ---- quote / create -----------------------------------------------------------------------

        private sealed record Prepared(DayPassCreateData Data, DayPassQuote Quote);

        /// <summary>Validates the request, checks the space and its availability, works out seats, price and tax. Errors go to the quote / the result.</summary>
        private async Task<(Prepared? Ok, int Status, List<string> Errors, List<string> Warnings)> PrepareAsync(DayPassRequest? r, IReadOnlyCollection<int>? allowed, bool forCreate)
        {
            var errors = DayPassRules.Validate(r, _clock.Today);
            var warnings = new List<string>();
            if (errors.Count > 0) return (null, 400, errors, warnings);
            r!.PaymentMethod = r.PaymentMethod!.Trim().ToLowerInvariant();

            if (!Allowed(allowed, r.LocationId)) return (null, 403, new List<string> { OtherLocation }, warnings);
            DayPassRules.TryParseDate(r.Date, out var date);
            var window = DayPassRules.NormalizeWindow(r.Window)!;

            var space = await _repo.GetSpaceInfoAsync(r.SpaceId, _clock.Today);
            if (space == null || !space.IsActive) return (null, 404, new List<string> { "Space not found." }, warnings);
            if (space.LocationId != r.LocationId) return (null, 400, new List<string> { "This space is not at the chosen location." }, warnings);
            if (!Allowed(allowed, space.LocationId)) return (null, 403, new List<string> { OtherLocation }, warnings);
            var type = DayPassRules.SpaceTypeOf(space.Category);

            var usage = (await _repo.GetSpaceUsageAsync(date, window, new[] { space.LocationId }, space.SpaceId)).FirstOrDefault();
            if (usage == null) return (null, 404, new List<string> { "Space not found." }, warnings);
            var sp = ToSpace(usage);
            var (seats, whole, seatError) = DayPassRules.ResolveSeats(type, sp.Capacity, sp.SeatsFree, sp.WholeSpaceFree, r.WholeSpace, r.Seats);
            if (seatError != null) return (null, 409, new List<string> { seatError }, warnings);

            var applyTaxPercent = space.TaxPercent;
            if (r.ApplyTax && applyTaxPercent <= 0) warnings.Add("No sales tax rate is set for this location, so no tax was added.");
            var (unit, sub, tax, total) = DayPassRules.Price(r.PriceMode, r.Amount, seats, r.ApplyTax, applyTaxPercent);
            if (DayPassRules.Round(sub) <= 0) return (null, 400, new List<string> { "Enter the amount (more than zero)." }, warnings);

            var (startsAt, endsAt, blockStart, blockEnd) = DayPassRules.WindowTimes(date, window);
            var data = new DayPassCreateData
            {
                Request = r, Date = date.Date, Window = window, StartsAt = startsAt, EndsAt = endsAt, BlockStart = blockStart, BlockEnd = blockEnd,
                Shift = DayPassRules.ShiftOf(window), WholeSpace = whole, Seats = seats,
                PriceMode = string.Equals(r.PriceMode, DayPassRules.PricePerSeat, StringComparison.OrdinalIgnoreCase) ? DayPassRules.PricePerSeat : DayPassRules.PriceTotal,
                UnitPrice = unit, SubTotal = sub, TaxPercent = r.ApplyTax ? applyTaxPercent : 0m, Tax = tax, Total = total, Today = _clock.Today, Now = _clock.Now
            };
            var quote = new DayPassQuote { SubTotal = sub, TaxPercent = data.TaxPercent, Tax = tax, Total = total, Seats = seats, Warnings = warnings };

            if (forCreate)
            {
                // Customer
                if (!string.IsNullOrWhiteSpace(r.CustomerCode))
                {
                    var c = await _repo.GetCustomerAsync(r.CustomerCode!.Trim());
                    if (c == null) return (null, 404, new List<string> { "Customer not found." }, warnings);
                    if (!DayPassRules.IsEmail(c.Email)) return (null, 400, new List<string> { "This customer has no email on file: add it to the customer first (the receipt and the app login need it)." }, warnings);
                }
                else
                {
                    var conflict = await _repo.FindWalkInConflictAsync(r.WalkIn!.Email!.Trim(), DayPassRules.CnicDigits(r.WalkIn.Cnic));
                    if (conflict != null) return (null, 400, new List<string> { conflict }, warnings);
                }
                if (r.PaymentMethod == DayPassRules.PayBank && !await _repo.IsBankAccountForLocationAsync(r.BankAccountId!.Value, space.LocationId))
                    return (null, 400, new List<string> { "This is not a bank account of this location's company. Choose a bank account from the list." }, warnings);
            }
            return (new Prepared(data, quote), 200, errors, warnings);
        }

        public async Task<DayPassResult<DayPassQuote>> QuoteAsync(DayPassRequest? request, IReadOnlyCollection<int>? allowedLocationIds)
        {
            // A quote never fails on a rule: the clients show errors[] (and the 403 for another location).
            var (ok, status, errors, warnings) = await PrepareAsync(request, allowedLocationIds, forCreate: false);
            if (status == 403) return DayPassResult<DayPassQuote>.Fail(403, errors[0]);
            if (ok == null)
            {
                var seats = request?.Seats ?? 0;
                var (_, sub, tax, total) = DayPassRules.Price(request?.PriceMode, request?.Amount ?? 0m, Math.Max(seats, 1), false, 0m);
                return DayPassResult<DayPassQuote>.Success(new DayPassQuote { SubTotal = sub, Tax = tax, Total = total, Seats = seats, Errors = errors, Warnings = warnings });
            }
            return DayPassResult<DayPassQuote>.Success(ok.Quote);
        }

        public async Task<DayPassResult<DayPassDto>> CreateAsync(DayPassRequest? request, string? actorEmail, IReadOnlyCollection<int>? allowedLocationIds)
        {
            var (prepared, status, errors, _) = await PrepareAsync(request, allowedLocationIds, forCreate: true);
            if (prepared == null) return DayPassResult<DayPassDto>.Fail(status, errors[0]);
            var actor = await _repo.ResolveActorAsync(actorEmail);
            if (actor == null) return DayPassResult<DayPassDto>.Fail(403, "Your user account could not be found.");
            prepared.Data.ActorId = actor.Value.Id;
            prepared.Data.ActorName = actor.Value.Name;
            prepared.Data.ActorEmail = actorEmail;
            try
            {
                var id = await _repo.CreateAsync(prepared.Data);
                var dto = await _repo.GetAsync(id, true);
                return dto == null ? DayPassResult<DayPassDto>.Fail(500, "The pass was saved but could not be read back.")
                                   : DayPassResult<DayPassDto>.Success(Finish(dto), "Day pass created.", 201);
            }
            catch (DayPassException ex) { return DayPassResult<DayPassDto>.Fail(ex.StatusCode, ex.Message); }
        }

        // ---- list / get ----------------------------------------------------------------------------

        private DayPassDto Finish(DayPassDto d)
        {
            d.Status = d.Status == DayPassRules.Cancelled ? DayPassRules.Cancelled : DayPassRules.StatusOf(false, d.EndsAt, _clock.Now);
            return d;
        }

        public async Task<DayPassResult<DayPassPage>> ListAsync(DayPassListFilter filter, IReadOnlyCollection<int>? allowedLocationIds)
        {
            if (filter.LocationId is > 0 && !Allowed(allowedLocationIds, filter.LocationId.Value)) return DayPassResult<DayPassPage>.Fail(403, OtherLocation);
            filter.Page = Math.Max(filter.Page, 1);
            filter.PageSize = Math.Clamp(filter.PageSize, 1, 200);
            var s = (filter.Status ?? "all").Trim().ToLowerInvariant();
            filter.Status = s is "active" or "used" or "cancelled" ? s : "all";
            if (allowedLocationIds != null && allowedLocationIds.Count == 0)
                return DayPassResult<DayPassPage>.Success(new DayPassPage { Page = filter.Page, PageSize = filter.PageSize });
            var page = await _repo.ListAsync(filter, allowedLocationIds, _clock.Now);
            foreach (var d in page.Items) Finish(d);
            return DayPassResult<DayPassPage>.Success(page);
        }

        private async Task<(DayPassDto? Pass, DayPassResult<DayPassDto>? Error)> LoadAsync(int id, IReadOnlyCollection<int>? allowed, bool history)
        {
            var d = await _repo.GetAsync(id, history);
            if (d == null) return (null, DayPassResult<DayPassDto>.Fail(404, "Day pass not found."));
            if (!Allowed(allowed, d.LocationId)) return (null, DayPassResult<DayPassDto>.Fail(403, OtherLocation));
            return (Finish(d), null);
        }

        public async Task<DayPassResult<DayPassDto>> GetAsync(int id, IReadOnlyCollection<int>? allowedLocationIds)
        {
            var (d, err) = await LoadAsync(id, allowedLocationIds, true);
            return err ?? DayPassResult<DayPassDto>.Success(d!);
        }

        // ---- cancel ---------------------------------------------------------------------------------

        public async Task<DayPassResult<DayPassDto>> CancelAsync(int id, string? reason, string? actorEmail, IReadOnlyCollection<int>? allowedLocationIds)
        {
            var (d, err) = await LoadAsync(id, allowedLocationIds, false);
            if (err != null) return err;
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) return DayPassResult<DayPassDto>.Fail(400, "A reason is required to cancel a day pass.");
            if (reason.Length > 500) return DayPassResult<DayPassDto>.Fail(400, "The reason can be 500 characters at most.");
            if (d!.Status == DayPassRules.Cancelled) return DayPassResult<DayPassDto>.Fail(400, "This day pass is already cancelled.");
            if (!DayPassRules.CanCancel(false, DateTime.ParseExact(d.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), _clock.Today))
                return DayPassResult<DayPassDto>.Fail(400, "A day pass can be cancelled the same day or before, not after its day.");
            var actor = await _repo.ResolveActorAsync(actorEmail);
            if (actor == null) return DayPassResult<DayPassDto>.Fail(403, "Your user account could not be found.");
            try { await _repo.CancelAsync(id, reason, actor.Value.Id, actor.Value.Name, _clock.Today); }
            catch (DayPassException ex) { return DayPassResult<DayPassDto>.Fail(ex.StatusCode, ex.Message); }

            // Door access is taken away now (the background job retries when the machines are not reachable).
            try { await RemoveAccessAsync(new DayPassAccessDue { DayPassId = id, BookingId = d.BookingId, BookingDetailId = await DetailOf(d.BookingId), PassNumber = d.PassNumber, Cancelled = true }); }
            catch (Exception ex) { _logger.LogWarning(ex, "Door access of cancelled day pass {Pass} could not be removed right away", d.PassNumber); }

            var after = await _repo.GetAsync(id, true);
            return DayPassResult<DayPassDto>.Success(Finish(after!), "Day pass cancelled.");
        }

        private Task<int?> DetailOf(int bookingId) => _repo.GetBookingDetailIdAsync(bookingId);

        // ---- receipt ---------------------------------------------------------------------------------

        public async Task<DayPassResult<byte[]>> ReceiptPdfAsync(int id, IReadOnlyCollection<int>? allowedLocationIds)
        {
            var (d, err) = await LoadAsync(id, allowedLocationIds, false);
            if (err != null) return DayPassResult<byte[]>.Fail(err.StatusCode, err.Message);
            if (d!.ReceiptId == null) return DayPassResult<byte[]>.Fail(404, "This pass has no receipt.");
            var bytes = _pdf.Generate(d);
            await _repo.AddHistoryAsync(id, "ReceiptPrinted", null, null, null);
            return DayPassResult<byte[]>.Success(bytes);
        }

        public async Task<DayPassResult<object>> EmailReceiptAsync(int id, string? actorEmail, IReadOnlyCollection<int>? allowedLocationIds)
        {
            var (d, err) = await LoadAsync(id, allowedLocationIds, false);
            if (err != null) return DayPassResult<object>.Fail(err.StatusCode, err.Message);
            if (d!.ReceiptId == null) return DayPassResult<object>.Fail(404, "This pass has no receipt.");
            if (!DayPassRules.IsEmail(d.CustomerEmail)) return DayPassResult<object>.Fail(400, "This customer has no email on file.");
            var actor = await _repo.ResolveActorAsync(actorEmail);
            try
            {
                await _email.SendTypedEmailAsync(EmailTypes.Invoice, d.CustomerEmail!.Trim(), $"[WorkNest] Receipt {d.ReceiptNumber} - Day pass {d.PassNumber}", ReceiptHtml(d), "WorkNest");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Receipt e-mail of day pass {Pass} failed", d.PassNumber);
                return DayPassResult<object>.Fail(502, "The receipt could not be e-mailed: " + ex.Message);
            }
            await _repo.AddHistoryAsync(id, "ReceiptEmailed", "Sent to " + d.CustomerEmail, actor?.Id, actor?.Name);
            return DayPassResult<object>.Success(new { message = "Receipt e-mailed to " + d.CustomerEmail + "." }, "Receipt e-mailed to " + d.CustomerEmail + ".");
        }

        public static string ReceiptHtml(DayPassDto d)
        {
            static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
            static string M(decimal v) => "PKR " + v.ToString("N2", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.Append("<div style='font-family:Arial,sans-serif;max-width:560px;margin:0 auto;padding:20px;border:1px solid #e2e8f0;border-radius:8px;'>");
            sb.Append($"<h2 style='margin-top:0;color:#1e293b;'>WorkNest receipt {E(d.ReceiptNumber)}</h2>");
            sb.Append($"<p>Dear {E(d.CustomerName)}, thank you. This is your receipt for day pass <strong>{E(d.PassNumber)}</strong>.</p>");
            sb.Append("<table style='width:100%;border-collapse:collapse;font-size:14px;'>");
            void Row(string k, string v) => sb.Append($"<tr><td style='padding:4px 0;color:#64748b;'>{E(k)}</td><td style='padding:4px 0;text-align:right;'>{E(v)}</td></tr>");
            Row("Space", d.SpaceName + " (" + d.LocationName + ")");
            Row("Date", d.Date);
            Row("Hours", d.WindowLabel);
            Row("Seats", d.WholeSpace ? "Whole space" : d.Seats.ToString(CultureInfo.InvariantCulture));
            Row("Amount", M(d.SubTotal));
            if (d.Tax > 0) Row($"Sales tax ({d.TaxPercent:0.##}%)", M(d.Tax));
            Row("Total paid", M(d.Total));
            Row("Payment", d.PaymentMethod == "bank" ? $"Bank transfer ({d.BankAccountName}) ref {d.PaymentReference}" : "Cash");
            Row("Invoice", d.InvoiceNumber ?? "");
            Row("Door access", d.AccessMode == "cnic" ? "By CNIC (add your people in the WorkNest app)" : "Reception");
            sb.Append("</table><p style='color:#64748b;font-size:12px;'>This is an automatic message from WorkNest.</p></div>");
            return sb.ToString();
        }

        // ---- customers -------------------------------------------------------------------------------

        public async Task<DayPassResult<List<DayPassCustomerRow>>> CustomersAsync(string? search, IReadOnlyCollection<int>? allowedLocationIds)
        {
            search = (search ?? "").Trim();
            if (search.Length < 2) return DayPassResult<List<DayPassCustomerRow>>.Success(new List<DayPassCustomerRow>());
            if (allowedLocationIds != null && allowedLocationIds.Count == 0) return DayPassResult<List<DayPassCustomerRow>>.Success(new List<DayPassCustomerRow>());
            return DayPassResult<List<DayPassCustomerRow>>.Success(await _repo.SearchCustomersAsync(search, allowedLocationIds));
        }

        // ---- door access removal -----------------------------------------------------------------------

        public Task<List<DayPassAccessDue>> AccessDueAsync() => _repo.GetAccessDueAsync(_clock.Now);

        /// <summary>
        /// Takes every person off the pass's room: blocked on the room and entrance machines (offline machines get it queued) and the
        /// assignment ended, exactly like the customer's "remove employee". Logs AccessRemoved per person; marks the pass when all went through.
        /// </summary>
        public async Task<bool> RemoveAccessAsync(DayPassAccessDue due)
        {
            bool allOk = true;
            if (due.BookingDetailId is int detailId)
            {
                foreach (var personId in await _repo.GetAttendantPersonIdsAsync(detailId))
                {
                    var name = await _repo.GetPersonNameAsync(personId) ?? ("Person " + personId);
                    try
                    {
                        var machines = await _attendants.RemoveAttendantAndRevokeAccessAsync(detailId, personId);
                        bool failed = machines.Error != null || machines.Devices.Any(x => !x.Ok && !x.Queued);
                        if (failed) allOk = false;
                        await _repo.AddHistoryAsync(due.DayPassId, "AccessRemoved",
                            (due.Cancelled ? "Pass cancelled: " : "Pass ended: ") + name + (failed ? " (the door machines could not be updated yet; retrying)" : ""), null, null);
                    }
                    catch (Exception ex)
                    {
                        allOk = false;
                        _logger.LogWarning(ex, "Removing {Person} from day pass {Pass} failed", name, due.PassNumber);
                    }
                }
            }
            if (allOk)
            {
                await _repo.MarkAccessRemovedAsync(due.DayPassId);
                if (!due.Cancelled) await _repo.AddHistoryAsync(due.DayPassId, "Used", "The pass ended; door access removed.", null, null);
            }
            return allOk;
        }
    }
}
