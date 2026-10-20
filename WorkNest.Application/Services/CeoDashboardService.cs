using System.Globalization;
using WorkNest.Application.DTOs.Contact;
using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// CEO dashboard (api/ceo/*): every location, read-only. The numbers come from the dbo.WN_Ceo_* procedures, which
    /// use the same rules as the admin overview (api/dashboard/overview) and the super admin role dashboard
    /// (api/dashboard/role), with the same invoice statuses (OrderStatusService) and the same business clock, so they
    /// match the rest of the system. Level 1 = dashboard, level 2 = breakdown of one chart element, level 3 = records.
    /// Tour inquiries follow the location filter by their branch (WN_Contacts.LocationId) once QA_07 has run, as on the
    /// Sales Manager dashboard: one location = only its inquiries, all locations = every inquiry. Before QA_07 they count
    /// every location's inquiries and say so (inquiriesAllLocations, "all locations"). Rooms (api/ceo/rooms, CEO_02):
    /// room occupancy and the price each room was sold at against its list / shift price.
    /// Period: month | quarter | year (to date) or, since CS_01, custom: a day range (from / to on the dashboard and the
    /// rooms; periodFrom / periodTo on the drill-down, where from / to narrow a list), compared with the same number of
    /// days just before it (<see cref="DashboardPeriod"/>). Only a custom range reaches the procedures as @From / @To.
    /// Team (19 Oct 2026): only the sales executives, each with their revenue (<see cref="TeamOf"/>).
    /// Invoices by billing period (CP_01, 20 Oct 2026): the procedures place an invoice in the month its billing period
    /// starts (not the month it was issued); unpaid invoices for a period not started yet (and not past due) are "billed
    /// in advance", apart from Outstanding (billedInAdvance; null before CP_01). Records: status advance, periodStart / periodEnd.
    /// Money (CP_02): invoiced leaves out the security deposits (securityDeposits apart); collected = paid so far on the
    /// period's invoices; whole billing months against the same months one period earlier; only invoices with something
    /// left to pay count as outstanding / overdue / billed in advance. Records: balance = GrandTotal - WHT - paid (0 when
    /// paid), status collected, invoiceTotals.
    /// </summary>
    public class CeoDashboardService : ICeoDashboardService
    {
        private const int ExpiringDays = 90;
        private const int MaxRows = 200;

        private static readonly CultureInfo En = CultureInfo.InvariantCulture;

        private static readonly (string Key, string Label)[] Buckets =
        {
            ("not_due", "Not due"), ("1_30", "1-30 days"), ("31_60", "31-60 days"), ("61_90", "61-90 days"), ("90_plus", "90+ days")
        };

        private static readonly (string Type, string Label)[] InquiryTypeLabels =
        {
            (InquiryTypes.Website, "Website"), (InquiryTypes.WhatsApp, "WhatsApp"), (InquiryTypes.WalkIn, "Walk-in"),
            (InquiryTypes.Reference, "Reference"), ("NotSet", "Not set")
        };

        private static readonly string[] Metrics =
            { "revenue", "aging", "occupancy-type", "occupancy-location", "bookings-month", "inquiries-type", "staff" };

        private readonly ICeoDashboardRepository _repo;
        private readonly IOrderStatusService _orderStatus;
        private readonly IBusinessClock _clock;
        private readonly IRoleService? _roles;
        private readonly ISpaceTypeService? _spaceTypes;

        public CeoDashboardService(ICeoDashboardRepository repo, IOrderStatusService orderStatus, IBusinessClock clock,
            IRoleService? roles = null, ISpaceTypeService? spaceTypes = null)
        {
            _repo = repo;
            _orderStatus = orderStatus;
            _clock = clock;
            _roles = roles;
            _spaceTypes = spaceTypes;
        }

        // ------------------------------------------------------------------ level 1: dashboard

        public async Task<ApiResponse> GetDashboardAsync(string? period, int? locationId, string? from = null, string? to = null)
        {
            if (!DashboardPeriod.TryParse(period, from, to, _clock.Today, out var p, out var periodError)) return ApiResponse.Fail(periodError!);
            var now = _clock.Now;
            var s = await _repo.GetDashboardAsync(Location(locationId), p.Months, now, await StatusesAsync(), ExpiringDays, p.From, p.To);

            var money = Set(s, 1).FirstOrDefault();
            var occ = Set(s, 5).FirstOrDefault();
            var sales = Set(s, 9).FirstOrDefault();
            var totalSpaces = Int(occ, "TotalSpaces");
            // [13] all leases expiring (QA_07); before it only the list of the first 50 is known.
            var leaseTotals = Set(s, 13).FirstOrDefault();

            var dto = new CeoDashboardDto
            {
                Period = p.Key,
                PeriodLabel = p.Label(now),
                GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", En),
                From = p.FromText,
                To = p.ToText,
                PreviousFrom = p.PreviousFromText,
                PreviousTo = p.PreviousToText,
                Locations = Set(s, 0).Select(r => new CeoLocationDto { LocationId = Int(r, "LocationId"), Name = Str(r, "Name") ?? "" }).ToList(),
                Money = new CeoMoneyDto
                {
                    Invoiced = Dec(money, "Invoiced"),
                    InvoicedPrev = Dec(money, "InvoicedPrev"),
                    Collected = Dec(money, "Collected"),
                    CollectedPrev = Dec(money, "CollectedPrev"),
                    Outstanding = Dec(money, "Outstanding"),
                    OutstandingCount = Int(money, "OutstandingCount"),
                    Overdue = Dec(money, "Overdue"),
                    OverdueCount = Int(money, "OverdueCount"),
                    // CP_01: unpaid invoices for a billing period not started yet (not in Outstanding); null before CP_01.
                    BilledInAdvance = Has(money, "BilledInAdvance") ? Dec(money, "BilledInAdvance") : null,
                    BilledInAdvanceCount = Has(money, "BilledInAdvanceCount") ? Int(money, "BilledInAdvanceCount") : null,
                    // CP_02: security deposits billed (kept out of Invoiced) and the previous invoice window; null before CP_02.
                    SecurityDeposits = Has(money, "SecurityDeposits") ? Dec(money, "SecurityDeposits") : null,
                    SecurityDepositsPrev = Has(money, "SecurityDepositsPrev") ? Dec(money, "SecurityDepositsPrev") : null,
                    SecurityDepositsCount = Has(money, "SecurityDepositsCount") ? Int(money, "SecurityDepositsCount") : null,
                    InvoicedPrevFrom = Day(money, "InvoicePrevFrom"),
                    InvoicedPrevTo = Day(money, "InvoicePrevTo"),
                    Months = Set(s, 2).Select(r => new CeoMoneyMonthDto
                        { Month = Str(r, "Month") ?? "", Invoiced = Dec(r, "Invoiced"), Collected = Dec(r, "Collected") }).ToList(),
                    Aging = Set(s, 3).Select(r => new CeoAgingBucketDto
                        { Bucket = Str(r, "BucketKey") ?? "", Label = Str(r, "Label") ?? "", Amount = Dec(r, "Amount"), Count = Int(r, "Invoices") }).ToList(),
                    TopCustomers = Set(s, 4).Select(r => new CeoTopCustomerDto
                    {
                        CustomerId = Val(r, "CustomerId") is { } id ? Convert.ToInt32(id) : null,
                        Name = Str(r, "Name") ?? "", Amount = Dec(r, "Amount"), Invoices = Int(r, "Invoices")
                    }).ToList()
                },
                Occupancy = new CeoOccupancyDto
                {
                    TotalSpaces = totalSpaces,
                    OccupiedSpaces = Int(occ, "OccupiedSpaces"),
                    OccupancyPct = Pct(Int(occ, "OccupiedSpaces"), totalSpaces),
                    OccupancyPctPrev = Pct(Int(occ, "OccupiedSpacesPrev"), totalSpaces),
                    ActiveBookings = Int(occ, "ActiveBookings"),
                    NewBookings = Int(occ, "NewBookings"),
                    NewBookingsPrev = Int(occ, "NewBookingsPrev"),
                    Months = Set(s, 2).Select(r => new CeoOccupancyMonthDto
                        { Month = Str(r, "Month") ?? "", OccupancyPct = Pct(Int(r, "Occupied"), totalSpaces), NewBookings = Int(r, "NewBookings") }).ToList(),
                    ByType = Set(s, 6).Select(r => new CeoSpaceTypeOccupancyDto
                        { SpaceTypeId = Int(r, "SpaceTypeId"), Name = Str(r, "Name") ?? "", Total = Int(r, "Total"), Occupied = Int(r, "Occupied") }).ToList(),
                    ByLocation = Set(s, 7).Select(r => new CeoLocationOccupancyDto
                        { LocationId = Int(r, "LocationId"), Name = Str(r, "Name") ?? "", Total = Int(r, "Total"), Occupied = Int(r, "Occupied") }).ToList(),
                    LeasesExpiring = Set(s, 8).Select(r => new CeoLeaseExpiringDto
                    {
                        BookingId = Int(r, "BookingId"), Customer = Str(r, "Customer"), Space = Str(r, "Space"),
                        Location = Str(r, "Location"), EndOn = Day(r, "EndOn"), MonthlyAmount = Dec(r, "MonthlyAmount")
                    }).ToList(),
                    LeasesExpiringCount = leaseTotals != null ? Int(leaseTotals, "ExpiringCount") : null,
                    LeasesExpiringMonthly = leaseTotals != null ? Dec(leaseTotals, "ExpiringMonthly") : null
                }
            };
            dto.Money.CollectionRate = dto.Money.Invoiced > 0 ? Math.Round(dto.Money.Collected * 100m / dto.Money.Invoiced, 1) : 0m;

            var quotations = Int(sales, "Quotations");
            var bookings = Int(sales, "Bookings");
            // Conversion = quotations created in the period that were booked / quotations created in it (SM_04 column);
            // before SM_04 ran: the per-location "converted" counts of the same quotations.
            var quotationsBooked = Has(sales, "QuotationsBooked") ? Int(sales, "QuotationsBooked")
                : Math.Min(Set(s, 11).Sum(r => Int(r, "Converted")), quotations);
            var byType = Set(s, 10).ToDictionary(r => Str(r, "InquiryType") ?? "NotSet", r => Int(r, "Inquiries"), StringComparer.OrdinalIgnoreCase);
            dto.Sales = new CeoSalesDto
            {
                Funnel = new List<CeoFunnelStageDto>
                {
                    new() { Stage = "inquiries", Label = "Tour inquiries", Count = Int(sales, "Inquiries") },
                    new() { Stage = "quotations", Label = "Quotations", Count = quotations },
                    new() { Stage = "accepted", Label = "Accepted", Count = Int(sales, "Accepted") },
                    new() { Stage = "bookings", Label = "Bookings", Count = bookings }
                },
                ConversionRate = Pct(quotationsBooked, quotations),
                QuotationsBooked = quotationsBooked,
                InquiriesByType = InquiryTypeLabels
                    .Select(t => new CeoInquiryTypeCountDto { Type = t.Type, Label = t.Label, Count = byType.GetValueOrDefault(t.Type) })
                    .Concat(byType.Where(kv => InquiryTypeLabels.All(t => !t.Type.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)))
                        .Select(kv => new CeoInquiryTypeCountDto { Type = kv.Key, Label = kv.Key, Count = kv.Value }))
                    .ToList(),
                QuotationValue = Dec(sales, "QuotationValue"),
                BookingValue = Dec(sales, "BookingValue"),
                // A location chosen, but the procedure counted every location's inquiries (QA_07 not run).
                InquiriesAllLocations = Location(locationId) != null && !Flag(sales, "InqByLocation")
            };
            dto.Team = TeamOf(Set(s, 11), Set(s, 12), await SalesExecutiveRoleAsync());
            return ApiResponse.Ok(dto);
        }

        /// <summary>
        /// The Team section (19 Oct 2026): only the sales executives. [12] lists the active admins and sales executives
        /// (of the chosen location, with their quotations on its spaces); everyone but the sales executives (RoleId 16)
        /// is left out here, so the CMS and the app get the same list. Revenue = their converted value (the total of the
        /// quotations they created in the period that were converted). Ranked by revenue, converted, quotations, name.
        /// The per-location rows keep their sales executive count and the location's quotations (no admin count).
        /// </summary>
        public static CeoTeamDto TeamOf(IEnumerable<IDictionary<string, object?>> perLocation, IEnumerable<IDictionary<string, object?>> people,
            string? salesExecutiveRole)
        {
            var list = people
                .Where(r => Int(r, "RoleId") == WorkNest.Common.Constants.Roles.SalesExecutiveId)
                .Select(r =>
                {
                    var revenue = Dec(r, "ConvertedValue");
                    return new CeoTeamPersonDto
                    {
                        UserId = Int(r, "UserId"), Name = Str(r, "Name") ?? "", Role = salesExecutiveRole ?? "",
                        Locations = Str(r, "Locations"), Quotations = Int(r, "Quotations"), Converted = Int(r, "Converted"),
                        Revenue = revenue, ConvertedValue = revenue
                    };
                })
                .OrderByDescending(p => p.Revenue).ThenByDescending(p => p.Converted).ThenByDescending(p => p.Quotations)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.UserId)
                .ToList();
            return new CeoTeamDto
            {
                PerLocation = perLocation.Select(r => new CeoTeamLocationDto
                {
                    LocationId = Int(r, "LocationId"), Name = Str(r, "Name") ?? "", SalesExecutives = Int(r, "SalesExecutives"),
                    Quotations = Int(r, "Quotations"), Converted = Int(r, "Converted")
                }).ToList(),
                People = list,
                SalesExecutives = list.Count,
                Revenue = list.Sum(p => p.Revenue)
            };
        }

        /// <summary>WN_Roles.DisplayName of the sales executive role; null when there is no role list.</summary>
        private async Task<string?> SalesExecutiveRoleAsync() =>
            _roles == null ? null : await _roles.GetDisplayNameAsync(WorkNest.Common.Constants.Roles.SalesExecutiveId);

        // ------------------------------------------------------------------ level 2: breakdown

        public async Task<ApiResponse> GetBreakdownAsync(CeoBreakdownQuery q)
        {
            var metric = (q.Metric ?? "").Trim().ToLowerInvariant();
            if (metric.Length == 0) return ApiResponse.Fail($"metric is required: {string.Join(", ", Metrics)}.");
            if (!Metrics.Contains(metric)) return ApiResponse.Fail($"Unknown metric '{q.Metric}'. Use {string.Join(", ", Metrics)}.");
            if (!DashboardPeriod.TryParse(q.Period, q.PeriodFrom, q.PeriodTo, _clock.Today, out _, out var periodError))
                return ApiResponse.Fail(periodError!);

            DateTime? month = null;
            // revenue (CS_01): the invoices whose billing period starts from / to (a custom period, CP_01) instead of one month.
            DateTime? rangeFrom = null, rangeTo = null;
            if (metric == "revenue" && string.IsNullOrWhiteSpace(q.Month) && (!string.IsNullOrWhiteSpace(q.From) || !string.IsNullOrWhiteSpace(q.To)))
            {
                if (string.IsNullOrWhiteSpace(q.From) || string.IsNullOrWhiteSpace(q.To))
                    return ApiResponse.Fail("from and to (yyyy-MM-dd) are both required for metric revenue without a month.");
                if (!TryDays(q.From, q.To, out rangeFrom, out var end, out _, out var rangeError)) return ApiResponse.Fail(rangeError!);
                rangeTo = end!.Value.AddDays(-1);
            }
            else if (metric is "revenue" or "bookings-month")
            {
                if (string.IsNullOrWhiteSpace(q.Month)) return ApiResponse.Fail($"month (yyyy-MM) is required for metric {metric}.");
                if (!TryMonth(q.Month, out var m)) return ApiResponse.Fail(MonthError);
                month = m;
            }
            string? bucket = null;
            if (metric == "aging")
            {
                bucket = (q.Bucket ?? "").Trim().ToLowerInvariant();
                if (Buckets.All(b => b.Key != bucket)) return ApiResponse.Fail(BucketError);
            }
            if (metric == "occupancy-type" && q.SpaceTypeId is null or < 0)
                return ApiResponse.Fail("spaceTypeId is required for metric occupancy-type.");
            if (metric == "occupancy-location" && q.LocationId is null or <= 0)
                return ApiResponse.Fail("locationId is required for metric occupancy-location.");
            string? type = null;
            if (metric == "inquiries-type")
            {
                type = InquiryType(q.Type);
                if (type == null) return ApiResponse.Fail(TypeError);
            }
            if (metric == "staff" && q.UserId is null or <= 0) return ApiResponse.Fail("userId is required for metric staff.");

            var s = await _repo.GetBreakdownAsync(metric, month, bucket, metric == "occupancy-type" ? q.SpaceTypeId : null,
                Location(q.LocationId), type, metric == "staff" ? q.UserId : null, _clock.Now, await StatusesAsync(), rangeFrom, rangeTo);
            var head = Set(s, 0).FirstOrDefault();
            var subject = Str(head, "Subject");
            var monthRows = metric is "inquiries-type" or "staff";
            var dto = new CeoBreakdownDto
            {
                Rows = Set(s, 1).Select(r =>
                {
                    var key = Str(r, "Key") ?? "";
                    return new CeoBreakdownRowDto
                    {
                        Key = key,
                        Label = monthRows && TryMonth(key, out var m) ? m.ToString("MMM yyyy", En) : Str(r, "Label") ?? key,
                        Value = Dec(r, "Value"),
                        Value2 = Val(r, "Value2") != null ? Dec(r, "Value2") : null,
                        Count = Val(r, "Count") is { } c ? Convert.ToInt32(c) : null
                    };
                }).ToList()
            };
            switch (metric)
            {
                case "revenue":
                    var billed = rangeFrom != null ? DashboardPeriod.RangeLabel(rangeFrom.Value, rangeTo!.Value) : month!.Value.ToString("MMMM yyyy", En);
                    (dto.Title, dto.Subtitle, dto.ValueLabel, dto.Value2Label) =
                        ("Revenue by location", $"Invoices by billing period, {billed}, without security deposits; collected = paid so far", "Invoiced", "Collected");
                    break;
                case "aging":
                    var bucketLabel = Buckets.First(b => b.Key == bucket).Label;
                    (dto.Title, dto.Subtitle, dto.ValueLabel, dto.Value2Label) =
                        ("Outstanding by location", bucket == "not_due" ? "Unpaid invoices not due yet" : $"Unpaid invoices {bucketLabel} past due", "Outstanding", null);
                    break;
                case "occupancy-type":
                    (dto.Title, dto.Subtitle, dto.ValueLabel, dto.Value2Label) =
                        ($"Occupancy: {subject ?? "Other"}", "Occupied and total spaces by location, right now", "Occupied", "Total spaces");
                    break;
                case "occupancy-location":
                    (dto.Title, dto.Subtitle, dto.ValueLabel, dto.Value2Label) =
                        ($"Occupancy: {subject ?? "Location"}", "Occupied and total spaces by space type, right now", "Occupied", "Total spaces");
                    break;
                case "bookings-month":
                    (dto.Title, dto.Subtitle, dto.ValueLabel, dto.Value2Label) =
                        ("New bookings by location", $"Bookings made in {month!.Value.ToString("MMMM yyyy", En)}", "New bookings", null);
                    break;
                case "inquiries-type":
                    (dto.Title, dto.Subtitle, dto.ValueLabel, dto.Value2Label) =
                        ($"Tour inquiries: {InquiryTypeLabels.First(t => t.Type == type).Label}",
                            Location(q.LocationId) != null && !Flag(head, "InqByLocation") ? "Last 6 months, all locations" : "Last 6 months",
                            "Inquiries", null);
                    break;
                default: // staff
                    (dto.Title, dto.Subtitle, dto.ValueLabel, dto.Value2Label) =
                        ($"Quotations: {subject ?? $"User #{q.UserId}"}", "Last 6 months", "Quotations", "Converted");
                    break;
            }
            return ApiResponse.Ok(dto);
        }

        // ------------------------------------------------------------------ level 3: records

        public async Task<ApiResponse> GetRecordsAsync(string kind, CeoRecordsQuery q)
        {
            kind = (kind ?? "").Trim().ToLowerInvariant();
            if (!DashboardPeriod.TryParse(q.Period, q.PeriodFrom, q.PeriodTo, _clock.Today, out _, out var periodError))
                return ApiResponse.Fail(periodError!);
            if (!TryRecordLocation(q.LocationId, out var locationId, out var noLocation))
                return ApiResponse.Fail("locationId must be a location id, or none for records not tied to a location.");

            return kind switch
            {
                "invoices" => await InvoicesAsync(q, locationId, noLocation),
                "spaces" => await SpacesAsync(q, locationId, noLocation),
                "bookings" => await BookingsAsync(q, locationId, noLocation),
                "inquiries" => noLocation
                    ? ApiResponse.Fail("locationId=none is not available for inquiries.")
                    : await InquiriesAsync(q, locationId),
                "quotations" => noLocation
                    ? ApiResponse.Fail("locationId=none is not available for quotations.")
                    : await QuotationsAsync(q, locationId),
                "leases" => await LeasesAsync(q, locationId, noLocation),
                _ => ApiResponse.Fail($"Unknown records list '{kind}'. Use invoices, spaces, bookings, inquiries, quotations or leases.")
            };
        }

        private async Task<ApiResponse> InvoicesAsync(CeoRecordsQuery q, int? locationId, bool noLocation)
        {
            DateTime? month = null;
            if (!string.IsNullOrWhiteSpace(q.Month))
            {
                if (!TryMonth(q.Month, out var m)) return ApiResponse.Fail(MonthError);
                month = m;
            }
            string? bucket = null;
            if (!string.IsNullOrWhiteSpace(q.Bucket))
            {
                bucket = q.Bucket.Trim().ToLowerInvariant();
                if (Buckets.All(b => b.Key != bucket)) return ApiResponse.Fail(BucketError);
            }
            string? status = null;
            if (!string.IsNullOrWhiteSpace(q.Status))
            {
                status = q.Status.Trim().ToLowerInvariant();
                // advance (CP_01): unpaid, for a billing period not started yet (open leaves them out).
                // collected (CP_02): something paid (what Collected counts: partial payments too); deposit (CP_02): with a
                // security deposit (what Security deposits counts).
                if (status is not ("open" or "overdue" or "paid" or "advance" or "collected" or "deposit"))
                    return ApiResponse.Fail("status must be open, overdue, paid, advance, collected or deposit.");
            }
            // CS_01: billing period starting from / to (yyyy-MM-dd, both days included; CP_01), e.g. a custom period's revenue by location.
            DateTime? from = null, to = null;
            string? periodWindow = null;
            if (month == null && (!string.IsNullOrWhiteSpace(q.From) || !string.IsNullOrWhiteSpace(q.To)))
            {
                if (string.IsNullOrWhiteSpace(q.From) || string.IsNullOrWhiteSpace(q.To))
                    return ApiResponse.Fail("from and to (yyyy-MM-dd) are both required for invoices.");
                if (!TryDays(q.From, q.To, out from, out var end, out _, out var error)) return ApiResponse.Fail(error!);
                to = end!.Value.AddDays(-1);
                periodWindow = $"billing period {DashboardPeriod.RangeLabel(from!.Value, to.Value)}";
            }

            var statuses = await StatusesAsync();
            var customerId = q.CustomerId is > 0 ? q.CustomerId : null;
            var s = await _repo.GetInvoiceRecordsAsync(month, locationId, noLocation, bucket, customerId, status, _clock.Now, statuses, MaxRows, from, to);
            var head = Set(s, 0).FirstOrDefault();
            // A database before CP_02 knows no status collected (no totals in [0]): the paid invoices instead, as before.
            if (status == "collected" && !Has(head, "Billed"))
            {
                status = "paid";
                s = await _repo.GetInvoiceRecordsAsync(month, locationId, noLocation, bucket, customerId, status, _clock.Now, statuses, MaxRows, from, to);
                head = Set(s, 0).FirstOrDefault();
            }
            var rows = Set(s, 1).Select(r => new CeoInvoiceRecordDto
            {
                InvoiceId = Int(r, "InvoiceId"), InvoiceNumber = Str(r, "InvoiceNumber"), Customer = Str(r, "Customer"),
                Location = Str(r, "Location"), IssuedOn = Day(r, "IssuedOn"), DueOn = Day(r, "DueOn"),
                Total = Dec(r, "Total"), Paid = Dec(r, "Paid"), Balance = Dec(r, "Balance"), Status = Str(r, "Status"),
                // CP_01 columns; null before it (Day answers null for a missing column).
                PeriodStart = Day(r, "PeriodStart"), PeriodEnd = Day(r, "PeriodEnd"),
                // CP_02 columns; null before it.
                Wht = DecOrNull(r, "Wht"), Deposit = DecOrNull(r, "Deposit"), Billed = DecOrNull(r, "Billed"), Collected = DecOrNull(r, "Collected")
            }).ToList();

            var parts = new List<string>();
            if (status == "overdue") parts.Add("overdue");
            else if (status == "open") parts.Add("unpaid");
            else if (status == "paid") parts.Add("paid");
            else if (status == "advance") parts.Add("billed in advance");
            else if (status == "collected") parts.Add("with payments");
            else if (status == "deposit") parts.Add("with a security deposit");
            if (bucket != null) parts.Add(bucket == "not_due" ? "not due yet" : $"{Buckets.First(b => b.Key == bucket).Label} past due");
            if (month != null) parts.Add($"billing period {month.Value.ToString("MMMM yyyy", En)}");
            if (periodWindow != null) parts.Add(periodWindow);
            if (q.CustomerId is > 0) parts.Add(month == null ? $"{rows.FirstOrDefault()?.Customer ?? "customer"}, last 12 months" : rows.FirstOrDefault()?.Customer ?? "customer");
            if (noLocation) parts.Add("no location");
            else if (locationId != null && rows.FirstOrDefault()?.Location is { } loc) parts.Add(loc);
            var result = Records("Invoices", parts, Total(s), rows);
            // CP_02: the totals of every matching invoice (the list holds at most MaxRows).
            if (Has(head, "Billed") && result.Data is CeoRecordsDto<CeoInvoiceRecordDto> dto)
                dto.InvoiceTotals = new CeoInvoiceTotalsDto { Billed = Dec(head, "Billed"), Collected = Dec(head, "Collected"), Balance = Dec(head, "Balance") };
            return result;
        }

        private async Task<ApiResponse> SpacesAsync(CeoRecordsQuery q, int? locationId, bool noLocation)
        {
            var spaceTypeId = q.SpaceTypeId is >= 0 ? q.SpaceTypeId : null;
            var s = await _repo.GetSpaceRecordsAsync(spaceTypeId, locationId, noLocation, q.Occupied, _clock.Now, MaxRows);
            var rows = Set(s, 1).Select(r => new CeoSpaceRecordDto
            {
                SpaceId = Int(r, "SpaceId"), Name = Str(r, "Name"), SpaceType = Str(r, "SpaceType"), Location = Str(r, "Location"),
                Capacity = Val(r, "Capacity") is { } c ? Convert.ToInt32(c) : null,
                Occupied = Val(r, "Occupied") is { } o && Convert.ToBoolean(o),
                Customer = Str(r, "Customer"), BookingEndOn = Day(r, "BookingEndOn")
            }).ToList();

            var parts = new List<string>();
            if (spaceTypeId != null && rows.FirstOrDefault()?.SpaceType is { } t) parts.Add(t);
            if (noLocation) parts.Add("no location");
            else if (locationId != null && rows.FirstOrDefault()?.Location is { } loc) parts.Add(loc);
            if (q.Occupied == true) parts.Add("occupied");
            else if (q.Occupied == false) parts.Add("free");
            return Records("Spaces", parts, Total(s), rows);
        }

        private async Task<ApiResponse> BookingsAsync(CeoRecordsQuery q, int? locationId, bool noLocation)
        {
            if (!TryWindow(q, out var from, out var to, out var window, out var error)) return ApiResponse.Fail(error!);
            string? statusIds = null;
            var running = false;
            var status = (q.Status ?? "").Trim().ToLowerInvariant();
            switch (status)
            {
                case "": break;
                case "all": statusIds = "*"; break;
                case "active": case "running": statusIds = "1,2,5,33"; running = true; break;
                case "pending": statusIds = "1,5"; break;
                case "confirmed": statusIds = "2,33"; break;
                case "cancelled": statusIds = "3,4,6,86"; break;
                default:
                    if (int.TryParse(status, out var id) && id > 0) statusIds = id.ToString(En);
                    else return ApiResponse.Fail("status must be all, active, pending, confirmed, cancelled or a booking status id.");
                    break;
            }

            var s = await _repo.GetBookingRecordsAsync(from, to, locationId, noLocation, statusIds, running, _clock.Now, MaxRows);
            var rows = Set(s, 1).Select(r => new CeoBookingRecordDto
            {
                BookingId = Int(r, "BookingId"), Customer = Str(r, "Customer"), Space = Str(r, "Space"), Location = Str(r, "Location"),
                StartOn = Day(r, "StartOn"), EndOn = Day(r, "EndOn"), Status = Str(r, "Status"), TotalAmount = Dec(r, "TotalAmount")
            }).ToList();

            var parts = new List<string> { $"made {window}" };
            if (status.Length > 0) parts.Insert(0, status);
            if (noLocation) parts.Add("no location");
            else if (locationId != null && rows.FirstOrDefault()?.Location is { } loc) parts.Add(loc);
            return Records(status.Length == 0 ? "New bookings" : "Bookings", parts, Total(s), rows);
        }

        /// <summary>
        /// Tour inquiry records: of one branch when a location is chosen (QA_07; before it every location's, "all
        /// locations" in the title), else every inquiry.
        /// </summary>
        private async Task<ApiResponse> InquiriesAsync(CeoRecordsQuery q, int? locationId)
        {
            string? type = null;
            if (!string.IsNullOrWhiteSpace(q.Type))
            {
                type = InquiryType(q.Type);
                if (type == null) return ApiResponse.Fail(TypeError);
            }
            if (!TryWindow(q, out var from, out var to, out var window, out var error)) return ApiResponse.Fail(error!);

            var s = await _repo.GetInquiryRecordsAsync(from, to, type, locationId, MaxRows);
            var rows = Set(s, 1).Select(r => new CeoInquiryRecordDto
            {
                InquiryId = Int(r, "InquiryId"), Name = Str(r, "Name"), Phone = Str(r, "Phone"), Type = Str(r, "InquiryType"),
                ReferenceName = Str(r, "ReferenceName"), Status = InquiryStatus(Int(r, "StatusId")), CreatedOn = Utc(r, "CreatedOn"),
                Feedback = Feedback(Str(r, "FeedbackOutcome"), Str(r, "FeedbackReason"))
            }).ToList();

            var parts = new List<string>();
            if (type != null) parts.Add(InquiryTypeLabels.First(t => t.Type == type).Label);
            parts.Add($"received {window}");
            if (locationId != null && !Flag(Set(s, 0).FirstOrDefault(), "ByLocation")) parts.Add("all locations");   // QA_07 not run yet
            return Records("Tour inquiries", parts, Total(s), rows);
        }

        /// <summary>
        /// Confirmed leases ending in a window, soonest first (QA_07): month (ending that month), else from / to
        /// (yyyy-MM-dd, both days included), else the next 90 days (the dashboard's "Leases expiring").
        /// </summary>
        private async Task<ApiResponse> LeasesAsync(CeoRecordsQuery q, int? locationId, bool noLocation)
        {
            DateTime? from = null, to = null;
            string window;
            if (!string.IsNullOrWhiteSpace(q.Month))
            {
                if (!TryMonth(q.Month, out var m)) return ApiResponse.Fail(MonthError);
                from = m; to = m.AddMonths(1); window = $"ending in {m.ToString("MMMM yyyy", En)}";
            }
            else if (!string.IsNullOrWhiteSpace(q.From) || !string.IsNullOrWhiteSpace(q.To))
            {
                if (!TryDays(q.From, q.To, out from, out to, out var label, out var error)) return ApiResponse.Fail(error!);
                window = $"ending {label}";
            }
            else window = $"ending in the next {ExpiringDays} days";

            var s = await _repo.GetLeaseRecordsAsync(locationId, noLocation, from, to, ExpiringDays, _clock.Now, MaxRows);
            var rows = Set(s, 1).Select(r => new CeoLeaseRecordDto
            {
                BookingId = Int(r, "BookingId"), Customer = Str(r, "Customer"), Space = Str(r, "Space"), Location = Str(r, "Location"),
                EndOn = Day(r, "EndOn"), MonthlyAmount = Dec(r, "MonthlyAmount"),
                DaysLeft = Val(r, "DaysLeft") is { } d ? Convert.ToInt32(d, En) : null
            }).ToList();

            var parts = new List<string> { window };
            if (noLocation) parts.Add("no location");
            else if (locationId != null && rows.FirstOrDefault()?.Location is { } loc) parts.Add(loc);
            return Records("Leases", parts, Total(s), rows);
        }

        private async Task<ApiResponse> QuotationsAsync(CeoRecordsQuery q, int? locationId)
        {
            var stage = (q.Stage ?? "").Trim().ToLowerInvariant();
            if (stage.Length == 0) stage = "quotations";
            if (stage is not ("quotations" or "accepted" or "converted")) return ApiResponse.Fail("stage must be quotations, accepted or converted.");
            if (!TryWindow(q, out var from, out var to, out var window, out var error)) return ApiResponse.Fail(error!);
            var userId = q.UserId is > 0 ? q.UserId : null;

            var s = await _repo.GetQuotationRecordsAsync(from, to, userId, stage, locationId, MaxRows);
            var rows = Set(s, 1).Select(r => new CeoQuotationRecordDto
            {
                QuotationId = Int(r, "QuotationId"), QuotationNumber = Str(r, "QuotationNumber"), Customer = Str(r, "Customer"),
                Location = Str(r, "Location"), CreatedBy = Str(r, "CreatedBy"), Status = Str(r, "Status"), Total = Dec(r, "Total"),
                CreatedOn = Utc(r, "CreatedOn")
            }).ToList();

            var parts = new List<string>();
            if (userId != null) parts.Add($"by {rows.FirstOrDefault()?.CreatedBy ?? $"user #{userId}"}");
            parts.Add($"created {window}");
            if (locationId != null && rows.FirstOrDefault()?.Location is { } loc) parts.Add(loc);
            var title = stage switch { "accepted" => "Accepted quotations", "converted" => "Converted quotations", _ => "Quotations" };
            return Records(title, parts, Total(s), rows);
        }

        // ------------------------------------------------------------------ rooms (CEO_02)

        /// <summary>Sold within this share of the expected price per seat (either way) counts as "at": 0.5%.</summary>
        public const decimal PricingBand = 0.005m;

        /// <summary>The shifts, in the order byShift lists them.</summary>
        public static readonly IReadOnlyList<string> RoomShifts = new[] { "24/7", "Morning", "Evening" };

        /// <summary>
        /// Rooms sold monthly (dbo.WN_Ceo_RoomReport, CEO_02): occupancy now and in the period, and every confirmed
        /// booking running in the period priced against its expected price (<see cref="PriceSale"/>), with totals
        /// (<see cref="SummarizeRooms"/>). Before CEO_02: available = false and the message, no summary, no rooms.
        /// </summary>
        public async Task<ApiResponse> GetRoomReportAsync(string? period, int? locationId, string? from = null, string? to = null)
        {
            if (!DashboardPeriod.TryParse(period, from, to, _clock.Today, out var p, out var periodError)) return ApiResponse.Fail(periodError!);
            var now = _clock.Now;
            var dto = new CeoRoomReportDto
            {
                Period = p.Key,
                PeriodLabel = p.Label(now),
                GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", En),
                From = p.FromText,
                To = p.ToText
            };
            List<List<IDictionary<string, object?>>> s;
            try
            {
                s = await _repo.GetRoomReportAsync(Location(locationId), p.Months, now, p.From, p.To);
            }
            catch (CeoDatabaseUpdateMissingException ex) when (ex.Update == "CEO_02")
            {
                dto.Available = false;
                dto.Message = CeoDatabaseUpdateMissingException.RoomsMessage;
                return ApiResponse.Ok(dto);
            }

            var rooms = new List<CeoRoomDto>();
            var byId = new Dictionary<int, CeoRoomDto>();
            // A category's label when the procedure sends one with its rooms (CategoryLabel); else the database's (below).
            var categoryLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int occupiedDays = 0, periodDays = 0;
            foreach (var r in Set(s, 0))
            {
                var seats = Int(r, "Capacity");
                var list = Val(r, "ListSeatPrice") is { } lp && Convert.ToDecimal(lp, En) > 0 ? Math.Round(Convert.ToDecimal(lp, En), 2) : (decimal?)null;
                var room = new CeoRoomDto
                {
                    SpaceId = Int(r, "SpaceId"), Name = Str(r, "Name"), Code = Str(r, "Code"),
                    LocationId = Val(r, "LocationId") is { } l ? Convert.ToInt32(l, En) : null,
                    Location = Str(r, "Location"), SpaceType = Str(r, "SpaceType"), CategoryCode = Str(r, "CategoryCode"),
                    Seats = seats, ListSeatPrice = list, ListRoomPrice = list is { } x ? Money(x * seats) : null,
                    OccupiedNow = Flag(r, "OccupiedNow"),
                    OccupiedDays = Int(r, "OccupiedDays"),
                    PeriodDays = Int(r, "PeriodDays"),
                    PeriodOccupancyPct = Pct(Int(r, "OccupiedDays"), Int(r, "PeriodDays"))
                };
                if (!byId.TryAdd(room.SpaceId, room)) continue;
                rooms.Add(room);
                occupiedDays += room.OccupiedDays;
                periodDays += room.PeriodDays;
                if (Str(r, "CategoryLabel") is { } cl && !string.IsNullOrWhiteSpace(cl) && !string.IsNullOrWhiteSpace(room.CategoryCode))
                    categoryLabels.TryAdd(room.CategoryCode.Trim(), cl.Trim());
            }
            foreach (var r in Set(s, 1))
            {
                if (!byId.TryGetValue(Int(r, "SpaceId"), out var room)) continue;
                var sold = Val(r, "SoldMonthly") is { } sm
                    ? Convert.ToDecimal(sm, En)
                    : (Val(r, "MonthlyRent") is { } mr ? Convert.ToDecimal(mr, En) : 0m) * (1 - (Val(r, "DiscountPct") is { } dp ? Convert.ToDecimal(dp, En) : 0m) / 100m);
                room.Sales.Add(PriceSale(new CeoRoomSaleDto
                {
                    BookingId = Int(r, "BookingId"), Customer = Str(r, "Customer"),
                    Shift = Str(r, "Shift") ?? Str(r, "ShiftRaw") ?? "",
                    SeatsSold = Int(r, "SeatsSold"), StartOn = Day(r, "StartOn"), EndOn = Day(r, "EndOn"),
                    ListSeatPriceAtSale = Val(r, "ListSeatPriceAtSale") is { } ls ? Convert.ToDecimal(ls, En) : null,
                    ShiftDiscountCapPct = Val(r, "ShiftDiscountCapPct") is { } cp ? Convert.ToDecimal(cp, En) : 0m,
                    SoldMonthly = sold
                }, room.Seats, room.ListSeatPrice));
            }

            dto.Rooms = SortRooms(rooms);
            await AddSpaceCategoryLabelsAsync(categoryLabels);
            dto.Summary = SummarizeRooms(dto.Rooms, occupiedDays, periodDays, categoryLabels);
            return ApiResponse.Ok(dto);
        }

        /// <summary>
        /// The labels of the space categories (dbo.WN_SpaceCategories: Code, Label) for the codes not labelled yet. A
        /// failed lookup only leaves them to <see cref="RoomCategoryLabel"/>'s fallback: the report still answers.
        /// </summary>
        private async Task AddSpaceCategoryLabelsAsync(Dictionary<string, string> labels)
        {
            if (_spaceTypes == null) return;
            try
            {
                foreach (var row in (await _spaceTypes.GetSpaceCategoriesAsync()).OfType<IDictionary<string, object?>>())
                {
                    var code = Str(row, "Code")?.Trim();
                    var label = Str(row, "Label")?.Trim();
                    if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(label)) labels.TryAdd(code, label);
                }
            }
            catch (Exception)
            {
                // Labels only: the report must not fail because of them.
            }
        }

        /// <summary>24/7 | Morning | Evening from a booking's ShiftType (or the procedure's Shift), as dbo.WN_Ceo_RoomReport:
        /// morning... / shift_morning... / 2 = Morning; evening... / night... / shift_evening... / 3 = Evening; else 24/7.</summary>
        public static string NormalizeShift(string? shift)
        {
            var v = (shift ?? "").Trim().ToLowerInvariant();
            if (v.StartsWith("morning") || v.StartsWith("shift_morning") || v == "2") return "Morning";
            if (v.StartsWith("evening") || v.StartsWith("night") || v.StartsWith("shift_evening") || v.StartsWith("shift_night") || v == "3")
                return "Evening";
            return "24/7";
        }

        /// <summary>
        /// Prices one sale (the procedure's raw figures in <paramref name="sale"/>; it is completed and returned):
        /// seats sold = the booking's seats, else the room's (<paramref name="roomSeats"/>), else 1; list per seat = the
        /// price at sale, else today's (<paramref name="roomListSeatPrice"/>); expected per seat = the list (24/7) or
        /// list x (1 - shift discount cap / 100) (Morning / Evening); variance per month = (sold per seat - expected per
        /// seat) x seats; under / over = more than <see cref="PricingBand"/> below / above the expected per seat, else
        /// at. No list price known (or nothing expected): not compared, the expected / variance figures and Pricing null.
        /// </summary>
        public static CeoRoomSaleDto PriceSale(CeoRoomSaleDto sale, int roomSeats, decimal? roomListSeatPrice)
        {
            sale.Shift = NormalizeShift(sale.Shift);
            var seats = sale.SeatsSold > 0 ? sale.SeatsSold : roomSeats > 0 ? roomSeats : 1;
            sale.SeatsSold = seats;
            decimal? list = sale.ListSeatPriceAtSale is > 0 ? sale.ListSeatPriceAtSale : roomListSeatPrice is > 0 ? roomListSeatPrice : null;
            sale.ListSeatPriceAtSale = list is { } lv ? Money(lv) : null;
            var cap = Math.Clamp(sale.ShiftDiscountCapPct, 0m, 100m);
            sale.ShiftDiscountCapPct = Math.Round(cap, 2);

            var sold = sale.SoldMonthly;
            var soldSeat = sold / seats;
            sale.SoldMonthly = Money(sold);
            sale.SoldSeatPrice = Money(soldSeat);

            decimal? expectedSeat = list is { } l ? (sale.Shift == "24/7" ? l : l * (1 - cap / 100m)) : null;
            if (expectedSeat is not > 0)
            {
                sale.ExpectedSeatPrice = sale.ExpectedMonthly = sale.VarianceMonthly = sale.VariancePct = null;
                sale.Pricing = null;
                return sale;
            }
            var expected = expectedSeat.Value;
            var ratio = soldSeat / expected - 1;
            sale.ExpectedSeatPrice = Money(expected);
            sale.ExpectedMonthly = Money(expected * seats);
            sale.VarianceMonthly = Money((soldSeat - expected) * seats);
            sale.VariancePct = Money(ratio * 100m);
            sale.Pricing = ratio < -PricingBand ? "under" : ratio > PricingBand ? "over" : "at";
            return sale;
        }

        /// <summary>The room categories of the report (dbo.WN_Ceo_RoomReport's rooms), in the order byCategory lists them.</summary>
        public static readonly IReadOnlyList<string> RoomCategories = new[] { "PrivateOffice", "SharedSpace" };

        /// <summary>A category's label when the database gives none: "PrivateOffice" -> "Private Office", "" -> "Other".</summary>
        public static string RoomCategoryLabel(string code, IReadOnlyDictionary<string, string>? labels = null)
        {
            if (labels != null && labels.TryGetValue(code, out var label) && !string.IsNullOrWhiteSpace(label)) return label.Trim();
            if (string.IsNullOrWhiteSpace(code)) return "Other";
            // Words of the code: a capital letter after a small one starts a new word.
            var words = new System.Text.StringBuilder();
            for (var i = 0; i < code.Length; i++)
            {
                if (i > 0 && char.IsUpper(code[i]) && char.IsLower(code[i - 1])) words.Append(' ');
                words.Append(i == 0 ? char.ToUpperInvariant(code[i]) : code[i]);
            }
            return words.ToString().Replace('_', ' ').Trim();
        }

        /// <summary>A room's category code as byCategory groups it (the report's spelling of a known code; "" when none).</summary>
        private static string CategoryOf(CeoRoomDto room)
        {
            var code = room.CategoryCode?.Trim() ?? "";
            return RoomCategories.FirstOrDefault(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)) ?? code;
        }

        /// <summary>
        /// Occupancy per category: always <see cref="RoomCategories"/> (zeros when there are none), then any other code
        /// the rooms carry. Days occupied from the rooms' own days (room-days), as the totals.
        /// </summary>
        public static List<CeoRoomCategorySummaryDto> SummarizeRoomCategories(IReadOnlyCollection<CeoRoomDto> rooms,
            IReadOnlyDictionary<string, string>? labels = null)
        {
            var codes = RoomCategories.Concat(rooms.Select(CategoryOf).Where(c => !RoomCategories.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase));
            return codes.Select(code =>
            {
                var g = rooms.Where(r => string.Equals(CategoryOf(r), code, StringComparison.OrdinalIgnoreCase)).ToList();
                var occupied = g.Count(r => r.OccupiedNow);
                return new CeoRoomCategorySummaryDto
                {
                    CategoryCode = code,
                    Label = RoomCategoryLabel(code, labels),
                    Rooms = g.Count,
                    OccupiedNow = occupied,
                    VacantNow = g.Count - occupied,
                    OccupancyNowPct = Pct(occupied, g.Count),
                    PeriodOccupancyPct = Pct(g.Sum(r => r.OccupiedDays), g.Sum(r => r.PeriodDays))
                };
            }).ToList();
        }

        /// <summary>
        /// Totals of the rooms and their priced sales: occupancy now (rooms with a confirmed booking now), occupancy in
        /// the period (<paramref name="occupiedDays"/> / <paramref name="periodDays"/>, room-days), under / at / over,
        /// expected (list) / sold / variance per month, the same by shift (always 24/7, Morning, Evening), and the
        /// occupancy per category (<see cref="SummarizeRoomCategories"/>; <paramref name="categoryLabels"/>: code -> label).
        /// </summary>
        public static CeoRoomSummaryDto SummarizeRooms(IReadOnlyCollection<CeoRoomDto> rooms, int occupiedDays, int periodDays,
            IReadOnlyDictionary<string, string>? categoryLabels = null)
        {
            var sales = rooms.SelectMany(r => r.Sales).ToList();
            var occupied = rooms.Count(r => r.OccupiedNow);
            return new CeoRoomSummaryDto
            {
                ByCategory = SummarizeRoomCategories(rooms, categoryLabels),
                Rooms = rooms.Count,
                OccupiedNow = occupied,
                VacantNow = rooms.Count - occupied,
                OccupancyNowPct = Pct(occupied, rooms.Count),
                PeriodOccupancyPct = Pct(occupiedDays, periodDays),
                Sales = sales.Count,
                Under = sales.Count(x => x.Pricing == "under"),
                At = sales.Count(x => x.Pricing == "at"),
                Over = sales.Count(x => x.Pricing == "over"),
                Unpriced = sales.Count(x => x.Pricing == null),
                ListMonthly = sales.Sum(x => x.ExpectedMonthly ?? 0m),
                SoldMonthly = sales.Sum(x => x.SoldMonthly),
                VarianceMonthly = sales.Sum(x => x.VarianceMonthly ?? 0m),
                UnderMonthly = sales.Where(x => x.Pricing == "under").Sum(x => x.VarianceMonthly ?? 0m),
                OverMonthly = sales.Where(x => x.Pricing == "over").Sum(x => x.VarianceMonthly ?? 0m),
                ByShift = RoomShifts.Select(shift =>
                {
                    var g = sales.Where(x => x.Shift == shift).ToList();
                    return new CeoRoomShiftSummaryDto
                    {
                        Shift = shift,
                        Sales = g.Count,
                        Under = g.Count(x => x.Pricing == "under"),
                        At = g.Count(x => x.Pricing == "at"),
                        Over = g.Count(x => x.Pricing == "over"),
                        Unpriced = g.Count(x => x.Pricing == null),
                        ExpectedMonthly = g.Sum(x => x.ExpectedMonthly ?? 0m),
                        SoldMonthly = g.Sum(x => x.SoldMonthly),
                        VarianceMonthly = g.Sum(x => x.VarianceMonthly ?? 0m)
                    };
                }).ToList()
            };
        }

        /// <summary>By location (rooms without one last), then room code, then name; numbers in order (9 before 10).</summary>
        private static List<CeoRoomDto> SortRooms(IEnumerable<CeoRoomDto> rooms) =>
            rooms.OrderBy(r => string.IsNullOrWhiteSpace(r.Location))
                .ThenBy(r => r.Location ?? "", Natural)
                .ThenBy(r => r.Code ?? r.Name ?? "", Natural)
                .ThenBy(r => r.Name ?? "", Natural)
                .ThenBy(r => r.SpaceId)
                .ToList();

        /// <summary>Compares text with runs of digits taken as numbers ("Office 9" before "Office 10"), case-insensitive.</summary>
        private static readonly IComparer<string> Natural = Comparer<string>.Create((a, b) =>
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    var x = a[si..i].TrimStart('0');
                    var y = b[sj..j].TrimStart('0');
                    var c = x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
                    if (c != 0) return c;
                }
                else
                {
                    var c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        });

        /// <summary>PKR, 2 decimals (half away from zero).</summary>
        private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        // ------------------------------------------------------------------ helpers

        private const string MonthError = "month must be yyyy-MM (e.g. 2026-10).";
        private const string BucketError = "bucket must be not_due, 1_30, 31_60, 61_90 or 90_plus.";
        private const string TypeError = "type must be Website, WhatsApp, WalkIn, Reference or NotSet.";

        private async Task<CeoInvoiceStatusIds> StatusesAsync()
        {
            var st = await _orderStatus.GetInvoiceStatusesAsync();
            // Same sets as the overview: open = Unpaid / Partial / Challan Expire; void = Cancelled + legacy 5.
            return new CeoInvoiceStatusIds(
                st.Unpaid.Concat(st.Partial).Concat(st.Overdue).Distinct().ToList(),
                st.Paid.ToList(),
                st.Cancelled.Append(5).Distinct().ToList());
        }

        /// <summary>Start of the current period (calendar month / quarter / year), as the overview and the procedures.</summary>
        private DateTime PeriodStart(string period)
        {
            var today = _clock.Today;
            return period switch
            {
                "year" => new DateTime(today.Year, 1, 1),
                "quarter" => new DateTime(today.Year, (today.Month - 1) / 3 * 3 + 1, 1),
                _ => new DateTime(today.Year, today.Month, 1)
            };
        }

        private static bool TryMonth(string? value, out DateTime month) =>
            DateTime.TryParseExact((value ?? "").Trim(), "yyyy-MM", En, DateTimeStyles.None, out month);

        private static int? Location(int? locationId) => locationId is > 0 ? locationId : null;

        /// <summary>Records: a location id, "none" (not tied to a location), or nothing / 0 (all locations).</summary>
        private static bool TryRecordLocation(string? value, out int? locationId, out bool noLocation)
        {
            locationId = null;
            noLocation = false;
            var v = (value ?? "").Trim();
            if (v.Length == 0) return true;
            if (v.Equals("none", StringComparison.OrdinalIgnoreCase)) { noLocation = true; return true; }
            if (!int.TryParse(v, NumberStyles.Integer, En, out var id)) return false;
            locationId = id > 0 ? id : null;
            return true;
        }

        /// <summary>
        /// Created-in window for bookings, inquiries and quotations: month (that month), else from / to (yyyy-MM-dd,
        /// both days included), else the period: to date (default this month), or a custom period's range (periodFrom /
        /// periodTo, already checked by the caller). <paramref name="label"/> describes it.
        /// </summary>
        private bool TryWindow(CeoRecordsQuery q, out DateTime? from, out DateTime? to, out string label, out string? error)
        {
            from = null; to = null; label = ""; error = null;
            if (!string.IsNullOrWhiteSpace(q.Month))
            {
                if (!TryMonth(q.Month, out var m)) { error = MonthError; return false; }
                from = m; to = m.AddMonths(1); label = $"in {m.ToString("MMMM yyyy", En)}";
                return true;
            }
            if (!string.IsNullOrWhiteSpace(q.From) || !string.IsNullOrWhiteSpace(q.To))
                return TryDays(q.From, q.To, out from, out to, out label, out error);
            if (!DashboardPeriod.TryParse(q.Period, q.PeriodFrom, q.PeriodTo, _clock.Today, out var p, out error)) return false;
            if (p.IsCustom)
            {
                // These lists filter on columns written in UTC (bookings CreatedOn, quotations CreatedDate, tour
                // inquiries CreatedOn): the Pakistan days of the range as UTC times, as the dashboard counts them (CS_01).
                from = p.UtcStart; to = p.UtcEnd; label = p.BetweenWords();
                return true;
            }
            var period = p.Key;
            from = PeriodStart(period);
            label = period switch
            {
                "year" => $"in {from.Value.Year}",
                "quarter" => $"in Q{(from.Value.Month - 1) / 3 + 1} {from.Value.Year}",
                _ => $"in {from.Value.ToString("MMMM yyyy", En)}"
            };
            return true;
        }

        /// <summary>
        /// from / to (yyyy-MM-dd, both days included; to is returned exclusive, the next day). <paramref name="label"/>
        /// describes the window ("from 2026-10-01 to 2026-10-31").
        /// </summary>
        private static bool TryDays(string? fromText, string? toText, out DateTime? from, out DateTime? to, out string label, out string? error)
        {
            from = null; to = null; label = ""; error = null;
            DateTime f = default, t = default;
            if (!string.IsNullOrWhiteSpace(fromText) && !DateTime.TryParseExact(fromText.Trim(), "yyyy-MM-dd", En, DateTimeStyles.None, out f))
            { error = "from must be yyyy-MM-dd."; return false; }
            if (!string.IsNullOrWhiteSpace(toText) && !DateTime.TryParseExact(toText.Trim(), "yyyy-MM-dd", En, DateTimeStyles.None, out t))
            { error = "to must be yyyy-MM-dd."; return false; }
            if (!string.IsNullOrWhiteSpace(fromText)) from = f;
            if (!string.IsNullOrWhiteSpace(toText)) to = t.AddDays(1);
            if (from != null && to != null && from >= to) { error = "from must be on or before to."; return false; }
            label = (from, to) switch
            {
                ({ } a, { } b) => $"from {a:yyyy-MM-dd} to {b.AddDays(-1):yyyy-MM-dd}",
                ({ } a, null) => $"from {a:yyyy-MM-dd}",
                _ => $"up to {to!.Value.AddDays(-1):yyyy-MM-dd}"
            };
            return true;
        }

        /// <summary>The stored Type for any common spelling; "NotSet" for no Type; null when unknown.</summary>
        private static string? InquiryType(string? value)
        {
            var letters = new string((value ?? "").Where(char.IsLetter).ToArray()).ToLowerInvariant();
            if (letters is "notset" or "none" or "unset") return "NotSet";
            return InquiryTypes.Normalize(value);
        }

        private static string InquiryStatus(int statusId) => statusId switch { 2 => "In Progress", 3 => "Resolved", _ => "New" };

        private static string? Feedback(string? outcome, string? reason)
        {
            var label = (outcome ?? "").Trim().ToLowerInvariant() switch
            {
                "" => null,
                "not_interested" => "Not interested",
                "future_prospect" => "Future prospect",
                "converted" => "Converted",
                var o => o.Replace('_', ' ')
            };
            if (label == null) return null;
            return string.IsNullOrWhiteSpace(reason) ? label : $"{label}: {reason.Trim()}";
        }

        private static ApiResponse Records<T>(string title, List<string> parts, int total, List<T> rows) =>
            ApiResponse.Ok(new CeoRecordsDto<T>
            {
                Title = parts.Count == 0 ? title : $"{title}: {string.Join(", ", parts)}",
                Total = total,
                Rows = rows
            });

        private static int Total(List<List<IDictionary<string, object?>>> s) => Int(Set(s, 0).FirstOrDefault(), "Total");

        private static List<IDictionary<string, object?>> Set(List<List<IDictionary<string, object?>>> s, int i) => i < s.Count ? s[i] : new();
        private static bool Has(IDictionary<string, object?>? r, string key) => r != null && r.ContainsKey(key);
        private static object? Val(IDictionary<string, object?>? r, string key) => r != null && r.TryGetValue(key, out var v) ? v : null;
        private static int Int(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToInt32(v) : 0;
        private static decimal Dec(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Math.Round(Convert.ToDecimal(v), 2) : 0m;
        /// <summary>A money column; null when it is missing or NULL (e.g. a procedure from before CP_02).</summary>
        private static decimal? DecOrNull(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Math.Round(Convert.ToDecimal(v), 2) : null;
        private static string? Str(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToString(v, En) : null;
        /// <summary>A bit column; false when it is missing (e.g. a procedure from before QA_07).</summary>
        private static bool Flag(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v && Convert.ToBoolean(v, En);
        private static string? Day(IDictionary<string, object?>? r, string key) => Val(r, key) is DateTime d ? d.ToString("yyyy-MM-dd", En) : null;
        /// <summary>CreatedOn / CreatedDate columns are written in UTC (SYSUTCDATETIME), so they get a Z.</summary>
        private static string? Utc(IDictionary<string, object?>? r, string key) => Val(r, key) is DateTime d ? d.ToString("yyyy-MM-ddTHH:mm:ss'Z'", En) : null;
        private static decimal Pct(int part, int total) => total > 0 ? Math.Round(part * 100m / total, 1) : 0m;
    }
}
