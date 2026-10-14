using System.Globalization;
using WorkNest.Application.DTOs.Contact;
using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Sales Manager dashboard (api/sales-manager/*): read-only, the caller's locations (super admins: all). The numbers
    /// come from the dbo.WN_SalesMgr_* procedures, which use the same rules as the CEO dashboard (dbo.WN_Ceo_*), with
    /// the same invoice statuses and business clock, so the figures match it for the same location and period. Tour
    /// inquiries have no location: always all of them. Level 1 = dashboard, level 2 = breakdown, level 3 = records.
    /// </summary>
    public class SalesManagerDashboardService : ISalesManagerDashboardService
    {
        private const int ExpiringDays = 90;
        private const int MaxRows = 200;

        private static readonly CultureInfo En = CultureInfo.InvariantCulture;

        private static readonly (string Type, string Label)[] InquiryTypeLabels =
        {
            (InquiryTypes.Website, "Website"), (InquiryTypes.WhatsApp, "WhatsApp"), (InquiryTypes.WalkIn, "Walk-In"),
            (InquiryTypes.Reference, "Reference"), ("NotSet", "Not set")
        };

        private static readonly string[] Metrics = { "funnel-month", "staff", "inquiries-type", "renewals-month", "revenue-month" };

        private static readonly string[] Outcomes = { "followup_due", "not_interested", "converted", "open", "followed_up" };

        private readonly ISalesManagerDashboardRepository _repo;
        private readonly IOrderStatusService _orderStatus;
        private readonly IBusinessClock _clock;

        public SalesManagerDashboardService(ISalesManagerDashboardRepository repo, IOrderStatusService orderStatus, IBusinessClock clock)
        {
            _repo = repo;
            _orderStatus = orderStatus;
            _clock = clock;
        }

        // ------------------------------------------------------------------ level 1: dashboard

        public async Task<ApiResponse> GetDashboardAsync(string? period, SalesManagerScope scope)
        {
            if (!TryPeriod(period, out var p, out var months)) return ApiResponse.Fail(PeriodError);
            var now = _clock.Now;
            var s = await _repo.GetDashboardAsync(scope.LocationIdsCsv, scope.PickerCsv, months, now, await VoidStatusesAsync(), ExpiringDays);

            var funnel = Set(s, 1).FirstOrDefault();
            var inq = Set(s, 5).FirstOrDefault();
            var ren = Set(s, 9).FirstOrDefault();
            var quotations = Int(funnel, "Quotations");
            var bookings = Int(funnel, "Bookings");
            var byType = Set(s, 6).ToDictionary(r => Str(r, "InquiryType") ?? "NotSet", r => Int(r, "Inquiries"), StringComparer.OrdinalIgnoreCase);

            var dto = new SalesManagerDashboardDto
            {
                Period = p,
                PeriodLabel = PeriodLabel(p, now),
                GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", En),
                Locations = Set(s, 0).Select(r => new CeoLocationDto { LocationId = Int(r, "LocationId"), Name = Str(r, "Name") ?? "" }).ToList(),
                Funnel = new SalesManagerFunnelDto
                {
                    Stages = new List<CeoFunnelStageDto>
                    {
                        new() { Stage = "inquiries", Label = "Tour inquiries", Count = Int(funnel, "Inquiries") },
                        new() { Stage = "quotations", Label = "Quotations", Count = quotations },
                        new() { Stage = "accepted", Label = "Accepted", Count = Int(funnel, "Accepted") },
                        new() { Stage = "bookings", Label = "Bookings", Count = bookings }
                    },
                    ConversionRate = Pct(bookings, quotations),
                    ConversionRatePrev = Pct(Int(funnel, "BookingsPrev"), Int(funnel, "QuotationsPrev")),
                    QuotedValue = Dec(funnel, "QuotedValue"),
                    BookedValue = Dec(funnel, "BookedValue"),
                    BookedValuePrev = Dec(funnel, "BookedValuePrev"),
                    Months = Set(s, 2).Select(r => new SalesManagerFunnelMonthDto
                    {
                        Month = Str(r, "Month") ?? "", Quotations = Int(r, "Quotations"), Accepted = Int(r, "Accepted"),
                        Bookings = Int(r, "Bookings"), BookedValue = Dec(r, "BookedValue")
                    }).ToList()
                },
                Team = new SalesManagerTeamDto
                {
                    People = Set(s, 3).Select(r => new SalesManagerPersonDto
                    {
                        UserId = Int(r, "UserId"), Name = Str(r, "Name") ?? "", Role = Str(r, "RoleName") ?? "",
                        Locations = Str(r, "Locations"), InquiriesHandled = Int(r, "InquiriesHandled"),
                        Quotations = Int(r, "Quotations"), Accepted = Int(r, "Accepted"), Converted = Int(r, "Converted"),
                        ConvertedValue = Dec(r, "ConvertedValue"), ConversionRate = Pct(Int(r, "Converted"), Int(r, "Quotations")),
                        FollowUpsDue = Int(r, "FollowUpsDue")
                    }).ToList(),
                    PerLocation = Set(s, 4).Select(r => new SalesManagerTeamLocationDto
                    {
                        LocationId = Int(r, "LocationId"), Name = Str(r, "Name") ?? "", SalesExecutives = Int(r, "SalesExecutives"),
                        Quotations = Int(r, "Quotations"), Converted = Int(r, "Converted"), ConvertedValue = Dec(r, "ConvertedValue")
                    }).ToList()
                },
                Inquiries = new SalesManagerInquiriesDto
                {
                    Total = Int(inq, "Total"), TotalPrev = Int(inq, "TotalPrev"), FollowedUp = Int(inq, "FollowedUp"),
                    NotInterested = Int(inq, "NotInterested"), Converted = Int(inq, "Converted"), FollowUpsDue = Int(inq, "FollowUpsDue"),
                    ByType = InquiryTypeLabels
                        .Select(t => new CeoInquiryTypeCountDto { Type = t.Type, Label = t.Label, Count = byType.GetValueOrDefault(t.Type) })
                        .Concat(byType.Where(kv => InquiryTypeLabels.All(t => !t.Type.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)))
                            .Select(kv => new CeoInquiryTypeCountDto { Type = kv.Key, Label = kv.Key, Count = kv.Value }))
                        .ToList(),
                    NotInterestedReasons = Set(s, 7).Select(r => new SalesManagerReasonDto
                        { Reason = Str(r, "Reason") ?? "", Count = Int(r, "Inquiries") }).ToList(),
                    Months = Set(s, 8).Select(r => new SalesManagerInquiryMonthDto
                    {
                        Month = Str(r, "Month") ?? "", Website = Int(r, "Website"), WhatsApp = Int(r, "WhatsApp"), WalkIn = Int(r, "WalkIn"),
                        Reference = Int(r, "Reference"), NotSet = Int(r, "NotSet")
                    }).ToList()
                },
                Renewals = new SalesManagerRenewalsDto
                {
                    Expiring = Set(s, 10).Select(Lease).ToList(),
                    ExpiringCount = Int(ren, "ExpiringCount"),
                    ExpiringMonthlyValue = Dec(ren, "ExpiringMonthlyValue"),
                    NewBookingRevenue = Dec(ren, "NewBookingRevenue"),
                    NewBookingRevenuePrev = Dec(ren, "NewBookingRevenuePrev"),
                    Months = Set(s, 11).Select(r => new SalesManagerRenewalMonthDto
                        { Month = Str(r, "Month") ?? "", NewBookingRevenue = Dec(r, "NewBookingRevenue"), Expiring = Int(r, "Expiring") }).ToList()
                }
            };
            return ApiResponse.Ok(dto);
        }

        // ------------------------------------------------------------------ level 2: breakdown

        public async Task<ApiResponse> GetBreakdownAsync(SalesManagerBreakdownQuery q, SalesManagerScope scope)
        {
            var metric = (q.Metric ?? "").Trim().ToLowerInvariant();
            if (metric.Length == 0) return ApiResponse.Fail($"metric is required: {string.Join(", ", Metrics)}.");
            if (!Metrics.Contains(metric)) return ApiResponse.Fail($"Unknown metric '{q.Metric}'. Use {string.Join(", ", Metrics)}.");
            if (!string.IsNullOrWhiteSpace(q.Period) && !TryPeriod(q.Period, out _, out _)) return ApiResponse.Fail(PeriodError);

            DateTime? month = null;
            if (metric is "funnel-month" or "renewals-month" or "revenue-month")
            {
                if (string.IsNullOrWhiteSpace(q.Month)) return ApiResponse.Fail($"month (yyyy-MM) is required for metric {metric}.");
                if (!TryMonth(q.Month, out var m)) return ApiResponse.Fail(MonthError);
                month = m;
            }
            string? type = null;
            if (metric == "inquiries-type")
            {
                if (string.IsNullOrWhiteSpace(q.Type)) return ApiResponse.Fail($"type is required for metric inquiries-type. {TypeError}");
                type = InquiryType(q.Type);
                if (type == null) return ApiResponse.Fail(TypeError);
            }
            if (metric == "staff" && q.UserId is null or <= 0) return ApiResponse.Fail("userId is required for metric staff.");

            var s = await _repo.GetBreakdownAsync(metric, scope.LocationIdsCsv, month, type, metric == "staff" ? q.UserId : null,
                _clock.Now, await VoidStatusesAsync());
            var head = Set(s, 0).FirstOrDefault();
            if (metric == "staff" && Val(head, "InScope") is { } inScope && !Convert.ToBoolean(inScope))
                throw new SalesManagerForbiddenException("That person does not work at your locations.");
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
            var monthName = month?.ToString("MMMM yyyy", En);
            (dto.Title, dto.Subtitle, dto.ValueLabel, dto.Value2Label) = metric switch
            {
                "funnel-month" => ("Sales funnel by location", $"Bookings and quotations made in {monthName}", "Bookings", "Quotations"),
                "staff" => ($"Quotations: {subject ?? $"User #{q.UserId}"}", "Last 6 months", "Quotations", "Converted"),
                "inquiries-type" => ($"Tour inquiries: {InquiryTypeLabels.First(t => t.Type == type).Label}",
                    "Last 6 months, all branches", "Inquiries", null),
                "renewals-month" => ("Leases ending by location", $"Confirmed leases ending in {monthName}", "Leases ending", "Monthly value"),
                _ => ("New-booking revenue by location", $"Invoiced on bookings made in {monthName}", "Revenue", (string?)null)
            };
            return ApiResponse.Ok(dto);
        }

        // ------------------------------------------------------------------ level 3: records

        public async Task<ApiResponse> GetRecordsAsync(string kind, SalesManagerRecordsQuery q, SalesManagerScope scope)
        {
            kind = (kind ?? "").Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(q.Period) && !TryPeriod(q.Period, out _, out _)) return ApiResponse.Fail(PeriodError);
            return kind switch
            {
                "quotations" => scope.NoLocation
                    ? ApiResponse.Fail("locationId=none is not available for quotations.")
                    : await QuotationsAsync(q, scope),
                "bookings" => await BookingsAsync(q, scope),
                "inquiries" => await InquiriesAsync(q),
                "leases" => await LeasesAsync(q, scope),
                _ => ApiResponse.Fail($"Unknown records list '{kind}'. Use quotations, bookings, inquiries or leases.")
            };
        }

        private async Task<ApiResponse> QuotationsAsync(SalesManagerRecordsQuery q, SalesManagerScope scope)
        {
            var stage = (q.Stage ?? "").Trim().ToLowerInvariant();
            if (stage.Length == 0) stage = "quotations";
            if (stage is not ("quotations" or "accepted" or "converted")) return ApiResponse.Fail("stage must be quotations, accepted or converted.");
            if (!TryWindow(q, out var from, out var to, out var window, out var error)) return ApiResponse.Fail(error!);
            var userId = q.UserId is > 0 ? q.UserId : null;

            var s = await _repo.GetQuotationRecordsAsync(scope.LocationIdsCsv, from, to, userId, stage, MaxRows);
            var rows = Set(s, 1).Select(r => new CeoQuotationRecordDto
            {
                QuotationId = Int(r, "QuotationId"), QuotationNumber = Str(r, "QuotationNumber"), Customer = Str(r, "Customer"),
                Location = Str(r, "Location"), CreatedBy = Str(r, "CreatedBy"), Status = Str(r, "Status"), Total = Dec(r, "Total"),
                CreatedOn = Utc(r, "CreatedOn")
            }).ToList();

            var parts = new List<string>();
            if (userId != null) parts.Add($"by {rows.FirstOrDefault()?.CreatedBy ?? $"user #{userId}"}");
            parts.Add($"created {window}");
            if (scope.LocationId != null && rows.FirstOrDefault()?.Location is { } loc) parts.Add(loc);
            var title = stage switch { "accepted" => "Accepted quotations", "converted" => "Converted quotations", _ => "Quotations" };
            return Records(title, parts, Total(s), rows);
        }

        private async Task<ApiResponse> BookingsAsync(SalesManagerRecordsQuery q, SalesManagerScope scope)
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
                    if (int.TryParse(status, NumberStyles.Integer, En, out var id) && id > 0) statusIds = id.ToString(En);
                    else return ApiResponse.Fail("status must be all, active, pending, confirmed, cancelled or a booking status id.");
                    break;
            }

            var s = await _repo.GetBookingRecordsAsync(scope.LocationIdsCsv, scope.NoLocation, from, to, statusIds, running, _clock.Now, MaxRows);
            var rows = Set(s, 1).Select(r => new CeoBookingRecordDto
            {
                BookingId = Int(r, "BookingId"), Customer = Str(r, "Customer"), Space = Str(r, "Space"), Location = Str(r, "Location"),
                StartOn = Day(r, "StartOn"), EndOn = Day(r, "EndOn"), Status = Str(r, "Status"), TotalAmount = Dec(r, "TotalAmount")
            }).ToList();

            var parts = new List<string> { $"made {window}" };
            if (status.Length > 0) parts.Insert(0, status);
            if (scope.NoLocation) parts.Add("no location");
            else if (scope.LocationId != null && rows.FirstOrDefault()?.Location is { } loc) parts.Add(loc);
            return Records(status.Length == 0 ? "New bookings" : "Bookings", parts, Total(s), rows);
        }

        private async Task<ApiResponse> InquiriesAsync(SalesManagerRecordsQuery q)
        {
            string? type = null;
            if (!string.IsNullOrWhiteSpace(q.Type))
            {
                type = InquiryType(q.Type);
                if (type == null) return ApiResponse.Fail(TypeError);
            }
            string? outcome = null;
            if (!string.IsNullOrWhiteSpace(q.Outcome))
            {
                outcome = q.Outcome.Trim().ToLowerInvariant().Replace('-', '_');
                if (!Outcomes.Contains(outcome)) return ApiResponse.Fail("outcome must be followup_due, not_interested, converted or open.");
            }
            var reason = string.IsNullOrWhiteSpace(q.Reason) ? null : q.Reason.Trim();
            if (reason is { Length: > 1000 }) return ApiResponse.Fail("reason can be at most 1000 characters.");

            // Follow-ups due are "right now", whenever the inquiry came in: no date window unless one is asked for.
            DateTime? from = null, to = null;
            string? window = null;
            var noWindow = outcome == "followup_due" && string.IsNullOrWhiteSpace(q.Month)
                           && string.IsNullOrWhiteSpace(q.From) && string.IsNullOrWhiteSpace(q.To);
            if (!noWindow)
            {
                if (!TryWindow(q, out from, out to, out var w, out var error)) return ApiResponse.Fail(error!);
                window = w;
            }

            var s = await _repo.GetInquiryRecordsAsync(from, to, type, outcome, reason, _clock.Now, MaxRows);
            var rows = Set(s, 1).Select(r => new SalesManagerInquiryRecordDto
            {
                InquiryId = Int(r, "InquiryId"), Name = Str(r, "Name"), Phone = Str(r, "Phone"), Type = Str(r, "InquiryType"),
                ReferenceName = Str(r, "ReferenceName"), Status = InquiryStatus(Int(r, "StatusId")), CreatedOn = Utc(r, "CreatedOn"),
                Feedback = Feedback(Str(r, "FeedbackOutcome"), Str(r, "FeedbackReason")), FollowUpOn = Day(r, "FollowUpOn")
            }).ToList();

            var parts = new List<string>();
            if (type != null) parts.Add(InquiryTypeLabels.First(t => t.Type == type).Label);
            if (outcome != null)
                parts.Add(outcome switch
                {
                    "followup_due" => "follow-up due",
                    "followed_up" => "future prospects",
                    "not_interested" => reason == null ? "not interested" : $"not interested: {reason}",
                    "converted" => "converted",
                    _ => "open"
                });
            else if (reason != null) parts.Add($"reason: {reason}");
            if (window != null) parts.Add($"received {window}");
            return Records("Tour inquiries", parts, Total(s), rows);
        }

        private async Task<ApiResponse> LeasesAsync(SalesManagerRecordsQuery q, SalesManagerScope scope)
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

            var s = await _repo.GetLeaseRecordsAsync(scope.LocationIdsCsv, scope.NoLocation, from, to, ExpiringDays, _clock.Now, MaxRows);
            var rows = Set(s, 1).Select(Lease).ToList();

            var parts = new List<string> { window };
            if (scope.NoLocation) parts.Add("no location");
            else if (scope.LocationId != null && rows.FirstOrDefault()?.Location is { } loc) parts.Add(loc);
            return Records("Leases", parts, Total(s), rows);
        }

        // ------------------------------------------------------------------ helpers

        private const string PeriodError = "period must be month, quarter or year.";
        private const string MonthError = "month must be yyyy-MM (e.g. 2026-10).";
        private const string TypeError = "type must be Website, WhatsApp, WalkIn, Reference or NotSet.";

        /// <summary>Void / cancelled invoice StatusIds, as the CEO dashboard (Cancelled + legacy 5), CSV.</summary>
        private async Task<string> VoidStatusesAsync()
        {
            var st = await _orderStatus.GetInvoiceStatusesAsync();
            return string.Join(",", st.Cancelled.Append(5).Distinct());
        }

        private static SalesManagerLeaseDto Lease(IDictionary<string, object?> r) => new()
        {
            BookingId = Int(r, "BookingId"), Customer = Str(r, "Customer"), Space = Str(r, "Space"), Location = Str(r, "Location"),
            EndOn = Day(r, "EndOn"), MonthlyAmount = Dec(r, "MonthlyAmount"), DaysLeft = Int(r, "DaysLeft")
        };

        private static bool TryPeriod(string? value, out string period, out int months)
        {
            period = string.IsNullOrWhiteSpace(value) ? "month" : value.Trim().ToLowerInvariant();
            months = period switch { "month" => 1, "quarter" => 3, "year" => 12, _ => 0 };
            return months > 0;
        }

        /// <summary>Start of the current period (calendar month / quarter / year), as the procedures.</summary>
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

        private static string PeriodLabel(string period, DateTime now) => period switch
        {
            "year" => now.Year.ToString(En),
            "quarter" => $"Q{(now.Month - 1) / 3 + 1} {now.Year}",
            _ => now.ToString("MMMM yyyy", En)
        };

        private static bool TryMonth(string? value, out DateTime month) =>
            DateTime.TryParseExact((value ?? "").Trim(), "yyyy-MM", En, DateTimeStyles.None, out month);

        /// <summary>from / to as yyyy-MM-dd, both days included (to is returned exclusive).</summary>
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

        /// <summary>
        /// Created-in window for quotations, bookings and inquiries: month (that month), else from / to (yyyy-MM-dd,
        /// both days included), else the period to date (default this month). <paramref name="label"/> describes it.
        /// </summary>
        private bool TryWindow(SalesManagerRecordsQuery q, out DateTime? from, out DateTime? to, out string label, out string? error)
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
            TryPeriod(q.Period, out var period, out _);
            from = PeriodStart(period);
            label = period switch
            {
                "year" => $"in {from.Value.Year}",
                "quarter" => $"in Q{(from.Value.Month - 1) / 3 + 1} {from.Value.Year}",
                _ => $"in {from.Value.ToString("MMMM yyyy", En)}"
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
        private static object? Val(IDictionary<string, object?>? r, string key) => r != null && r.TryGetValue(key, out var v) ? v : null;
        private static int Int(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToInt32(v) : 0;
        private static decimal Dec(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Math.Round(Convert.ToDecimal(v), 2) : 0m;
        private static string? Str(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToString(v, En) : null;
        private static string? Day(IDictionary<string, object?>? r, string key) => Val(r, key) switch
        {
            DateTime d => d.ToString("yyyy-MM-dd", En),
            DateOnly d => d.ToString("yyyy-MM-dd", En),
            _ => null
        };
        /// <summary>CreatedOn / CreatedDate columns are written in UTC (SYSUTCDATETIME), so they get a Z (as the CEO dashboard).</summary>
        private static string? Utc(IDictionary<string, object?>? r, string key) => Val(r, key) is DateTime d ? d.ToString("yyyy-MM-ddTHH:mm:ss'Z'", En) : null;
        private static decimal Pct(int part, int total) => total > 0 ? Math.Round(part * 100m / total, 1) : 0m;
    }
}
