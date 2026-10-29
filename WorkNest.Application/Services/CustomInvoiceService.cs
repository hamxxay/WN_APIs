using System.Globalization;
using WorkNest.Application.DTOs.CustomInvoice;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// <see cref="ICustomInvoiceService"/> over <see cref="ICustomInvoiceRepository"/> (script CI_01_custom_invoice).
    /// The request is checked here first (<see cref="Validate"/>) so the CMS gets a clear 400 before anything is written;
    /// the procedure checks again inside its transaction (THROW 51000 -> 400 with its message). Before CI_01: 503.
    /// The lookup procedures take no location filter, so an admin's rows are filtered here to their own locations
    /// (<see cref="CustomInvoiceScope"/>); a super admin sees everything.
    /// </summary>
    public sealed class CustomInvoiceService : ICustomInvoiceService
    {
        /// <summary>WN_Invoices.InvoiceTypeId of a custom invoice (paid in full only, like a WHT invoice).</summary>
        public const int InvoiceTypeId = 7;
        public const int MinLines = 2;
        public const int MaxLines = 5;
        public const int MaxTextLength = 500;
        public const int CustomerSearchTop = 20;
        /// <summary>An admin's customer search asks for more rows (the procedure's maximum), as some are then filtered out.</summary>
        public const int ScopedCustomerSearchTop = 50;
        public const int AccountSearchTop = 30;
        public const int DefaultPageSize = 20;
        /// <summary>The list procedure's largest page; an admin's list reads every page (at most <see cref="MaxScopedPages"/>).</summary>
        private const int ProcedurePageSize = 100;
        private const int MaxScopedPages = 50;
        private const int MaxSearchLength = 100;
        private const decimal MaxAmount = 999_999_999_999.99m;

        public const string NotYourBookingMessage = "This booking is not at one of your locations.";
        public const string NotYourLocationMessage = "This location is not one of yours.";

        private readonly ICustomInvoiceRepository _repository;
        private readonly IDbRepository _db;
        private readonly IRecordScopeRepository _records;
        private readonly ICustomInvoiceAccessService _access;
        private readonly IBusinessClock _clock;

        public CustomInvoiceService(ICustomInvoiceRepository repository, IDbRepository db, IRecordScopeRepository records,
            ICustomInvoiceAccessService access, IBusinessClock clock)
        {
            _repository = repository;
            _db = db;
            _records = records;
            _access = access;
            _clock = clock;
        }

        // ---- lookups ---------------------------------------------------------------------------------------------

        public Task<CustomInvoiceResult> SearchCustomersAsync(string? search, CustomInvoiceScope scope) =>
            Run(async () =>
            {
                var rows = await _repository.SearchCustomersAsync(CleanSearch(search), scope.IsAll ? CustomerSearchTop : ScopedCustomerSearchTop);
                if (scope.IsAll) return CustomInvoiceResult.Ok(rows);

                // A customer belongs to the locations of their bookings and of their account (as RecordScope / KYC);
                // one with no location yet (no booking) stays open to every admin.
                var locations = rows.Count == 0
                    ? new Dictionary<int, HashSet<int>>()
                    : await _repository.GetCustomerLocationsAsync(rows.Select(c => (c.UserId, c.CustomerId)).ToList());
                var visible = rows
                    .Where(c => !locations.TryGetValue(c.UserId, out var l) || l.Count == 0 || scope.AllowsAny(l))
                    .Take(CustomerSearchTop)
                    .ToList();
                return CustomInvoiceResult.Ok(visible);
            });

        public Task<CustomInvoiceResult> GetBookingsAsync(int? userId, CustomInvoiceScope scope) =>
            userId is not > 0
                ? Task.FromResult(CustomInvoiceResult.BadRequest("Choose the customer first."))
                : Run(async () =>
                {
                    var rows = await _repository.GetBookingsAsync(userId.Value);
                    return CustomInvoiceResult.Ok(scope.IsAll ? rows : rows.Where(b => scope.Allows(b.LocationId)).ToList());
                });

        public Task<CustomInvoiceResult> GetReplaceableAsync(int? bookingId, CustomInvoiceScope scope) =>
            bookingId is not > 0
                ? Task.FromResult(CustomInvoiceResult.BadRequest("Choose the booking first."))
                : Run(async () =>
                {
                    if (!await BookingAllowedAsync(bookingId.Value, scope))
                        return CustomInvoiceResult.Forbidden(NotYourBookingMessage);
                    return CustomInvoiceResult.Ok(await _repository.GetReplaceableAsync(bookingId.Value));
                });

        public Task<CustomInvoiceResult> GetSpacesAsync(int? locationId, CustomInvoiceScope scope)
        {
            if (locationId is not > 0) return Task.FromResult(CustomInvoiceResult.BadRequest("Choose the location first."));
            if (!scope.Allows(locationId)) return Task.FromResult(CustomInvoiceResult.Forbidden(NotYourLocationMessage));
            return Run(async () => CustomInvoiceResult.Ok(await _repository.GetSpacesAsync(locationId.Value)));
        }

        public async Task<CustomInvoiceResult> GetLocationsAsync(CustomInvoiceScope scope)
        {
            var locations = BranchScope.Options(await _db.GetAllLocationsAsync())
                .Where(l => scope.Allows(l.Id))
                .Select(l => new CustomInvoiceLocation(l.Id, l.Name))
                .ToList();
            return CustomInvoiceResult.Ok(locations);
        }

        public Task<CustomInvoiceResult> GetLedgerDefaultsAsync(int? locationId, DateTime? date, CustomInvoiceScope scope)
        {
            if (locationId is not > 0) return Task.FromResult(CustomInvoiceResult.BadRequest("Choose the location first."));
            if (!scope.Allows(locationId)) return Task.FromResult(CustomInvoiceResult.Forbidden(NotYourLocationMessage));
            return Run(async () => CustomInvoiceResult.Ok(
                await _repository.GetLedgerDefaultsAsync(locationId.Value, (date ?? _clock.Today).Date)));
        }

        public Task<CustomInvoiceResult> SearchAccountsAsync(string? search) =>
            Run(async () => CustomInvoiceResult.Ok(await _repository.SearchAccountsAsync(CleanSearch(search), AccountSearchTop)));

        public Task<CustomInvoiceResult> GetListAsync(int page, string? search, CustomInvoiceScope scope, int pageSize = DefaultPageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, ProcedurePageSize);
            var text = CleanSearch(search);
            if (scope.IsAll)
                return Run(async () => CustomInvoiceResult.Ok(await _repository.GetListAsync(text, page, pageSize)));

            return Run(async () =>
            {
                // Custom invoices are few: read them all, keep the admin's locations, then page.
                var all = new List<CustomInvoiceListItem>();
                for (int p = 1; p <= MaxScopedPages; p++)
                {
                    var chunk = await _repository.GetListAsync(text, p, ProcedurePageSize);
                    all.AddRange(chunk.Items);
                    if (chunk.Items.Count < ProcedurePageSize || all.Count >= chunk.Total) break;
                }
                var locations = all.Count == 0
                    ? new Dictionary<int, int>()
                    : await _repository.GetInvoiceLocationsAsync(all.Select(i => i.InvoiceId).Distinct().ToList());
                var mine = all.Where(i => locations.TryGetValue(i.InvoiceId, out var loc) && scope.Allows(loc)).ToList();
                return CustomInvoiceResult.Ok(new CustomInvoiceListPage
                {
                    Items = mine.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                    Total = mine.Count
                });
            });
        }

        // ---- create ----------------------------------------------------------------------------------------------

        public async Task<CustomInvoiceResult> CreateAsync(CustomInvoiceCreateRequest? request, int createdById, CustomInvoiceScope scope)
        {
            var (invoice, error) = Validate(request);
            if (invoice == null) return CustomInvoiceResult.BadRequest(error!);
            invoice.CreatedById = createdById;

            return await Run(async () =>
            {
                // An admin writes invoices only for bookings / locations of their own.
                if (!scope.IsAll)
                {
                    if (invoice.LocationId != null && !scope.Allows(invoice.LocationId))
                        return CustomInvoiceResult.Forbidden(NotYourLocationMessage);
                    if (invoice.BookingId is { } bookingId)
                    {
                        var bookingLocations = await _records.GetLocationIdsAsync(RecordKind.Booking, bookingId.ToString(CultureInfo.InvariantCulture));
                        if (bookingLocations is { Count: > 0 } && !scope.AllowsAny(bookingLocations))
                            return CustomInvoiceResult.Forbidden(NotYourBookingMessage);
                        if (bookingLocations is { Count: 0 } && invoice.LocationId == null)
                            return CustomInvoiceResult.Forbidden(NotYourBookingMessage); // booking without a location
                        // No such booking: the procedure answers "The booking was not found."
                    }
                }

                var created = await _repository.InsertAsync(invoice);
                if (created == null || created.InvoiceId <= 0)
                    throw new InvalidOperationException("WN_CustomInvoice_Insert returned no invoice row.");

                var message = string.IsNullOrWhiteSpace(created.ReplacedInvoiceNumber)
                    ? $"Custom invoice {created.InvoiceNumber} created."
                    : $"Custom invoice {created.InvoiceNumber} created; invoice {created.ReplacedInvoiceNumber} was replaced.";
                return CustomInvoiceResult.Ok(created, message);
            });
        }

        /// <summary>
        /// Checks a create request and turns it into the procedure's values, or returns the 400 message:
        /// customer; location when there is no booking; booking when replacing; issue / due dates (due on or after issue);
        /// period both dates or neither, from &lt;= to; reason (required, max 500); amounts 0 or more, total above 0 and
        /// equal to rent + service charge + tax + deposit (2 decimals); 2-5 voucher lines, each an account with exactly one
        /// of debit / credit above 0, debits = credits (2 decimals); the customer ledger debit above 0 and equal to the
        /// total unless <c>overrideTotalMismatch</c> ("Post anyway").
        /// </summary>
        public static (CustomInvoiceInsert? Invoice, string? Error) Validate(CustomInvoiceCreateRequest? r)
        {
            if (r == null) return Fail("Invalid request body.");
            if (r.UserId is not > 0) return Fail("Choose the customer.");

            int? bookingId = r.BookingId is > 0 ? r.BookingId : null;
            int? locationId = r.LocationId is > 0 ? r.LocationId : null;
            int? spaceId = r.SpaceId is > 0 ? r.SpaceId : null;
            int? replaceInvoiceId = r.ReplaceInvoiceId is > 0 ? r.ReplaceInvoiceId : null;
            if (bookingId == null && locationId == null)
                return Fail("Choose the location (needed when the invoice is not for a booking), then optionally a space.");
            if (replaceInvoiceId != null && bookingId == null)
                return Fail("Choose the booking of the invoice to replace.");

            if (r.IssuedOn is not { } issuedOn) return Fail("Enter the issue date.");
            if (r.DueOn is not { } dueOn) return Fail("Enter the due date.");
            if (dueOn.Date < issuedOn.Date) return Fail("The due date cannot be before the issue date.");
            if (r.PeriodFrom.HasValue != r.PeriodTo.HasValue) return Fail("Enter both period dates, or leave both empty.");
            if (r.PeriodFrom?.Date > r.PeriodTo?.Date) return Fail("The period start date cannot be after the period end date.");

            var reason = r.Reason?.Trim();
            if (string.IsNullOrEmpty(reason)) return Fail("Enter the reason for this custom invoice.");
            if (reason.Length > MaxTextLength) return Fail($"The reason can be at most {MaxTextLength} characters.");

            var rent = r.RentAmount ?? 0m;
            var service = r.ServiceCharge ?? 0m;
            var tax = r.TaxAmount ?? 0m;
            var deposit = r.SecurityDeposit ?? 0m;
            if (r.GrandTotal is not { } grandTotal) return Fail("Enter the invoice total.");
            foreach (var (label, value) in new[]
                     {
                         ("The rent", rent), ("The service charge", service), ("The tax", tax),
                         ("The security deposit", deposit), ("The invoice total", grandTotal)
                     })
            {
                if (value < 0) return Fail($"{label} cannot be negative.");
                if (value > MaxAmount) return Fail($"{label} is too large.");
            }
            if (r.TaxPercent is < 0 or > 100) return Fail("The tax percent must be between 0 and 100.");
            if (grandTotal <= 0) return Fail("The invoice total must be more than zero.");
            var parts = rent + service + tax + deposit;
            if (R2(parts) != R2(grandTotal))
                return Fail($"The invoice total ({Money(grandTotal)}) must equal rent + service charge + tax + security deposit ({Money(parts)}).");

            if (r.Lines == null || r.Lines.Count < MinLines || r.Lines.Count > MaxLines)
                return Fail($"The voucher needs {MinLines} to {MaxLines} lines.");
            var lines = new List<CustomInvoiceInsertLine>();
            for (int i = 0; i < r.Lines.Count; i++)
            {
                var line = r.Lines[i];
                var n = i + 1;
                if (line == null) return Fail($"Line {n} is empty.");
                if (line.AccountId is not > 0) return Fail($"Line {n}: choose an account.");
                var debit = line.Debit ?? 0m;
                var credit = line.Credit ?? 0m;
                if (debit < 0 || credit < 0) return Fail($"Line {n}: amounts cannot be negative.");
                if (debit > MaxAmount || credit > MaxAmount) return Fail($"Line {n}: the amount is too large.");
                if (debit > 0 && credit > 0) return Fail($"Line {n}: enter a debit or a credit, not both.");
                if (debit == 0 && credit == 0) return Fail($"Line {n}: enter a debit or a credit amount.");
                var narration = Clean(line.Narration);
                if (narration?.Length > MaxTextLength) return Fail($"Line {n}: the narration can be at most {MaxTextLength} characters.");
                lines.Add(new CustomInvoiceInsertLine(n, line.AccountId.Value, debit, credit, narration));
            }
            var debits = lines.Sum(l => l.Debit);
            var credits = lines.Sum(l => l.Credit);
            if (R2(debits) != R2(credits))
                return Fail($"The voucher does not balance: debits {Money(debits)}, credits {Money(credits)}.");

            var voucherDescription = Clean(r.Voucher?.Description) ?? reason;
            if (voucherDescription.Length > MaxTextLength)
                return Fail($"The voucher description can be at most {MaxTextLength} characters.");

            if (r.CustomerLedger?.Debit is not { } ledgerDebit) return Fail("Enter the customer ledger debit.");
            if (ledgerDebit <= 0) return Fail("The customer ledger debit must be more than zero.");
            if (ledgerDebit > MaxAmount) return Fail("The customer ledger debit is too large.");
            var ledgerNarration = Clean(r.CustomerLedger.Narration);
            if (ledgerNarration?.Length > MaxTextLength)
                return Fail($"The customer ledger narration can be at most {MaxTextLength} characters.");
            if (R2(ledgerDebit) != R2(grandTotal) && !r.OverrideTotalMismatch)
                return Fail(LedgerMismatchMessage(ledgerDebit, grandTotal));

            return (new CustomInvoiceInsert
            {
                UserId = r.UserId.Value,
                BookingId = bookingId,
                LocationId = locationId,
                SpaceId = spaceId,
                IssuedOn = issuedOn.Date,
                DueOn = dueOn.Date,
                PeriodFrom = r.PeriodFrom?.Date,
                PeriodTo = r.PeriodTo?.Date,
                RoomRentExclTax = rent,
                ServiceCharges = service,
                TaxPercent = r.TaxPercent,
                TaxOnServiceCharges = tax,
                SecurityDepositAmount = deposit,
                GrandTotal = grandTotal,
                CustomReason = reason,
                VoucherDate = (r.Voucher?.Date ?? issuedOn).Date,
                VoucherDescription = voucherDescription,
                CustomerLedgerDebit = ledgerDebit,
                CustomerLedgerNarration = ledgerNarration,
                ReplaceInvoiceId = replaceInvoiceId,
                Lines = lines
            }, null);
        }

        /// <summary>400 when the customer ledger debit is not the invoice total and "Post anyway" was not ticked.</summary>
        public static string LedgerMismatchMessage(decimal ledgerDebit, decimal grandTotal) =>
            $"The customer ledger debit ({Money(ledgerDebit)}) differs from the invoice total ({Money(grandTotal)}). Tick 'Post anyway' to continue.";

        // ---- access (super admin) --------------------------------------------------------------------------------

        public Task<CustomInvoiceResult> ListAccessAsync() =>
            Run(async () => CustomInvoiceResult.Ok(await _repository.ListAdminsAsync(CustomInvoiceAccessService.Permission)));

        public async Task<CustomInvoiceResult> SetAccessAsync(int userId, CustomInvoiceAccessRequest? request, int grantedById)
        {
            if (userId <= 0) return CustomInvoiceResult.BadRequest("Choose the admin.");
            if (request?.HasAccess is not bool grant)
                return CustomInvoiceResult.BadRequest("Send hasAccess: true to give access or false to take it away.");

            try
            {
                return await Run(async () =>
                {
                    var item = await _repository.SetPermissionAsync(userId, CustomInvoiceAccessService.Permission, grant, grantedById);
                    if (item == null) return CustomInvoiceResult.NotFound("Admin not found.");
                    var who = string.IsNullOrWhiteSpace(item.Name) ? "The admin" : item.Name;
                    return CustomInvoiceResult.Ok(item, grant ? $"{who} can now use custom invoices." : $"{who} can no longer use custom invoices.");
                });
            }
            finally
            {
                _access.Forget(userId); // the next request of that admin reads the permission again
            }
        }

        // ---- helpers ---------------------------------------------------------------------------------------------

        /// <summary>True when the booking is at one of the scope's locations (or there is no such booking: the procedure answers).</summary>
        private async Task<bool> BookingAllowedAsync(int bookingId, CustomInvoiceScope scope)
        {
            if (scope.IsAll) return true;
            var locations = await _records.GetLocationIdsAsync(RecordKind.Booking, bookingId.ToString(CultureInfo.InvariantCulture));
            return locations == null || scope.AllowsAny(locations);
        }

        /// <summary>CI_01 missing -> 503; a procedure rule (THROW 51000) -> 400 with its message. Anything else is not caught.</summary>
        private static async Task<CustomInvoiceResult> Run(Func<Task<CustomInvoiceResult>> call)
        {
            try
            {
                return await call();
            }
            catch (CustomInvoiceDatabaseMissingException)
            {
                return CustomInvoiceResult.DatabaseUpdateNeeded();
            }
            catch (CustomInvoiceRuleException ex)
            {
                return CustomInvoiceResult.BadRequest(ex.Message);
            }
        }

        private static (CustomInvoiceInsert? Invoice, string? Error) Fail(string message) => (null, message);

        private static decimal R2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        /// <summary>12345.5 -> "12,345.50".</summary>
        public static string Money(decimal value) => R2(value).ToString("N2", CultureInfo.InvariantCulture);

        private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

        private static string? CleanSearch(string? search)
        {
            var s = Clean(search);
            return s != null && s.Length > MaxSearchLength ? s[..MaxSearchLength] : s;
        }
    }
}
