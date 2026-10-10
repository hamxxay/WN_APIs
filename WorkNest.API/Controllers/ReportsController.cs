using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Services;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// The weekly super admin report (also e-mailed every week by WeeklyReportService). Super admin only.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "super_admin,SuperAdmin")]
    public class ReportsController : ControllerBase
    {
        private readonly IWeeklyReportGenerator _weekly;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(IWeeklyReportGenerator weekly, IConfiguration configuration, ILogger<ReportsController> logger)
        {
            _weekly = weekly;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>The weekly report as it would be e-mailed right now (text/html, for a browser tab).</summary>
        [HttpGet("api/reports/weekly/preview")]
        public async Task<IActionResult> Preview()
        {
            var report = await _weekly.BuildAsync();
            return Content(report.Html, "text/html; charset=utf-8");
        }

        /// <summary>
        /// Builds the weekly report and e-mails it now to the active super admins and Reports:Weekly:ExtraRecipients.
        /// Does not count as the scheduled weekly run. Returns { isSuccessful, message, recipients }.
        /// </summary>
        [HttpPost("api/reports/weekly/send-now")]
        public async Task<IActionResult> SendNow(CancellationToken ct)
        {
            var options = WeeklyReportService.ReadOptions(_configuration);
            var recipients = await _weekly.GetRecipientsAsync(options.ExtraRecipients);
            if (recipients.Count == 0)
                return Ok(new { isSuccessful = false, message = "There is no super admin e-mail address to send the report to.", recipients });

            var report = await _weekly.BuildAsync();
            var result = await _weekly.SendAsync(report, recipients, ct);
            if (!result.IsSuccessful)
                _logger.LogWarning("Weekly report (send now) failed for every recipient: {Error}", result.Error);
            return Ok(new { isSuccessful = result.IsSuccessful, message = result.Message, recipients = result.Recipients, failed = result.Failed });
        }
    }
}
