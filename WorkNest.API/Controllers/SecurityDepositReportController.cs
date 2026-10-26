using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.Reports;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;
using Microsoft.AspNetCore.RateLimiting;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Security deposit report. Location-bound admins see only the deposits whose booking is in their locations (summary,
    /// export, detail) and only their customers (customer-lookup); a customer of other locations answers 404 on detail.
    /// 503 "Run SEC_LOC_02 on the database." for them until that script has run (everyone else: the old procedures).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Route("api/reports/security-deposits")]
    [Authorize(Roles = "admin,super_admin,Administrator,SuperAdmin,1,2")]
    public class SecurityDepositReportController : ControllerBase
    {
        private readonly ISecurityDepositReportService _reportService;

        public SecurityDepositReportController(ISecurityDepositReportService reportService)
        {
            _reportService = reportService;
        }

        /// <summary>The caller's locations (location-bound staff); null = every location.</summary>
        private List<int>? Scope => User.ScopedLocationIds(null);

        private async Task<IActionResult> Scoped(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (LocationScopeUpdateMissingException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(ex.Message));
            }
        }

        [HttpGet("")]
        [HttpGet("summary")]
        public Task<IActionResult> Index([FromQuery] SecurityDepositReportFilterDto filter) => Scoped(async () =>
        {
            var result = await _reportService.GetCustomerSummaryAsync(filter, Scope);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        });

        [HttpGet("detail/{customerId:int}")]
        [RecordScope(RecordKind.Customer, "customerId")]
        public Task<IActionResult> Detail(
            int customerId,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null) => Scoped(async () =>
        {
            var result = await _reportService.GetCustomerDetailAsync(customerId, fromDate, toDate, Scope);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        });

        [EnableRateLimiting("pdf")]
        [HttpGet("detail/{customerId:int}/export")]
        [RecordScope(RecordKind.Customer, "customerId")]
        public Task<IActionResult> ExportCustomerDetailExcel(
            int customerId,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null) => Scoped(async () =>
        {
            var fileBytes = await _reportService.ExportCustomerDetailExcelAsync(customerId, fromDate, toDate, Scope);
            var fileName = $"Customer_{customerId}_Deposit_History_{WorkNest.Application.Services.BusinessClock.Default.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        });

        [EnableRateLimiting("pdf")]
        [HttpGet("export")]
        public Task<IActionResult> ExportExcel([FromQuery] SecurityDepositReportFilterDto filter) => Scoped(async () =>
        {
            var fileBytes = await _reportService.ExportExcelAsync(filter, Scope);
            var fileName = $"Security_Deposit_Report_{WorkNest.Application.Services.BusinessClock.Default.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        });

        [HttpGet("customer-lookup")]
        public Task<IActionResult> CustomerLookup() => Scoped(async () =>
        {
            var result = await _reportService.GetCustomerLookupAsync(Scope);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        });
    }
}
