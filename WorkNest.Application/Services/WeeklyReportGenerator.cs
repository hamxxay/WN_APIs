using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Reports;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// The weekly super admin report: per location and company total, occupancy and active bookings now, invoiced and
    /// collected in the last 7 days, outstanding and overdue now (same rules as the location comparison on the super
    /// admin dashboard: collected = PaidTotal of the invoices issued in the period; outstanding = GrandTotal - WHT -
    /// PaidTotal of open invoices), tour inquiries received in the last 7 days, running leases expiring in the next
    /// 30 / 60 days and the 3-month forecast. Rendered as an e-mail-safe HTML page (tables, inline CSS only).
    /// Sending is done by WeeklyReportService (scheduled) and ReportsController (send now).
    /// </summary>
    public class WeeklyReportGenerator : IWeeklyReportGenerator
    {
        private readonly IRoleDashboardRepository _dashboardRepo;
        private readonly IReportRepository _repo;
        private readonly IForecastService _forecast;
        private readonly IOrderStatusService _orderStatus;
        private readonly IBusinessClock _clock;
        private readonly IEmailService _email;
        private readonly ILogger<WeeklyReportGenerator> _logger;

        public WeeklyReportGenerator(IRoleDashboardRepository dashboardRepo, IReportRepository repo, IForecastService forecast,
            IOrderStatusService orderStatus, IBusinessClock clock, IEmailService email, ILogger<WeeklyReportGenerator> logger)
        {
            _dashboardRepo = dashboardRepo;
            _repo = repo;
            _forecast = forecast;
            _orderStatus = orderStatus;
            _clock = clock;
            _email = email;
            _logger = logger;
        }

        /// <summary>ISO week of a business date, e.g. "2026-W42" (the key of one weekly run).</summary>
        public static string PeriodKeyFor(DateTime date) =>
            $"{ISOWeek.GetYear(date)}-W{ISOWeek.GetWeekOfYear(date):00}";

        // --- row helpers ---
        private static object? Val(IDictionary<string, object?>? r, string key) => r != null && r.TryGetValue(key, out var v) ? v : null;
        private static int Int(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToInt32(v) : 0;
        private static decimal Dec(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Math.Round(Convert.ToDecimal(v), 2) : 0m;
        private static string? Str(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToString(v) : null;
        private static decimal Pct(int part, int total) => total > 0 ? Math.Round(part * 100m / total, 1) : 0m;
        private static List<IDictionary<string, object?>> Set(List<List<IDictionary<string, object?>>> s, int i) => i < s.Count ? s[i] : new();

        public async Task<WeeklyReportDto> BuildAsync()
        {
            var now = _clock.Now;
            var weekStart = now.AddDays(-7);

            var st = await _orderStatus.GetInvoiceStatusesAsync();
            var open = st.Unpaid.Concat(st.Partial).Concat(st.Overdue).ToList();
            var voids = st.Cancelled.Append(5).ToList();

            var cmp = await _dashboardRepo.GetLocationComparisonAsync(weekStart, now, open, voids);
            var extras = await _repo.GetWeeklyExtrasAsync(weekStart, now);
            var forecast = await _forecast.GetAsync(null, ForecastService.DefaultMonths);

            var expiring = Set(extras, 1).ToDictionary(r => Int(r, "LocationId"), r => (d30: Int(r, "Expiring30"), d60: Int(r, "Expiring60")));
            var report = new WeeklyReportDto
            {
                PeriodKey = PeriodKeyFor(now),
                From = weekStart.Date,
                To = now,
                NewInquiries = Int(Set(extras, 0).FirstOrDefault(), "NewInquiries"),
                Forecast = forecast,
                Locations = Set(cmp, 0).Select(r =>
                {
                    var id = Int(r, "Id");
                    var total = Int(r, "TotalSpaces");
                    var occupied = Int(r, "OccupiedSpaces");
                    expiring.TryGetValue(id, out var e);
                    return new WeeklyLocationRowDto
                    {
                        LocationId = id, Name = Str(r, "Name") ?? $"Location #{id}",
                        TotalSpaces = total, OccupiedSpaces = occupied, OccupancyPct = Pct(occupied, total),
                        ActiveBookings = Int(r, "ActiveBookings"),
                        Invoiced = Dec(r, "Invoiced"), Collected = Dec(r, "Collected"),
                        Outstanding = Dec(r, "Outstanding"), Overdue = Dec(r, "Overdue"),
                        Expiring30 = e.d30, Expiring60 = e.d60
                    };
                }).ToList(),
                ExpiringLeases = Set(extras, 2).Select(r =>
                {
                    var end = Val(r, "EndOn") is DateTime d ? d : now;
                    return new ExpiringLeaseDto
                    {
                        BookingId = Int(r, "BookingId"), Customer = Str(r, "Customer"), Space = Str(r, "Space"), Location = Str(r, "Location"),
                        EndOn = end, DaysLeft = Math.Max(0, (int)(end.Date - now.Date).TotalDays), MonthlyRent = Dec(r, "MonthlyRent")
                    };
                }).ToList()
            };

            // Company total = the locations added up (a space without a location is not in any row).
            var t = report.Total;
            foreach (var l in report.Locations)
            {
                t.TotalSpaces += l.TotalSpaces; t.OccupiedSpaces += l.OccupiedSpaces; t.ActiveBookings += l.ActiveBookings;
                t.Invoiced += l.Invoiced; t.Collected += l.Collected; t.Outstanding += l.Outstanding; t.Overdue += l.Overdue;
                t.Expiring30 += l.Expiring30; t.Expiring60 += l.Expiring60;
            }
            t.OccupancyPct = Pct(t.OccupiedSpaces, t.TotalSpaces);

            report.Subject = $"WorkNest weekly report · {Range(report.From, report.To)}";
            report.Html = Render(report);
            return report;
        }

        public async Task<List<string>> GetRecipientsAsync(IEnumerable<string>? extraRecipients)
        {
            var all = (await _repo.GetSuperAdminEmailsAsync()).Concat(extraRecipients ?? Enumerable.Empty<string>());
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var raw in all)
            {
                var e = (raw ?? "").Trim();
                if (e.Length == 0 || !MailAddress.TryCreate(e, out var addr) || addr.Address != e) continue;
                if (seen.Add(e)) list.Add(e);
            }
            return list;
        }

        public async Task<WeeklyReportSendResultDto> SendAsync(WeeklyReportDto report, IReadOnlyCollection<string> recipients, CancellationToken ct = default)
        {
            var result = new WeeklyReportSendResultDto();
            if (recipients.Count == 0)
            {
                result.Status = "Failed";
                result.Error = "No recipients.";
                result.Message = "There is no super admin e-mail address to send the report to.";
                return result;
            }

            string? lastError = null;
            foreach (var to in recipients)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await _email.SendHtmlReportAsync(to, report.Subject, report.Html);
                    result.Recipients.Add(to);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Weekly report {PeriodKey} could not be sent to {Email}.", report.PeriodKey, to);
                    result.Failed.Add(to);
                    lastError = ex.Message;
                }
            }

            result.IsSuccessful = result.Recipients.Count > 0;
            result.Status = result.Failed.Count == 0 ? "Sent" : result.IsSuccessful ? "Partial" : "Failed";
            result.Error = lastError;
            result.Message = result.Status switch
            {
                "Sent" => $"Weekly report sent to {result.Recipients.Count} recipient{(result.Recipients.Count == 1 ? "" : "s")}.",
                "Partial" => $"Weekly report sent to {result.Recipients.Count} recipient{(result.Recipients.Count == 1 ? "" : "s")}; it could not be delivered to {string.Join(", ", result.Failed)}.",
                _ => "The weekly report could not be e-mailed. Please check the e-mail settings and try again."
            };
            return result;
        }

        // ------------------------------------------------------------------ HTML (e-mail safe: tables, inline CSS)

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        private static string Pkr(decimal v) => "PKR " + Math.Round(v, 0).ToString("N0", Inv);
        private static string N(int v) => v.ToString("N0", Inv);
        private static string P(decimal v) => v.ToString("0.#", Inv) + "%";
        private static string D(DateTime d) => d.ToString("d MMM yyyy", Inv);
        private static string Range(DateTime from, DateTime to) =>
            from.Year == to.Year
                ? (from.Month == to.Month ? $"{from:%d}–{to.ToString("d MMM yyyy", Inv)}" : $"{from.ToString("d MMM", Inv)} – {to.ToString("d MMM yyyy", Inv)}")
                : $"{D(from)} – {D(to)}";

        private const string Font = "font-family:Arial,Helvetica,sans-serif;";
        private const string Th = "padding:8px 10px;background:#f1f5f9;color:#475569;font-size:11px;font-weight:bold;text-transform:uppercase;letter-spacing:0.04em;border-bottom:1px solid #e2e8f0;white-space:nowrap;";
        private const string Td = "padding:8px 10px;border-bottom:1px solid #f1f5f9;color:#334155;font-size:13px;white-space:nowrap;";
        private const string Tf = "padding:9px 10px;background:#f8fafc;color:#0f172a;font-size:13px;font-weight:bold;white-space:nowrap;";
        private const string Num = "text-align:right;";

        private static string Section(string title, string sub) =>
            $@"<tr><td style=""padding:22px 24px 8px;{Font}""><div style=""font-size:16px;font-weight:bold;color:#0f172a;"">{H(title)}</div><div style=""font-size:12px;color:#64748b;margin-top:2px;"">{H(sub)}</div></td></tr>";

        private static string Bar(decimal pct, string color) =>
            $@"<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" style=""width:80px;border-collapse:collapse;display:inline-table;vertical-align:middle;""><tr><td style=""height:6px;background:#e2e8f0;border-radius:3px;padding:0;""><div style=""height:6px;width:{Math.Clamp(pct, 0, 100).ToString("0", Inv)}%;background:{color};border-radius:3px;font-size:0;line-height:0;"">&nbsp;</div></td></tr></table>";

        private static string Tile(string label, string value, string sub, string accent) =>
            $@"<td width=""25%"" valign=""top"" style=""padding:6px;""><div style=""border:1px solid #e2e8f0;border-left:3px solid {accent};border-radius:8px;padding:10px 12px;background:#ffffff;{Font}"">
                <div style=""font-size:10px;font-weight:bold;color:#64748b;text-transform:uppercase;letter-spacing:0.05em;"">{H(label)}</div>
                <div style=""font-size:18px;font-weight:bold;color:#0f172a;margin:4px 0 2px;white-space:nowrap;"">{H(value)}</div>
                <div style=""font-size:11px;color:#94a3b8;"">{H(sub)}</div></div></td>";

        private static string Render(WeeklyReportDto r)
        {
            var t = r.Total;
            var sb = new StringBuilder();
            sb.Append($@"<!DOCTYPE html><html><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""><title>{H(r.Subject)}</title></head>
<body style=""margin:0;padding:0;background:#f1f5f9;{Font}"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f1f5f9;border-collapse:collapse;""><tr><td align=""center"" style=""padding:24px 12px;"">
<table role=""presentation"" width=""720"" cellpadding=""0"" cellspacing=""0"" style=""width:100%;max-width:720px;background:#ffffff;border:1px solid #e2e8f0;border-radius:12px;border-collapse:separate;overflow:hidden;"">
<tr><td style=""background:#0d9488;padding:20px 24px;{Font}"">
  <div style=""font-size:12px;color:#ccfbf1;text-transform:uppercase;letter-spacing:0.08em;font-weight:bold;"">WorkNest · Weekly report</div>
  <div style=""font-size:22px;color:#ffffff;font-weight:bold;margin-top:4px;"">{H(Range(r.From, r.To))}</div>
  <div style=""font-size:12px;color:#ccfbf1;margin-top:4px;"">Money for the last 7 days · occupancy, balances and leases as of {H(r.To.ToString("ddd d MMM yyyy, HH:mm", Inv))} (Pakistan time)</div>
</td></tr>");

            // headline tiles
            sb.Append(Section("Company at a glance", "All locations"));
            sb.Append($@"<tr><td style=""padding:0 18px;""><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse:collapse;""><tr>");
            sb.Append(Tile("Occupancy", P(t.OccupancyPct), $"{N(t.OccupiedSpaces)} of {N(t.TotalSpaces)} spaces", "#0d9488"));
            sb.Append(Tile("Active bookings", N(t.ActiveBookings), "running right now", "#0d9488"));
            sb.Append(Tile("Invoiced", Pkr(t.Invoiced), "last 7 days", "#6366f1"));
            sb.Append(Tile("Collected", Pkr(t.Collected), "paid on those invoices", "#10b981"));
            sb.Append("</tr><tr>");
            sb.Append(Tile("Outstanding", Pkr(t.Outstanding), "open invoices now", "#f59e0b"));
            sb.Append(Tile("Overdue", Pkr(t.Overdue), "past the due date", t.Overdue > 0 ? "#ef4444" : "#10b981"));
            sb.Append(Tile("New tour inquiries", N(r.NewInquiries), "last 7 days", "#0ea5e9"));
            sb.Append(Tile("Leases expiring", $"{N(t.Expiring30)} / {N(t.Expiring60)}", "next 30 / 60 days", t.Expiring30 > 0 ? "#f59e0b" : "#10b981"));
            sb.Append("</tr></table></td></tr>");

            // per location
            sb.Append(Section("By location", "Occupancy and balances now · invoiced and collected in the last 7 days"));
            sb.Append($@"<tr><td style=""padding:0 24px;""><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse:collapse;border:1px solid #e2e8f0;{Font}"">
<tr><th align=""left"" style=""{Th}"">Location</th><th align=""left"" style=""{Th}"">Occupancy</th><th style=""{Th}{Num}"">Active</th><th style=""{Th}{Num}"">Invoiced</th><th style=""{Th}{Num}"">Collected</th><th style=""{Th}{Num}"">Outstanding</th><th style=""{Th}{Num}"">Overdue</th><th style=""{Th}{Num}"">Expiring 30 / 60d</th></tr>");
            if (r.Locations.Count == 0)
                sb.Append($@"<tr><td colspan=""8"" style=""{Td}color:#64748b;"">No locations yet.</td></tr>");
            foreach (var l in r.Locations)
                sb.Append(LocationRow(l, Td));
            if (r.Locations.Count > 1)
                sb.Append(LocationRow(t, Tf));
            sb.Append("</table></td></tr>");

            // forecast
            var f = r.Forecast;
            var months = f.Totals;
            sb.Append(Section($"Next {f.Months} months", "Forecast from current leases: rent prorated on a 30-day month, occupancy on each month's last day, no renewals assumed"));
            sb.Append($@"<tr><td style=""padding:0 24px;""><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse:collapse;border:1px solid #e2e8f0;{Font}"">
<tr><th align=""left"" style=""{Th}"">Month</th><th style=""{Th}{Num}"">Expected rent</th><th style=""{Th}{Num}"">Of which confirmed</th><th align=""left"" style=""{Th}"">Occupancy</th><th style=""{Th}{Num}"">Leases ending</th><th style=""{Th}{Num}"">Leases starting</th></tr>");
            var maxRent = Math.Max(1m, months.Count == 0 ? 1m : months.Max(m => m.ExpectedRent));
            foreach (var m in months)
            {
                sb.Append($@"<tr><td style=""{Td}font-weight:bold;color:#0f172a;"">{H(m.Label)}</td>
<td style=""{Td}{Num}"">{Pkr(m.ExpectedRent)}<br>{Bar(m.ExpectedRent * 100m / maxRent, "#6366f1")}</td>
<td style=""{Td}{Num}"">{Pkr(m.ConfirmedRent)}</td>
<td style=""{Td}"">{Bar(m.OccupancyPct, "#0d9488")} <span style=""padding-left:6px;"">{P(m.OccupancyPct)}</span> <span style=""color:#94a3b8;font-size:11px;"">({N(m.OccupiedSpaces)}/{N(m.TotalSpaces)})</span></td>
<td style=""{Td}{Num}{(m.LeasesEnding > 0 ? "color:#b45309;font-weight:bold;" : "")}"">{N(m.LeasesEnding)}</td>
<td style=""{Td}{Num}"">{N(m.LeasesStarting)}{(m.PendingStarting > 0 ? $@" <span style=""color:#94a3b8;font-size:11px;"">({N(m.PendingStarting)} pending)</span>" : "")}</td></tr>");
            }
            sb.Append("</table></td></tr>");

            if (f.Locations.Count > 1)
            {
                sb.Append($@"<tr><td style=""padding:12px 24px 0;""><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse:collapse;border:1px solid #e2e8f0;{Font}""><tr><th align=""left"" style=""{Th}"">Location</th>");
                foreach (var m in months) sb.Append($@"<th style=""{Th}{Num}"">{H(m.Label)}</th>");
                sb.Append("</tr>");
                foreach (var l in f.Locations)
                {
                    sb.Append($@"<tr><td style=""{Td}font-weight:bold;color:#0f172a;"">{H(l.Name)}</td>");
                    foreach (var m in l.Months)
                        sb.Append($@"<td style=""{Td}{Num}"">{Pkr(m.ExpectedRent)}<br><span style=""color:#94a3b8;font-size:11px;"">{P(m.OccupancyPct)} occupied</span></td>");
                    sb.Append("</tr>");
                }
                sb.Append("</table></td></tr>");
            }

            // expiring leases
            sb.Append(Section("Leases expiring soon", $"Running leases ending in the next 60 days ({N(t.Expiring30)} within 30 days, {N(t.Expiring60)} within 60){(t.Expiring60 > r.ExpiringLeases.Count ? $" · first {r.ExpiringLeases.Count} shown" : "")}"));
            sb.Append($@"<tr><td style=""padding:0 24px;""><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse:collapse;border:1px solid #e2e8f0;{Font}"">");
            if (r.ExpiringLeases.Count == 0)
                sb.Append($@"<tr><td style=""{Td}color:#64748b;"">No running lease ends in the next 60 days.</td></tr>");
            else
            {
                sb.Append($@"<tr><th align=""left"" style=""{Th}"">Customer</th><th align=""left"" style=""{Th}"">Space</th><th style=""{Th}{Num}"">Ends</th><th style=""{Th}{Num}"">Days left</th><th style=""{Th}{Num}"">Monthly rent</th></tr>");
                foreach (var e in r.ExpiringLeases)
                {
                    var urgent = e.DaysLeft <= 30;
                    sb.Append($@"<tr><td style=""{Td}white-space:normal;""><strong style=""color:#0f172a;"">{H(e.Customer ?? "Customer")}</strong><br><span style=""color:#94a3b8;font-size:11px;"">Booking #{e.BookingId}</span></td>
<td style=""{Td}white-space:normal;"">{H(e.Space)}{(string.IsNullOrWhiteSpace(e.Location) ? "" : $@"<br><span style=""color:#94a3b8;font-size:11px;"">{H(e.Location)}</span>")}</td>
<td style=""{Td}{Num}"">{H(D(e.EndOn))}</td>
<td style=""{Td}{Num}{(urgent ? "color:#b45309;font-weight:bold;" : "")}"">{N(e.DaysLeft)}</td>
<td style=""{Td}{Num}"">{Pkr(e.MonthlyRent)}</td></tr>");
                }
            }
            sb.Append("</table></td></tr>");

            sb.Append($@"<tr><td style=""padding:22px 24px 20px;{Font}""><div style=""border-top:1px solid #e2e8f0;padding-top:12px;font-size:11px;color:#94a3b8;line-height:1.6;"">
Amounts in PKR. Collected is the amount paid on invoices issued in the last 7 days. Outstanding and overdue leave out withholding tax, which the customer pays to the government.
Expected rent is the monthly rent of current leases (less percentage discounts, before tax), whether invoiced yet or not.<br>
Sent automatically every week by WorkNest to super admins.</div></td></tr>
</table></td></tr></table></body></html>");
            return sb.ToString();
        }

        private static string LocationRow(WeeklyLocationRowDto l, string td) =>
            $@"<tr><td style=""{td}font-weight:bold;color:#0f172a;"">{H(l.Name)}</td>
<td style=""{td}"">{Bar(l.OccupancyPct, "#0d9488")} <span style=""padding-left:6px;"">{P(l.OccupancyPct)}</span> <span style=""color:#94a3b8;font-size:11px;font-weight:normal;"">({N(l.OccupiedSpaces)}/{N(l.TotalSpaces)})</span></td>
<td style=""{td}{Num}"">{N(l.ActiveBookings)}</td>
<td style=""{td}{Num}"">{Pkr(l.Invoiced)}</td>
<td style=""{td}{Num}"">{Pkr(l.Collected)}</td>
<td style=""{td}{Num}"">{Pkr(l.Outstanding)}</td>
<td style=""{td}{Num}{(l.Overdue > 0 ? "color:#b91c1c;font-weight:bold;" : "")}"">{Pkr(l.Overdue)}</td>
<td style=""{td}{Num}"">{N(l.Expiring30)} / {N(l.Expiring60)}</td></tr>";
    }
}
