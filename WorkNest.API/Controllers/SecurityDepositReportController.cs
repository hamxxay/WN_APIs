using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Reports;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
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

        [HttpGet("")]
        [HttpGet("summary")]
        public async Task<IActionResult> Index([FromQuery] SecurityDepositReportFilterDto filter)
        {
            var result = await _reportService.GetCustomerSummaryAsync(filter);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        [HttpGet("detail/{customerId:int}")]
        public async Task<IActionResult> Detail(
            int customerId,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            var result = await _reportService.GetCustomerDetailAsync(customerId, fromDate, toDate);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        [HttpGet("detail/{customerId:int}/export")]
        public async Task<IActionResult> ExportCustomerDetailExcel(
            int customerId,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            var fileBytes = await _reportService.ExportCustomerDetailExcelAsync(customerId, fromDate, toDate);
            var fileName = $"Customer_{customerId}_Deposit_History_{WorkNest.Application.Services.BusinessClock.Default.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        [HttpGet("export")]
        public async Task<IActionResult> ExportExcel([FromQuery] SecurityDepositReportFilterDto filter)
        {
            var fileBytes = await _reportService.ExportExcelAsync(filter);
            var fileName = $"Security_Deposit_Report_{WorkNest.Application.Services.BusinessClock.Default.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        [HttpGet("customer-lookup")]
        public async Task<IActionResult> CustomerLookup()
        {
            var result = await _reportService.GetCustomerLookupAsync();
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }
    }
}
