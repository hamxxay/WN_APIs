using System.Globalization;
using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Discounts section of the CEO dashboard (api/ceo/discounts, DD_01): the discount given on the rooms sold monthly.
    /// The bookings and their prices are the Rooms section's (dbo.WN_Ceo_Discounts reads them as dbo.WN_Ceo_RoomReport
    /// with RM_01, and every booking is priced with <see cref="CeoDashboardService.PriceSale"/>), so a booking the Rooms
    /// section shows "under" is the one discounted here, by the same amount:
    ///   discount a month = below the expected price (only "under": more than 0.5% below; 24/7 against the list price x
    ///   seats, Morning / Evening against the list less the location's shift discount cap, so a shift booking at the
    ///   shift price is no discount) + the booking's fixed PKR discount a month (<see cref="FixedMonthly"/>, which the
    ///   price comparison does not see);
    ///   % = discount / the expected rent (the rent before the fixed discount on a room without a list price);
    ///   seller = who made the booking's converted quotation (as the Team section's revenue), else who created the
    ///   booking; a customer who booked it themselves is no seller;
    ///   invoices = WN_Invoices.DiscountTotal of the invoices issued in the period that are not void (the invoice total
    ///   is net of it), shown on its own: it is the same booking discounts as billed.
    /// Same period rules and parameters as the other CEO endpoints (<see cref="DashboardPeriod"/>). Before DD_01 the
    /// dashboard part answers available = false with the message, the records 503.
    /// </summary>
    public class CeoDiscountsService : ICeoDiscountsService
    {
        public const int BiggestCount = 10;
        public const int MaxRows = 200;
        public const string NoSeller = "No salesperson recorded";
        public const string NoLocation = "No location";

        private static readonly CultureInfo En = CultureInfo.InvariantCulture;

        private readonly ICeoDiscountsRepository _repo;
        private readonly IOrderStatusService _orderStatus;
        private readonly IBusinessClock _clock;

        public CeoDiscountsService(ICeoDiscountsRepository repo, IOrderStatusService orderStatus, IBusinessClock clock)
        {
            _repo = repo;
            _orderStatus = orderStatus;
            _clock = clock;
        }

        // ------------------------------------------------------------------ dashboard

        public async Task<ApiResponse> GetDiscountsAsync(string? period, int? locationId, string? from = null, string? to = null)
        {
            if (!DashboardPeriod.TryParse(period, from, to, _clock.Today, out var p, out var periodError)) return ApiResponse.Fail(periodError!);
            var now = _clock.Now;
            var dto = new CeoDiscountsDto
            {
                Period = p.Key,
                PeriodLabel = p.Label(now),
                GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", En),
                From = p.FromText,
                To = p.ToText
            };
            Loaded data;
            try
            {
                data = await LoadAsync(p, Location(locationId));
            }
            catch (CeoDiscountsUpdateMissingException)
            {
                dto.Available = false;
                dto.Message = CeoDiscountsUpdateMissingException.UserMessage;
                return ApiResponse.Ok(dto);
            }

            dto.Summary = Summarize(data.Bookings, data.Invoices);
            dto.ByLocation = ByLocation(data.Bookings, data.Invoices);
            dto.BySeller = BySeller(data.Bookings);
            dto.ByShift = ByShift(data.Bookings);
            dto.Biggest = Biggest(data.Bookings.Where(b => b.DiscountMonthly > 0)).Take(BiggestCount).ToList();
            return ApiResponse.Ok(dto);
        }

        // ------------------------------------------------------------------ records

        public async Task<ApiResponse> GetRecordsAsync(string kind, CeoDiscountRecordsQuery q)
        {
            kind = (kind ?? "").Trim().ToLowerInvariant();
            if (kind is not ("bookings" or "invoices")) return ApiResponse.Fail($"Unknown records list '{kind}'. Use bookings or invoices.");
            var rangeFrom = string.IsNullOrWhiteSpace(q.PeriodFrom) ? q.From : q.PeriodFrom;
            var rangeTo = string.IsNullOrWhiteSpace(q.PeriodTo) ? q.To : q.PeriodTo;
            if (!DashboardPeriod.TryParse(q.Period, rangeFrom, rangeTo, _clock.Today, out var p, out var periodError))
                return ApiResponse.Fail(periodError!);
            if (!TryKey(q.LocationId, out var locationId, out var noLocation))
                return ApiResponse.Fail("locationId must be a location id, or none for records not tied to a location.");
            if (!TryKey(q.SellerId, out var sellerId, out var noSeller))
                return ApiResponse.Fail("sellerId must be a user id, or none for the bookings with no salesperson recorded.");
            string? shift = null;
            if (!string.IsNullOrWhiteSpace(q.Shift))
            {
                shift = ShiftOf(q.Shift);
                if (shift == null) return ApiResponse.Fail("shift must be 24/7, Morning or Evening.");
            }

            // "none" needs every location's rows (the rows without one are only in the all-locations answer).
            var data = await LoadAsync(p, noLocation ? null : locationId);
            var periodWords = p.IsCustom ? p.BetweenWords() : $"in {p.Label(_clock.Now)}";

            if (kind == "invoices")
            {
                var inv = data.Invoices.Where(i => noLocation ? i.LocationId == null : locationId == null || i.LocationId == locationId).ToList();
                var parts = new List<string>();
                if (noLocation) parts.Add("no location");
                else if (locationId != null) parts.Add(LocationName(locationId.Value, data) ?? $"location #{locationId}");
                parts.Add($"issued {periodWords}");
                var subTotal = inv.Sum(i => i.SubTotal);
                var discount = inv.Sum(i => i.Discount);
                return ApiResponse.Ok(new CeoDiscountRecordsDto<CeoDiscountInvoiceDto>
                {
                    Title = $"Invoices with a discount: {string.Join(", ", parts)}",
                    Total = inv.Count,
                    DiscountTotal = discount,
                    BaseTotal = subTotal,
                    DiscountPct = Pct(discount, subTotal),
                    Rows = inv.Take(MaxRows).ToList()
                });
            }

            var all = q.All == true;
            var rows = data.Bookings
                .Where(b => all || b.DiscountMonthly > 0)
                .Where(b => noLocation ? b.LocationId == null : locationId == null || b.LocationId == locationId)
                .Where(b => noSeller ? b.SellerId == null : sellerId == null || b.SellerId == sellerId)
                .Where(b => shift == null || b.Shift == shift)
                .Where(b => q.NewOnly != true || b.StartedInPeriod)
                .ToList();
            var sorted = Biggest(rows).ToList();
            var bp = new List<string>();
            if (noLocation) bp.Add("no location");
            else if (locationId != null) bp.Add(LocationName(locationId.Value, data) ?? $"location #{locationId}");
            if (noSeller) bp.Add(NoSeller.ToLowerInvariant());
            else if (sellerId != null) bp.Add($"sold by {data.Bookings.FirstOrDefault(b => b.SellerId == sellerId)?.Seller ?? $"user #{sellerId}"}");
            if (shift != null) bp.Add(shift);
            if (q.NewOnly == true) bp.Add("started " + periodWords);
            else bp.Add("running " + periodWords);
            var disc = sorted.Sum(b => b.DiscountMonthly);
            var baseM = sorted.Sum(b => b.BaseMonthly);
            return ApiResponse.Ok(new CeoDiscountRecordsDto<CeoDiscountBookingDto>
            {
                Title = $"{(all ? "Bookings" : "Discounted bookings")}: {string.Join(", ", bp)}",
                Total = sorted.Count,
                DiscountTotal = disc,
                BaseTotal = baseM,
                DiscountPct = Pct(disc, baseM),
                Rows = sorted.Take(MaxRows).ToList()
            });
        }

        // ------------------------------------------------------------------ loading

        private sealed record Loaded(List<CeoDiscountBookingDto> Bookings, List<CeoDiscountInvoiceDto> Invoices);

        private async Task<Loaded> LoadAsync(DashboardPeriod p, int? locationId)
        {
            var st = await _orderStatus.GetInvoiceStatusesAsync();
            // Same void set as the CEO dashboard: Cancelled + legacy 5.
            var voidIds = st.Cancelled.Append(5).Distinct().ToList();
            var paid = st.Paid.ToHashSet();
            var s = await _repo.GetDiscountsAsync(locationId, p.Months, _clock.Now, voidIds, p.From, p.To);
            var (start, end) = PeriodWindow(p, _clock.Today);
            var bookings = Set(s, 0).Select(r => PriceBooking(r, start, end)).ToList();
            var invoices = Set(s, 1).Select(r => InvoiceOf(r, paid)).ToList();
            return new Loaded(bookings, invoices);
        }

        /// <summary>First day of the period and the first day after it (calendar period: the whole month / quarter / year).</summary>
        public static (DateTime Start, DateTime End) PeriodWindow(DashboardPeriod p, DateTime today)
        {
            if (p.IsCustom) return (p.From!.Value.Date, p.To!.Value.Date.AddDays(1));
            var start = p.Key switch
            {
                "year" => new DateTime(today.Year, 1, 1),
                "quarter" => new DateTime(today.Year, (today.Month - 1) / 3 * 3 + 1, 1),
                _ => new DateTime(today.Year, today.Month, 1)
            };
            return (start, start.AddMonths(p.Months));
        }

        // ------------------------------------------------------------------ money rules

        /// <summary>
        /// A booking's fixed PKR discount a month: only for a discount given as an amount (DiscountType Amount / Fixed,
        /// <see cref="SecurityDepositRules.IsFixedDiscount"/>) on a booking without a percentage (a percentage, even one
        /// worked out from the amount, is already in the rent the Rooms section compares). Per month = DiscountValue (the
        /// amount as entered), else DiscountAmount, as the invoices bill it (x billing months); at most the rent; 0 when
        /// there is no rent.
        /// </summary>
        public static decimal FixedMonthly(string? discountType, decimal discountPct, decimal discountAmount, decimal discountValue, decimal rentMonthly)
        {
            if (discountPct > 0m || !SecurityDepositRules.IsFixedDiscount(discountType)) return 0m;
            var perMonth = discountValue > 0m ? discountValue : discountAmount;
            if (perMonth <= 0m || rentMonthly <= 0m) return 0m;
            return Money(Math.Min(perMonth, rentMonthly));
        }

        /// <summary>
        /// One booking row of dbo.WN_Ceo_Discounts priced as the Rooms section prices it
        /// (<see cref="CeoDashboardService.PriceSale"/>), plus its fixed PKR discount: below the price (under only) +
        /// fixed = the discount a month, as a share of the expected rent (else of the rent before the fixed discount).
        /// A customer as the seller (they booked it themselves) is no seller.
        /// </summary>
        public static CeoDiscountBookingDto PriceBooking(IDictionary<string, object?> r, DateTime? periodStart = null, DateTime? periodEnd = null)
        {
            var roomSeats = Int(r, "RoomSeats");
            decimal? roomList = Val(r, "RoomListSeatPrice") is { } lp && Convert.ToDecimal(lp, En) > 0 ? Math.Round(Convert.ToDecimal(lp, En), 2) : null;
            var pct = DecRaw(r, "DiscountPct");
            // The Rooms section's sold a month: the procedure's figure, else the rent less the percentage.
            var rent = Val(r, "SoldMonthly") is { } sm ? Convert.ToDecimal(sm, En) : DecRaw(r, "MonthlyRent") * (1 - pct / 100m);
            var sale = CeoDashboardService.PriceSale(new CeoRoomSaleDto
            {
                BookingId = Int(r, "BookingId"),
                Customer = Str(r, "Customer"),
                Shift = Str(r, "Shift") ?? Str(r, "ShiftRaw") ?? "",
                SeatsSold = Int(r, "SeatsSold"),
                StartOn = Day(r, "StartOn"),
                EndOn = Day(r, "EndOn"),
                ListSeatPriceAtSale = Val(r, "ListSeatPriceAtSale") is { } ls ? Convert.ToDecimal(ls, En) : null,
                ShiftDiscountCapPct = DecRaw(r, "ShiftDiscountCapPct"),
                SoldMonthly = rent
            }, roomSeats, roomList);

            var fixedMonthly = FixedMonthly(Str(r, "DiscountType"), pct, DecRaw(r, "DiscountAmount"), DecRaw(r, "DiscountValue"), sale.SoldMonthly);
            var below = sale.Pricing == "under" ? Money(-(sale.VarianceMonthly ?? 0m)) : 0m;
            var discount = below + fixedMonthly;
            var baseMonthly = sale.ExpectedMonthly ?? sale.SoldMonthly;

            int? sellerId = Val(r, "SellerId") is { } sid ? Convert.ToInt32(sid, En) : null;
            var sellerRoleId = Val(r, "SellerRoleId") is { } rid ? Convert.ToInt32(rid, En) : (int?)null;
            if (sellerRoleId == Roles.GeneralId) sellerId = null;   // the customer booked it themselves
            var start = Val(r, "StartOn") is DateTime sd ? sd.Date : (DateTime?)null;

            return new CeoDiscountBookingDto
            {
                BookingId = sale.BookingId,
                Customer = sale.Customer,
                SpaceId = Int(r, "SpaceId"),
                Room = Str(r, "Room"),
                Code = Str(r, "Code"),
                LocationId = Val(r, "LocationId") is { } l ? Convert.ToInt32(l, En) : null,
                Location = Str(r, "Location"),
                SpaceType = Str(r, "SpaceType"),
                Shift = sale.Shift,
                Seats = sale.SeatsSold,
                StartOn = sale.StartOn,
                EndOn = sale.EndOn,
                StartedInPeriod = start != null && periodStart != null && periodEnd != null && start >= periodStart && start < periodEnd,
                ListSeatPrice = sale.ListSeatPriceAtSale,
                ListMonthly = sale.ListSeatPriceAtSale is { } list ? Money(list * sale.SeatsSold) : null,
                ShiftDiscountCapPct = sale.ShiftDiscountCapPct,
                ExpectedMonthly = sale.ExpectedMonthly,
                RentMonthly = sale.SoldMonthly,
                SoldMonthly = Money(sale.SoldMonthly - fixedMonthly),
                Pricing = sale.Pricing,
                BelowPriceMonthly = below,
                FixedMonthly = fixedMonthly,
                DiscountMonthly = discount,
                DiscountPct = Pct(discount, baseMonthly),
                BaseMonthly = baseMonthly,
                BookingDiscountPct = Math.Round(pct, 2),
                SellerId = sellerId,
                Seller = sellerId == null ? null : Str(r, "Seller") ?? $"User #{sellerId}",
                SellerRole = sellerId == null ? null : Str(r, "SellerRole")
            };
        }

        private static CeoDiscountInvoiceDto InvoiceOf(IDictionary<string, object?> r, IReadOnlySet<int> paid) => new()
        {
            InvoiceId = Int(r, "InvoiceId"),
            InvoiceNumber = Str(r, "InvoiceNumber"),
            BookingId = Val(r, "BookingId") is { } b ? Convert.ToInt32(b, En) : null,
            Customer = Str(r, "Customer"),
            LocationId = Val(r, "LocationId") is { } l ? Convert.ToInt32(l, En) : null,
            Location = Str(r, "Location"),
            Room = Str(r, "Room"),
            IssuedOn = Day(r, "IssuedOn"),
            SubTotal = Money(DecRaw(r, "SubTotal")),
            Discount = Money(DecRaw(r, "DiscountTotal")),
            Total = Money(DecRaw(r, "GrandTotal")),
            Status = Str(r, "Status"),
            Paid = Val(r, "StatusId") is { } s && paid.Contains(Convert.ToInt32(s, En))
        };

        // ------------------------------------------------------------------ totals

        public static CeoDiscountSummaryDto Summarize(IReadOnlyCollection<CeoDiscountBookingDto> bookings, IReadOnlyCollection<CeoDiscountInvoiceDto> invoices)
        {
            var discounted = bookings.Where(b => b.DiscountMonthly > 0).ToList();
            var fresh = bookings.Where(b => b.StartedInPeriod).ToList();
            var total = bookings.Sum(b => b.DiscountMonthly);
            var baseAll = bookings.Sum(b => b.BaseMonthly);
            return new CeoDiscountSummaryDto
            {
                Bookings = bookings.Count,
                Priced = bookings.Count(b => b.Pricing != null),
                Unpriced = bookings.Count(b => b.Pricing == null),
                Discounted = discounted.Count,
                ExpectedMonthly = bookings.Sum(b => b.ExpectedMonthly ?? 0m),
                BaseMonthly = baseAll,
                SoldMonthly = bookings.Sum(b => b.SoldMonthly),
                DiscountMonthly = total,
                DiscountPct = Pct(total, baseAll),
                AverageDiscountPct = Pct(total, discounted.Sum(b => b.BaseMonthly)),
                BelowPriceMonthly = bookings.Sum(b => b.BelowPriceMonthly),
                BelowPriceBookings = bookings.Count(b => b.BelowPriceMonthly > 0),
                FixedMonthly = bookings.Sum(b => b.FixedMonthly),
                FixedBookings = bookings.Count(b => b.FixedMonthly > 0),
                NewBookings = fresh.Count,
                NewDiscounted = fresh.Count(b => b.DiscountMonthly > 0),
                NewDiscountMonthly = fresh.Sum(b => b.DiscountMonthly),
                InvoiceDiscount = invoices.Sum(i => i.Discount),
                InvoicesDiscounted = invoices.Count,
                InvoiceDiscountPaid = invoices.Where(i => i.Paid).Sum(i => i.Discount)
            };
        }

        private static CeoDiscountGroupDto Group(string? key, string name, string? role, IReadOnlyCollection<CeoDiscountBookingDto> rows)
        {
            var discount = rows.Sum(b => b.DiscountMonthly);
            var baseMonthly = rows.Sum(b => b.BaseMonthly);
            return new CeoDiscountGroupDto
            {
                Key = key, Name = name, Role = role,
                Bookings = rows.Count,
                Discounted = rows.Count(b => b.DiscountMonthly > 0),
                BaseMonthly = baseMonthly,
                DiscountMonthly = discount,
                DiscountPct = Pct(discount, baseMonthly),
                LargestMonthly = rows.Count == 0 ? 0m : rows.Max(b => b.DiscountMonthly)
            };
        }

        /// <summary>Every location with bookings or discounted invoices; biggest discount a month first, then invoices, name.</summary>
        public static List<CeoDiscountGroupDto> ByLocation(IReadOnlyCollection<CeoDiscountBookingDto> bookings, IReadOnlyCollection<CeoDiscountInvoiceDto> invoices)
        {
            var ids = bookings.Select(b => b.LocationId).Concat(invoices.Select(i => i.LocationId)).Distinct().ToList();
            return ids.Select(id =>
                {
                    var rows = bookings.Where(b => b.LocationId == id).ToList();
                    var inv = invoices.Where(i => i.LocationId == id).ToList();
                    var name = rows.Select(b => b.Location).Concat(inv.Select(i => i.Location)).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                               ?? (id == null ? NoLocation : $"Location {id}");
                    var g = Group(id?.ToString(En), name, null, rows);
                    g.InvoiceDiscount = inv.Sum(i => i.Discount);
                    g.InvoicesDiscounted = inv.Count;
                    return g;
                })
                .OrderBy(g => g.Key == null)
                .ThenByDescending(g => g.DiscountMonthly).ThenByDescending(g => g.InvoiceDiscount)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Every seller of a booking in the period (and "no salesperson recorded"); biggest discount a month first.</summary>
        public static List<CeoDiscountGroupDto> BySeller(IReadOnlyCollection<CeoDiscountBookingDto> bookings) =>
            bookings.GroupBy(b => b.SellerId)
                .Select(g =>
                {
                    var first = g.First();
                    return Group(g.Key?.ToString(En), g.Key == null ? NoSeller : first.Seller ?? $"User #{g.Key}", g.Key == null ? null : first.SellerRole, g.ToList());
                })
                .OrderByDescending(g => g.DiscountMonthly).ThenBy(g => g.Key == null).ThenByDescending(g => g.Discounted)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>24/7, Morning, Evening (always all three).</summary>
        public static List<CeoDiscountGroupDto> ByShift(IReadOnlyCollection<CeoDiscountBookingDto> bookings) =>
            CeoDashboardService.RoomShifts.Select(s => Group(s, s, null, bookings.Where(b => b.Shift == s).ToList())).ToList();

        /// <summary>Biggest discount a month first, then the biggest %, then the booking.</summary>
        public static IEnumerable<CeoDiscountBookingDto> Biggest(IEnumerable<CeoDiscountBookingDto> bookings) =>
            bookings.OrderByDescending(b => b.DiscountMonthly).ThenByDescending(b => b.DiscountPct).ThenBy(b => b.BookingId);

        // ------------------------------------------------------------------ helpers

        /// <summary>24/7 | Morning | Evening for any common spelling; null when unknown.</summary>
        public static string? ShiftOf(string? value)
        {
            var v = (value ?? "").Trim().ToLowerInvariant();
            if (v is "24/7" or "24_7" or "247" or "24-7" or "full") return "24/7";
            if (v.StartsWith("morning")) return "Morning";
            if (v.StartsWith("evening") || v.StartsWith("night")) return "Evening";
            return null;
        }

        private static string? LocationName(int id, Loaded data) =>
            data.Bookings.FirstOrDefault(b => b.LocationId == id)?.Location ?? data.Invoices.FirstOrDefault(i => i.LocationId == id)?.Location;

        private static int? Location(int? locationId) => locationId is > 0 ? locationId : null;

        /// <summary>An id, "none", or nothing / 0 (all).</summary>
        private static bool TryKey(string? value, out int? id, out bool none)
        {
            id = null;
            none = false;
            var v = (value ?? "").Trim();
            if (v.Length == 0) return true;
            if (v.Equals("none", StringComparison.OrdinalIgnoreCase)) { none = true; return true; }
            if (!int.TryParse(v, NumberStyles.Integer, En, out var n)) return false;
            id = n > 0 ? n : null;
            return true;
        }

        /// <summary>PKR, 2 decimals (half away from zero).</summary>
        private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
        private static decimal Pct(decimal part, decimal whole) => whole > 0 ? Math.Round(part * 100m / whole, 1, MidpointRounding.AwayFromZero) : 0m;

        private static List<IDictionary<string, object?>> Set(List<List<IDictionary<string, object?>>> s, int i) => i < s.Count ? s[i] : new();
        private static object? Val(IDictionary<string, object?>? r, string key) => r != null && r.TryGetValue(key, out var v) ? v : null;
        private static int Int(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToInt32(v, En) : 0;
        private static decimal DecRaw(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToDecimal(v, En) : 0m;
        private static string? Str(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToString(v, En) : null;
        private static string? Day(IDictionary<string, object?>? r, string key) => Val(r, key) is DateTime d ? d.ToString("yyyy-MM-dd", En) : null;
    }
}
