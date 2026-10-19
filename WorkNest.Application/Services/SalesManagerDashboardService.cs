using System.Globalization;
using WorkNest.Application.DTOs.Contact;
using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Sales Manager dashboard (api/sales-manager/*): read-only, the caller's locations (super admins: all). No money:
    /// counts and desks only (desks of a booking / quotation = its Capacity, else the Capacity of its space; meeting
    /// rooms count 0). The numbers come from the dbo.WN_SalesMgr_* procedures (SM_01, desks from SM_02), which count
    /// with the same rules and business clock as the CEO dashboard (dbo.WN_Ceo_*), so the counts match it for the same
    /// location and period. Tour inquiries belong to their branch (WN_Contacts.LocationId, BR_01) and follow the same
    /// scope as complaints (BR_02): the chosen location's inquiries, or with no single location chosen the locations'
    /// inquiries plus those without a branch (super admins: every inquiry). Before BR_02 has run the procedures count
    /// every branch's inquiries (inquiriesAllBranches = true). Level 1 = dashboard, level 2 = breakdown, level 3 =
    /// records. The Complaints section (SM_03) is a separate call, so the rest of the dashboard keeps working without it:
    /// complaints have no location column, they are matched to a location by their branch.
    /// Period: month | quarter | year (to date) or, since CS_01, custom: a day range (from / to on the dashboard and the
    /// complaints; periodFrom / periodTo on the drill-down, where from / to narrow a list), compared with the same number
    /// of days just before it (<see cref="DashboardPeriod"/>). Only a custom range reaches the procedures as @From / @To.
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

        private static readonly string[] Metrics = { "funnel-month", "staff", "inquiries-type", "renewals-month", "desks-month" };

        private static readonly string[] Outcomes = { "followup_due", "not_interested", "converted", "open", "followed_up" };

        private readonly ISalesManagerDashboardRepository _repo;
        private readonly IBusinessClock _clock;

        public SalesManagerDashboardService(ISalesManagerDashboardRepository repo, IBusinessClock clock)
        {
            _repo = repo;
            _clock = clock;
        }

        // ------------------------------------------------------------------ level 1: dashboard

        public async Task<ApiResponse> GetDashboardAsync(string? period, SalesManagerScope scope, string? from = null, string? to = null)
        {
            if (!DashboardPeriod.TryParse(period, from, to, _clock.Today, out var p, out var periodError)) return ApiResponse.Fail(periodError!);
            var now = _clock.Now;
            var s = await _repo.GetDashboardAsync(scope.LocationIdsCsv, scope.PickerCsv, scope.IncludeNoBranch, p.Months, now, ExpiringDays,
                p.From, p.To);

            var funnel = Set(s, 1).FirstOrDefault();
            var inq = Set(s, 5).FirstOrDefault();
            var ren = Set(s, 9).FirstOrDefault();
            var quotations = Int(funnel, "Quotations");
            var bookings = Int(funnel, "Bookings");
            var bookedDesks = Int(funnel, "BookedDesks");
            // Conversion = quotations created in the period that were booked / quotations created in it (SM_04 columns).
            // Before SM_04 ran: the per-location "converted" counts of the same quotations; the previous period capped at 100%.
            var quotationsPrev = Int(funnel, "QuotationsPrev");
            var quotationsBooked = Has(funnel, "QuotationsBooked") ? Int(funnel, "QuotationsBooked")
                : Math.Min(Set(s, 4).Sum(r => Int(r, "Converted")), quotations);
            var quotationsBookedPrev = Has(funnel, "QuotationsBookedPrev") ? Int(funnel, "QuotationsBookedPrev")
                : Math.Min(Int(funnel, "BookingsPrev"), quotationsPrev);
            var bookedDesksPrev = Int(funnel, "BookedDesksPrev");
            var endedDesks = Int(ren, "EndedDesks");
            var byType = Set(s, 6).ToDictionary(r => Str(r, "InquiryType") ?? "NotSet", r => Int(r, "Inquiries"), StringComparer.OrdinalIgnoreCase);

            var dto = new SalesManagerDashboardDto
            {
                Period = p.Key,
                PeriodLabel = p.Label(now),
                GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", En),
                From = p.FromText,
                To = p.ToText,
                PreviousFrom = p.PreviousFromText,
                PreviousTo = p.PreviousToText,
                InquiriesAllBranches = !Flag(inq, "ByBranch"),
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
                    ConversionRate = Pct(quotationsBooked, quotations),
                    ConversionRatePrev = Pct(quotationsBookedPrev, quotationsPrev),
                    QuotationsBooked = quotationsBooked,
                    QuotationsBookedPrev = quotationsBookedPrev,
                    QuotedDesks = Int(funnel, "QuotedDesks"),
                    BookedDesks = bookedDesks,
                    BookedDesksPrev = bookedDesksPrev,
                    Months = Set(s, 2).Select(r => new SalesManagerFunnelMonthDto
                    {
                        Month = Str(r, "Month") ?? "", Quotations = Int(r, "Quotations"), Accepted = Int(r, "Accepted"),
                        Bookings = Int(r, "Bookings"), BookedDesks = Int(r, "BookedDesks"),
                        QuotationsBooked = Has(r, "QuotationsBooked") ? Int(r, "QuotationsBooked") : Math.Min(Int(r, "Bookings"), Int(r, "Quotations"))
                    }).ToList()
                },
                Team = new SalesManagerTeamDto
                {
                    People = Set(s, 3).Select(r => new SalesManagerPersonDto
                    {
                        UserId = Int(r, "UserId"), Name = Str(r, "Name") ?? "", Role = Str(r, "RoleName") ?? "",
                        Locations = Str(r, "Locations"), InquiriesHandled = Int(r, "InquiriesHandled"),
                        Quotations = Int(r, "Quotations"), Accepted = Int(r, "Accepted"), Converted = Int(r, "Converted"),
                        DesksSold = Int(r, "DesksSold"), ConversionRate = Pct(Int(r, "Converted"), Int(r, "Quotations")),
                        FollowUpsDue = Int(r, "FollowUpsDue")
                    }).ToList(),
                    PerLocation = Set(s, 4).Select(r => new SalesManagerTeamLocationDto
                    {
                        LocationId = Int(r, "LocationId"), Name = Str(r, "Name") ?? "", SalesExecutives = Int(r, "SalesExecutives"),
                        Quotations = Int(r, "Quotations"), Converted = Int(r, "Converted"), DesksSold = Int(r, "DesksSold")
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
                    ExpiringDesks = Int(ren, "ExpiringDesks"),
                    Expiring30Count = Int(ren, "Expiring30Count"),
                    Expiring30Desks = Int(ren, "Expiring30Desks"),
                    EndedDesks = endedDesks,
                    NetDesks = bookedDesks - endedDesks,
                    NetDesksPrev = bookedDesksPrev - Int(ren, "EndedDesksPrev"),
                    Months = Set(s, 11).Select(r => new SalesManagerRenewalMonthDto
                    {
                        Month = Str(r, "Month") ?? "", BookedDesks = Int(r, "BookedDesks"), EndingDesks = Int(r, "EndingDesks"),
                        Expiring = Int(r, "Expiring")
                    }).ToList()
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
            // The breakdowns are by month or the last 6 months: a custom period only has to be valid.
            if (!DashboardPeriod.TryParse(q.Period, q.PeriodFrom, q.PeriodTo, _clock.Today, out _, out var periodError))
                return ApiResponse.Fail(periodError!);

            DateTime? month = null;
            if (metric is "funnel-month" or "renewals-month" or "desks-month")
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

            var s = await _repo.GetBreakdownAsync(metric, scope.LocationIdsCsv, scope.IncludeNoBranch, month, type,
                metric == "staff" ? q.UserId : null, _clock.Now);
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
                        Value = Int(r, "Value"),
                        Value2 = Val(r, "Value2") != null ? Int(r, "Value2") : null,
                        Count = Val(r, "Count") is { } c ? Convert.ToInt32(c) : null
                    };
                }).ToList()
            };
            var monthName = month?.ToString("MMMM yyyy", En);
            (dto.Title, dto.Subtitle, dto.ValueLabel, dto.Value2Label) = metric switch
            {
                "funnel-month" => ("Desks booked by location", $"Bookings made in {monthName}", "Desks booked", "Bookings"),
                "staff" => ($"Quotations: {subject ?? $"User #{q.UserId}"}", "Last 6 months", "Quotations", "Desks sold"),
                "inquiries-type" => ($"Tour inquiries: {InquiryTypeLabels.First(t => t.Type == type).Label}",
                    Flag(head, "InqByBranch") ? "Last 6 months" : "Last 6 months, all branches", "Inquiries", null),
                "renewals-month" => ("Leases ending by location", $"Confirmed leases ending in {monthName}", "Leases ending", "Desks"),
                _ => ("Desks booked and ending by location", $"Desks booked and desks on leases ending in {monthName}", "Desks booked",
                    (string?)"Desks ending")
            };
            return ApiResponse.Ok(dto);
        }

        // ------------------------------------------------------------------ level 3: records

        public async Task<ApiResponse> GetRecordsAsync(string kind, SalesManagerRecordsQuery q, SalesManagerScope scope)
        {
            kind = (kind ?? "").Trim().ToLowerInvariant();
            if (!DashboardPeriod.TryParse(q.Period, q.PeriodFrom, q.PeriodTo, _clock.Today, out _, out var periodError))
                return ApiResponse.Fail(periodError!);
            return kind switch
            {
                "quotations" => scope.NoLocation
                    ? ApiResponse.Fail("locationId=none is not available for quotations.")
                    : await QuotationsAsync(q, scope),
                "bookings" => await BookingsAsync(q, scope),
                "inquiries" => await InquiriesAsync(q, scope),
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
            var rows = Set(s, 1).Select(r => new SalesManagerQuotationRecordDto
            {
                QuotationId = Int(r, "QuotationId"), QuotationNumber = Str(r, "QuotationNumber"), Customer = Str(r, "Customer"),
                Location = Str(r, "Location"), CreatedBy = Str(r, "CreatedBy"), Status = Str(r, "Status"), Desks = Int(r, "Desks"),
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
            var rows = Set(s, 1).Select(r => new SalesManagerBookingRecordDto
            {
                BookingId = Int(r, "BookingId"), Customer = Str(r, "Customer"), Space = Str(r, "Space"), Location = Str(r, "Location"),
                StartOn = Day(r, "StartOn"), EndOn = Day(r, "EndOn"), Status = Str(r, "Status"), Desks = Int(r, "Desks")
            }).ToList();

            var parts = new List<string> { $"made {window}" };
            if (status.Length > 0) parts.Insert(0, status);
            if (scope.NoLocation) parts.Add("no location");
            else if (scope.LocationId != null && rows.FirstOrDefault()?.Location is { } loc) parts.Add(loc);
            return Records(status.Length == 0 ? "New bookings" : "Bookings", parts, Total(s), rows);
        }

        /// <summary>
        /// Tour inquiry records in scope (BR_02): the chosen location's; with no single location chosen the locations'
        /// plus those without a branch; locationId=none (super admins): only those without a branch.
        /// </summary>
        private async Task<ApiResponse> InquiriesAsync(SalesManagerRecordsQuery q, SalesManagerScope scope)
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

            var s = await _repo.GetInquiryRecordsAsync(scope.NoLocation ? "0" : scope.LocationIdsCsv, scope.NoLocation || scope.IncludeNoBranch,
                from, to, type, outcome, reason, _clock.Now, MaxRows);
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
            if (!Flag(Set(s, 0).FirstOrDefault(), "ByBranch")) parts.Add("all branches");   // BR_02 not run yet
            else if (scope.NoLocation) parts.Add("no branch");
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

        // ------------------------------------------------------------------ complaints section (SM_03)

        private const int OldestComplaints = 8;
        private const string NoBranchLabel = "No branch";

        private static readonly string[] ComplaintRecordStatuses =
            { "open", "in_progress", "resolved", "closed", "active", "resolved_period", "new_period", "reopened_period" };

        /// <summary>
        /// Complaints in scope: the manager's locations matched by branch name, plus the complaints without a branch
        /// (WhatsApp) when no single location is chosen; super admins: all (or the chosen location's branch).
        /// </summary>
        public async Task<ApiResponse> GetComplaintsAsync(string? period, SalesManagerScope scope, string? from = null, string? to = null)
        {
            if (!DashboardPeriod.TryParse(period, from, to, _clock.Today, out var p, out var periodError)) return ApiResponse.Fail(periodError!);
            var now = _clock.Now;
            var s = await _repo.GetComplaintsAsync(scope.LocationIdsCsv, scope.IncludeNoBranch, p.Months, now, OldestComplaints, p.From, p.To);

            var sum = Set(s, 0).FirstOrDefault();
            var open = Int(sum, "OpenCount");
            var inProgress = Int(sum, "InProgressCount");
            var dto = new SalesManagerComplaintsDto
            {
                Period = p.Key,
                PeriodLabel = p.Label(now),
                From = p.FromText,
                To = p.ToText,
                PreviousFrom = p.PreviousFromText,
                PreviousTo = p.PreviousToText,
                Status = new SalesManagerComplaintStatusDto
                {
                    Open = open, InProgress = inProgress, Resolved = Int(sum, "ResolvedCount"), Closed = Int(sum, "ClosedCount")
                },
                ActiveTotal = open + inProgress,
                NewInPeriod = Int(sum, "NewInPeriod"),
                NewInPeriodPrev = Int(sum, "NewInPeriodPrev"),
                ResolvedInPeriod = Int(sum, "ResolvedInPeriod"),
                ReopenedInPeriod = Int(sum, "ReopenedInPeriod"),
                AvgResolutionDays = Val(sum, "AvgResolutionDays") is { } avg ? Math.Round(Convert.ToDecimal(avg, En), 1) : null,
                ByCategory = Set(s, 1).Select(r => new SalesManagerComplaintGroupDto
                {
                    Category = Str(r, "Category") ?? "Other", Open = Int(r, "OpenCount"), InProgress = Int(r, "InProgressCount"),
                    Total = Int(r, "Total")
                }).ToList(),
                ByBranch = Set(s, 2).Select(r =>
                {
                    var branch = Str(r, "Branch")?.Trim() ?? "";
                    return new SalesManagerComplaintBranchDto
                    {
                        Branch = branch, Label = branch.Length == 0 ? NoBranchLabel : branch, Open = Int(r, "OpenCount"),
                        InProgress = Int(r, "InProgressCount"), Total = Int(r, "Total")
                    };
                }).ToList(),
                Months = Set(s, 3).Select(r => new SalesManagerComplaintMonthDto
                {
                    Month = Str(r, "Month") ?? "", Created = Int(r, "Created"), Resolved = Int(r, "Resolved")
                }).ToList(),
                OldestActive = Set(s, 4).Select(r => new SalesManagerComplaintDto
                {
                    ComplaintId = Int(r, "ComplaintId"), ComplaintNo = Str(r, "ComplaintNo"), CustomerName = Str(r, "CustomerName"),
                    Category = Str(r, "Category"), Branch = Str(r, "Branch"), Status = Str(r, "Status"),
                    CreatedOn = Local(r, "CreatedOn"), AgeDays = Int(r, "AgeDays")
                }).ToList()
            };
            return ApiResponse.Ok(dto);
        }

        /// <summary>
        /// Complaint records, newest first, at most 200 (total = all that match). Filters: status (also active and the
        /// period ones), category ("Other" also = none), branch ("none" = no branch), month (created in it).
        /// </summary>
        public async Task<ApiResponse> GetComplaintRecordsAsync(SalesManagerComplaintRecordsQuery q, SalesManagerScope scope)
        {
            if (!DashboardPeriod.TryParse(q.Period, q.PeriodFrom, q.PeriodTo, _clock.Today, out var p, out var periodError))
                return ApiResponse.Fail(periodError!);
            string? status = null;
            if (!string.IsNullOrWhiteSpace(q.Status))
            {
                status = ComplaintRecordStatus(q.Status);
                if (status == null)
                    return ApiResponse.Fail("status must be open, in_progress, resolved, closed, active, resolved_period, new_period or reopened_period.");
            }
            var category = string.IsNullOrWhiteSpace(q.Category) ? null : q.Category.Trim();
            if (category is { Length: > 100 }) return ApiResponse.Fail("category can be at most 100 characters.");
            string? branch = null;
            if (!string.IsNullOrWhiteSpace(q.Branch))
            {
                branch = q.Branch.Trim();
                if (IsNoBranch(branch)) branch = "";
                else if (branch.Length > 100) return ApiResponse.Fail("branch can be at most 100 characters.");
            }
            DateTime? month = null;
            if (!string.IsNullOrWhiteSpace(q.Month))
            {
                if (!TryMonth(q.Month, out var m)) return ApiResponse.Fail(MonthError);
                month = m;
            }

            // A custom range matters only to the *_period statuses: the other lists do not need CS_01.
            var periodBound = status is "resolved_period" or "new_period" or "reopened_period";
            var s = await _repo.GetComplaintRecordsAsync(scope.LocationIdsCsv, scope.IncludeNoBranch, status, category, branch, month,
                p.Months, _clock.Now, MaxRows, periodBound ? p.From : null, periodBound ? p.To : null);
            var rows = Set(s, 1).Select(r => new SalesManagerComplaintRecordDto
            {
                ComplaintId = Int(r, "ComplaintId"), ComplaintNo = Str(r, "ComplaintNo"), CustomerName = Str(r, "CustomerName"),
                Phone = Str(r, "Phone"), Category = Str(r, "Category"), Branch = Str(r, "Branch"), Status = Str(r, "Status"),
                Source = Str(r, "Source"), IsFollowUp = Val(r, "IsFollowUp") is { } f && Convert.ToBoolean(f, En),
                CreatedOn = Local(r, "CreatedOn"), UpdatedOn = Local(r, "UpdatedOn"), AgeDays = Int(r, "AgeDays"),
                Description = Str(r, "Description")
            }).ToList();

            var window = PeriodWords(p);
            var parts = new List<string>();
            if (status != null)
                parts.Add(status switch
                {
                    "in_progress" => "in progress",
                    "resolved" => "resolved, awaiting the customer",
                    "active" => "open or in progress",
                    "resolved_period" => $"resolved {window}",
                    "new_period" => $"received {window}",
                    "reopened_period" => $"reopened {window}",
                    _ => status
                });
            if (category != null) parts.Add(category);
            if (branch != null) parts.Add(branch.Length == 0 ? "no branch" : branch);
            else if (scope.LocationId != null && rows.FirstOrDefault()?.Branch is { } loc) parts.Add(loc);
            if (month != null) parts.Add($"received in {month.Value.ToString("MMMM yyyy", En)}");
            return Records("Complaints", parts, Total(s), rows);
        }

        /// <summary>A records/complaints status (spaces, dashes and case ignored; new = open), null when unknown.</summary>
        private static string? ComplaintRecordStatus(string value)
        {
            var s = value.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
            s = s switch { "new" => "open", "inprogress" => "in_progress", _ => s };
            return ComplaintRecordStatuses.Contains(s) ? s : null;
        }

        private static bool IsNoBranch(string value) =>
            new string(value.Where(char.IsLetter).ToArray()).ToLowerInvariant() is "none" or "nobranch";

        /// <summary>
        /// "in October 2026" / "in Q4 2026" / "in 2026": the current period, as the records titles say it; a custom one as
        /// "between 1 Oct 2026 and 15 Oct 2026".
        /// </summary>
        private string PeriodWords(DashboardPeriod p)
        {
            if (p.IsCustom) return p.BetweenWords();
            var period = p.Key;
            var from = PeriodStart(period);
            return period switch
            {
                "year" => $"in {from.Year}",
                "quarter" => $"in Q{(from.Month - 1) / 3 + 1} {from.Year}",
                _ => $"in {from.ToString("MMMM yyyy", En)}"
            };
        }

        // ------------------------------------------------------------------ helpers

        private const string MonthError = "month must be yyyy-MM (e.g. 2026-10).";
        private const string TypeError = "type must be Website, WhatsApp, WalkIn, Reference or NotSet.";

        private static SalesManagerLeaseDto Lease(IDictionary<string, object?> r) => new()
        {
            BookingId = Int(r, "BookingId"), Customer = Str(r, "Customer"), Space = Str(r, "Space"), Location = Str(r, "Location"),
            EndOn = Day(r, "EndOn"), Desks = Int(r, "Desks"), DaysLeft = Int(r, "DaysLeft")
        };

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
        /// both days included), else the period: to date (default this month), or a custom period's range (periodFrom /
        /// periodTo, already checked by the caller). <paramref name="label"/> describes it.
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
            if (!DashboardPeriod.TryParse(q.Period, q.PeriodFrom, q.PeriodTo, _clock.Today, out var p, out error)) return false;
            if (p.IsCustom)
            {
                from = p.From; to = p.To!.Value.AddDays(1); label = p.BetweenWords();
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
        /// <summary>A bit column; false when it is missing (e.g. a procedure from before BR_02).</summary>
        private static bool Flag(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v && Convert.ToBoolean(v, En);
        private static string? Str(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToString(v, En) : null;
        private static string? Day(IDictionary<string, object?>? r, string key) => Val(r, key) switch
        {
            DateTime d => d.ToString("yyyy-MM-dd", En),
            DateOnly d => d.ToString("yyyy-MM-dd", En),
            _ => null
        };
        /// <summary>CreatedOn / CreatedDate columns are written in UTC (SYSUTCDATETIME), so they get a Z (as the CEO dashboard).</summary>
        private static string? Utc(IDictionary<string, object?>? r, string key) => Val(r, key) is DateTime d ? d.ToString("yyyy-MM-ddTHH:mm:ss'Z'", En) : null;
        /// <summary>Business wall-clock times (complaints: SYSDATETIME, Pakistan time): no offset, as the API writes them elsewhere.</summary>
        private static string? Local(IDictionary<string, object?>? r, string key) => Val(r, key) is DateTime d ? d.ToString("yyyy-MM-ddTHH:mm:ss", En) : null;
        private static decimal Pct(int part, int total) => total > 0 ? Math.Round(part * 100m / total, 1) : 0m;
    }
}
