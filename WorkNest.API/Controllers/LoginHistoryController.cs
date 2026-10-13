using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.LoginHistory;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Read-only Login History controller.
    /// Strictly visible and accessible ONLY to the super_admin role.
    /// Non-super_admin users (admin, sales_executive, general) receive 403 Forbidden.
    /// </summary>
    [ApiController]
    [Authorize(Roles = Roles.SuperAdmin + ",SuperAdmin")]
    public class LoginHistoryController : ControllerBase
    {
        private readonly ILoginHistoryRepository _repository;
        private readonly ILogger<LoginHistoryController> _logger;

        public LoginHistoryController(ILoginHistoryRepository repository, ILogger<LoginHistoryController> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves a paginated list of login history records based on filters.
        /// Supports both JSON API response (for front-end SPA) and server-rendered HTML page (for direct MVC access).
        /// </summary>
        [HttpGet("api/login-history")]
        [HttpGet("admin/login-history")]
        public async Task<IActionResult> GetLoginHistory([FromQuery] LoginHistoryFilterDto filter, CancellationToken ct)
        {
            var result = await _repository.GetPagedLoginHistoryAsync(filter, ct);

            // If requested via the /admin/login-history route or accepting text/html, serve the HTML page
            var accept = Request.Headers.Accept.ToString();
            if (Request.Path.StartsWithSegments("/admin/login-history") || 
                (accept.Contains("text/html") && !accept.Contains("application/json")))
            {
                var html = RenderHtmlPage(result, filter);
                return Content(html, "text/html", Encoding.UTF8);
            }

            return Ok(new
            {
                isSuccessful = true,
                data = result
            });
        }

        /// <summary>
        /// Retrieves summary statistics for login attempts today in Asia/Karachi time.
        /// </summary>
        [HttpGet("api/login-history/stats")]
        public async Task<IActionResult> GetStats(CancellationToken ct)
        {
            var stats = await _repository.GetStatsAsync(ct);
            return Ok(new
            {
                isSuccessful = true,
                data = stats
            });
        }

        private string RenderHtmlPage(LoginHistoryPagedResultDto data, LoginHistoryFilterDto filter)
        {
            var sb = new StringBuilder();

            // Safe HTML encoding helper: strictly encodes all dynamic text, never Html.Raw
            static string H(string? val) => WebUtility.HtmlEncode(val ?? string.Empty);

            var fromDateStr = filter.FromDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            var toDateStr = filter.ToDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            var emailStr = filter.Email ?? string.Empty;
            var eventTypeStr = filter.EventType ?? string.Empty;
            var ipStr = filter.IpAddress ?? string.Empty;
            var latestOnlyChecked = filter.LatestOnly ? "checked" : string.Empty;

            sb.Append($@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Login History - WorkNest Admin</title>
    <style>
        :root {{
            --bg-body: #0b1120;
            --bg-card: #131c31;
            --bg-input: #1a2542;
            --border-color: #243256;
            --text-main: #f1f5f9;
            --text-muted: #94a3b8;
            --primary: #3b82f6;
            --primary-hover: #2563eb;
            --badge-login-bg: rgba(16, 185, 129, 0.15);
            --badge-login-text: #34d399;
            --badge-failed-bg: rgba(239, 68, 68, 0.15);
            --badge-failed-text: #f87171;
            --badge-logout-bg: rgba(148, 163, 184, 0.15);
            --badge-logout-text: #cbd5e1;
        }}
        * {{ box-sizing: border-box; margin: 0; padding: 0; }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
            background-color: var(--bg-body);
            color: var(--text-main);
            padding: 24px;
            font-size: 14px;
            line-height: 1.5;
        }}
        .container {{ max-width: 1400px; margin: 0 auto; }}
        .header {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 24px;
        }}
        .title h1 {{ font-size: 24px; font-weight: 700; color: #fff; }}
        .title p {{ color: var(--text-muted); font-size: 13px; margin-top: 4px; }}
        .badge-role {{
            display: inline-flex;
            align-items: center;
            gap: 6px;
            padding: 6px 12px;
            background: rgba(59, 130, 246, 0.15);
            color: #60a5fa;
            border: 1px solid rgba(59, 130, 246, 0.3);
            border-radius: 9999px;
            font-size: 12px;
            font-weight: 600;
        }}
        
        /* Summary Strip */
        .summary-strip {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
            gap: 16px;
            margin-bottom: 24px;
        }}
        .summary-card {{
            background: var(--bg-card);
            border: 1px solid var(--border-color);
            border-radius: 10px;
            padding: 16px 20px;
        }}
        .summary-label {{ font-size: 12px; color: var(--text-muted); text-transform: uppercase; font-weight: 600; letter-spacing: 0.5px; }}
        .summary-val {{ font-size: 28px; font-weight: 700; margin-top: 6px; color: #fff; }}
        .summary-val.emerald {{ color: #34d399; }}
        .summary-val.rose {{ color: #f87171; }}
        .summary-val.sky {{ color: #38bdf8; }}

        /* Filter Card */
        .filter-card {{
            background: var(--bg-card);
            border: 1px solid var(--border-color);
            border-radius: 10px;
            padding: 20px;
            margin-bottom: 24px;
        }}
        .filter-form {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
            gap: 16px;
            align-items: flex-end;
        }}
        .form-group {{ display: flex; flex-direction: column; gap: 6px; }}
        .form-group label {{ font-size: 12px; font-weight: 600; color: var(--text-muted); }}
        .form-control {{
            background: var(--bg-input);
            border: 1px solid var(--border-color);
            border-radius: 6px;
            padding: 8px 12px;
            color: #fff;
            font-size: 13px;
            outline: none;
            width: 100%;
        }}
        .form-control:focus {{ border-color: var(--primary); }}
        .filter-actions {{ display: flex; gap: 10px; align-items: center; }}
        .btn {{
            padding: 8px 16px;
            border-radius: 6px;
            font-weight: 600;
            font-size: 13px;
            cursor: pointer;
            border: none;
            transition: all 0.2s;
            text-decoration: none;
            display: inline-flex;
            align-items: center;
            justify-content: center;
        }}
        .btn-primary {{ background: var(--primary); color: #fff; }}
        .btn-primary:hover {{ background: var(--primary-hover); }}
        .btn-secondary {{ background: var(--bg-input); color: var(--text-muted); border: 1px solid var(--border-color); }}
        .btn-secondary:hover {{ color: #fff; border-color: #3b82f6; }}
        .checkbox-group {{ display: flex; align-items: center; gap: 8px; cursor: pointer; padding-bottom: 8px; }}
        .checkbox-group input {{ width: 16px; height: 16px; cursor: pointer; }}

        /* Table Card */
        .table-card {{
            background: var(--bg-card);
            border: 1px solid var(--border-color);
            border-radius: 10px;
            overflow: hidden;
        }}
        .table-responsive {{ overflow-x: auto; width: 100%; }}
        table {{
            width: 100%;
            border-collapse: collapse;
            text-align: left;
        }}
        th {{
            background: rgba(11, 17, 32, 0.4);
            padding: 12px 16px;
            font-size: 12px;
            font-weight: 600;
            color: var(--text-muted);
            border-bottom: 1px solid var(--border-color);
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }}
        td {{
            padding: 14px 16px;
            border-bottom: 1px solid var(--border-color);
            color: var(--text-main);
            vertical-align: middle;
        }}
        tr:hover td {{ background: rgba(255, 255, 255, 0.02); }}
        .badge {{
            display: inline-block;
            padding: 3px 8px;
            border-radius: 4px;
            font-size: 11px;
            font-weight: 700;
            letter-spacing: 0.5px;
        }}
        .badge-login {{ background: var(--badge-login-bg); color: var(--badge-login-text); }}
        .badge-failed {{ background: var(--badge-failed-bg); color: var(--badge-failed-text); }}
        .badge-logout {{ background: var(--badge-logout-bg); color: var(--badge-logout-text); }}
        .device-info {{
            max-width: 260px;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            cursor: help;
        }}
        .reason-badge {{
            color: #fca5a5;
            background: rgba(239, 68, 68, 0.1);
            padding: 2px 6px;
            border-radius: 4px;
            font-size: 11px;
        }}
        
        /* Pagination */
        .pagination-bar {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 14px 20px;
            background: rgba(11, 17, 32, 0.3);
            border-top: 1px solid var(--border-color);
        }}
        .page-info {{ color: var(--text-muted); font-size: 13px; }}
        .page-links {{ display: flex; gap: 6px; align-items: center; }}
        .page-link {{
            padding: 6px 12px;
            border-radius: 6px;
            background: var(--bg-input);
            border: 1px solid var(--border-color);
            color: var(--text-main);
            text-decoration: none;
            font-size: 12px;
            font-weight: 600;
        }}
        .page-link.active {{ background: var(--primary); border-color: var(--primary); color: #fff; }}
        .page-link:hover:not(.active) {{ border-color: var(--primary); }}
        .empty-state {{
            padding: 48px;
            text-align: center;
            color: var(--text-muted);
        }}
    </style>
</head>
<body>
<div class=""container"">
    <div class=""header"">
        <div class=""title"">
            <h1>Login History</h1>
            <p>Authentication audit log &middot; Asia/Karachi (PKT, UTC+5)</p>
        </div>
        <div>
            <span class=""badge-role"">Super Admin Only &middot; Read-only</span>
        </div>
    </div>

    <!-- Summary Strip -->
    <div class=""summary-strip"">
        <div class=""summary-card"">
            <div class=""summary-label"">Logins Today</div>
            <div class=""summary-val emerald"">{data.Stats.LoginsToday}</div>
        </div>
        <div class=""summary-card"">
            <div class=""summary-label"">Failed Attempts Today</div>
            <div class=""summary-val rose"">{data.Stats.FailedToday}</div>
        </div>
        <div class=""summary-card"">
            <div class=""summary-label"">Distinct Users Today</div>
            <div class=""summary-val sky"">{data.Stats.DistinctUsersToday}</div>
        </div>
    </div>

    <!-- Filter Card -->
    <div class=""filter-card"">
        <form method=""get"" action="""" class=""filter-form"">
            <div class=""form-group"">
                <label for=""fromDate"">From Date</label>
                <input type=""date"" id=""fromDate"" name=""FromDate"" class=""form-control"" value=""{H(fromDateStr)}"">
            </div>
            <div class=""form-group"">
                <label for=""toDate"">To Date (Inclusive)</label>
                <input type=""date"" id=""toDate"" name=""ToDate"" class=""form-control"" value=""{H(toDateStr)}"">
            </div>
            <div class=""form-group"">
                <label for=""email"">Email (Contains)</label>
                <input type=""text"" id=""email"" name=""Email"" class=""form-control"" placeholder=""Search email..."" value=""{H(emailStr)}"">
            </div>
            <div class=""form-group"">
                <label for=""eventType"">Event Type</label>
                <select id=""eventType"" name=""EventType"" class=""form-control"">
                    <option value="""">All Events</option>
                    <option value=""LOGIN"" {(eventTypeStr == "LOGIN" ? "selected" : "")}>LOGIN</option>
                    <option value=""FAILED"" {(eventTypeStr == "FAILED" ? "selected" : "")}>FAILED</option>
                    <option value=""LOGOUT"" {(eventTypeStr == "LOGOUT" ? "selected" : "")}>LOGOUT</option>
                </select>
            </div>
            <div class=""form-group"">
                <label for=""ipAddress"">IP Address</label>
                <input type=""text"" id=""ipAddress"" name=""IpAddress"" class=""form-control"" placeholder=""Filter IP..."" value=""{H(ipStr)}"">
            </div>
            <div class=""form-group"">
                <label class=""checkbox-group"">
                    <input type=""checkbox"" name=""LatestOnly"" value=""true"" {latestOnlyChecked}>
                    <span>Last login per user</span>
                </label>
            </div>
            <div class=""filter-actions"">
                <button type=""submit"" class=""btn btn-primary"">Search</button>
                <a href=""?"" class=""btn btn-secondary"">Reset</a>
            </div>
        </form>
    </div>

    <!-- Results Table -->
    <div class=""table-card"">
        <div class=""table-responsive"">
            <table>
                <thead>
                    <tr>
                        <th>Event Time (PKT)</th>
                        <th>Email</th>
                        <th>Event</th>
                        <th>Failure Reason</th>
                        <th>IP Address</th>
                        <th>Browser / Device</th>
                    </tr>
                </thead>
                <tbody>");

            if (data.Items.Count == 0)
            {
                sb.Append(@"<tr><td colspan=""6"" class=""empty-state"">No login events match the criteria.</td></tr>");
            }
            else
            {
                foreach (var item in data.Items)
                {
                    var badgeClass = item.EventType.ToUpperInvariant() switch
                    {
                        "LOGIN" => "badge-login",
                        "FAILED" => "badge-failed",
                        "LOGOUT" => "badge-logout",
                        _ => "badge-logout"
                    };

                    var timeFormatted = item.EventAt.ToString("yyyy-MM-dd HH:mm:ss");
                    var tooltip = H(item.UserAgent ?? "Unknown User Agent");

                    sb.Append("<tr>");
                    sb.Append($"<td>{H(timeFormatted)}</td>");
                    sb.Append($"<td>{H(item.Email ?? "Unknown")}</td>");
                    sb.Append($"<td><span class=\"badge {badgeClass}\">{H(item.EventType)}</span></td>");
                    sb.Append(item.FailureReason != null 
                        ? $"<td><span class=\"reason-badge\">{H(item.FailureReason)}</span></td>" 
                        : "<td><span style=\"color: var(--text-muted);\">-</span></td>");
                    sb.Append($"<td><code>{H(item.IpAddress ?? "-")}</code></td>");
                    sb.Append($"<td><div class=\"device-info\" title=\"{tooltip}\">{H(item.DeviceSummary)}</div></td>");
                    sb.Append("</tr>");
                }
            }

            sb.Append(@"
                </tbody>
            </table>
        </div>

        <!-- Pagination -->
        <div class=""pagination-bar"">
            <div class=""page-info"">
                Showing <strong>");
            sb.Append(data.Items.Count > 0 ? ((data.Page - 1) * data.PageSize + 1) : 0);
            sb.Append("</strong> to <strong>");
            sb.Append((data.Page - 1) * data.PageSize + data.Items.Count);
            sb.Append("</strong> of <strong>");
            sb.Append(data.TotalCount);
            sb.Append("</strong> events");
            sb.Append("</div>");

            // Build query parameters for page links
            string BuildPageUrl(int pageNum)
            {
                var queryParams = new List<string> { $"Page={pageNum}", $"PageSize={data.PageSize}" };
                if (!string.IsNullOrWhiteSpace(fromDateStr)) queryParams.Add($"FromDate={Uri.EscapeDataString(fromDateStr)}");
                if (!string.IsNullOrWhiteSpace(toDateStr)) queryParams.Add($"ToDate={Uri.EscapeDataString(toDateStr)}");
                if (!string.IsNullOrWhiteSpace(emailStr)) queryParams.Add($"Email={Uri.EscapeDataString(emailStr)}");
                if (!string.IsNullOrWhiteSpace(eventTypeStr)) queryParams.Add($"EventType={Uri.EscapeDataString(eventTypeStr)}");
                if (!string.IsNullOrWhiteSpace(ipStr)) queryParams.Add($"IpAddress={Uri.EscapeDataString(ipStr)}");
                if (filter.LatestOnly) queryParams.Add("LatestOnly=true");
                return "?" + string.Join("&", queryParams);
            }

            sb.Append("<div class=\"page-links\">");
            if (data.Page > 1)
            {
                sb.Append($"<a href=\"{H(BuildPageUrl(data.Page - 1))}\" class=\"page-link\">&laquo; Prev</a>");
            }

            int startPage = Math.Max(1, data.Page - 2);
            int endPage = Math.Min(data.TotalPages, data.Page + 2);
            if (endPage == 0) endPage = 1;

            for (int p = startPage; p <= endPage; p++)
            {
                var activeClass = p == data.Page ? "active" : string.Empty;
                sb.Append($"<a href=\"{H(BuildPageUrl(p))}\" class=\"page-link {activeClass}\">{p}</a>");
            }

            if (data.Page < data.TotalPages)
            {
                sb.Append($"<a href=\"{H(BuildPageUrl(data.Page + 1))}\" class=\"page-link\">Next &raquo;</a>");
            }
            sb.Append("</div>");

            sb.Append(@"
        </div>
    </div>
</div>
</body>
</html>");

            return sb.ToString();
        }
    }
}
