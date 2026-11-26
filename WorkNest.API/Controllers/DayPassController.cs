using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.DayPass;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Day Pass (docs/superpowers/specs/2026-11-26-day-pass-api-contract.md). Location scope exactly as TransferController:
    /// location-bound roles see and create only their locations (an empty list = nothing).
    /// Until Database/Booking/DP_01_day_pass.txt has run every endpoint answers 503.
    /// </summary>
    [ApiController]
    [Route("api/day-passes")]
    [Authorize(Roles = "sales_executive,SalesExecutive,admin,Admin,super_admin,SuperAdmin")]
    [ValidateLocationScope]
    public class DayPassController : ControllerBase, IAsyncActionFilter
    {
        private readonly IDayPassService _service;

        public DayPassController(IDayPassService service) { _service = service; }

        [NonAction]
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (!await _service.IsInstalledAsync())
            {
                context.Result = new ObjectResult(ApiResponse.Fail(DayPassRules.NotInstalledMessage)) { StatusCode = 503 };
                return;
            }
            await next();
        }

        private IReadOnlyCollection<int>? Allowed => User.IsLocationBoundRole() ? User.GetLocationIds() : null;

        private IActionResult Respond<T>(DayPassResult<T> r) =>
            r.Ok ? StatusCode(r.StatusCode, ApiResponse.Ok(r.Data, r.Message)) : StatusCode(r.StatusCode, ApiResponse.Fail(r.Message));

        /// <summary>Spaces with something free for the date and hours: seats free, whole space free, list price.</summary>
        [HttpGet("available-spaces")]
        public async Task<IActionResult> AvailableSpaces([FromQuery] string? date, [FromQuery] string? window, [FromQuery] int? locationId) =>
            Respond(await _service.AvailableSpacesAsync(date, window, locationId, Allowed));

        /// <summary>Price, tax and seats of a request (nothing saved); errors[] lists what is wrong, availability included.</summary>
        [HttpPost("quote")]
        public async Task<IActionResult> Quote([FromBody] DayPassRequest? request) =>
            Respond(await _service.QuoteAsync(request, Allowed));

        /// <summary>Creates the pass: booking, pass, paid invoice, receipt, history.</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DayPassRequest? request) =>
            Respond(await _service.CreateAsync(request, User.GetEmail(), Allowed));

        /// <summary>The list and History view: date range, status (active | used | cancelled | all), location, search; with totals.</summary>
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? status, [FromQuery] int? locationId,
            [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
            Respond(await _service.ListAsync(new DayPassListFilter { From = from, To = to, Status = status ?? "all", LocationId = locationId, Search = search, Page = page, PageSize = pageSize }, Allowed));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id) => Respond(await _service.GetAsync(id, Allowed));

        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, [FromBody] DayPassCancelRequest? body) =>
            Respond(await _service.CancelAsync(id, body?.Reason, User.GetEmail(), Allowed));

        [HttpPost("{id:int}/email-receipt")]
        public async Task<IActionResult> EmailReceipt(int id)
        {
            var r = await _service.EmailReceiptAsync(id, User.GetEmail(), Allowed);
            return r.Ok ? Ok(new { message = r.Message }) : StatusCode(r.StatusCode, ApiResponse.Fail(r.Message));
        }

        [HttpGet("{id:int}/receipt-pdf")]
        public async Task<IActionResult> ReceiptPdf(int id)
        {
            var r = await _service.ReceiptPdfAsync(id, Allowed);
            return r.Ok ? File(r.Data!, "application/pdf", $"day-pass-receipt-{id}.pdf") : StatusCode(r.StatusCode, ApiResponse.Fail(r.Message));
        }

        /// <summary>Customer picker (location scoped): code, name, company, email, phone, cnic.</summary>
        [HttpGet("customers")]
        public async Task<IActionResult> Customers([FromQuery] string? search)
        {
            var r = await _service.CustomersAsync(search, Allowed);
            return r.Ok ? Ok(ApiResponse.Ok(r.Data, r.Message)) : StatusCode(r.StatusCode, ApiResponse.Fail(r.Message));
        }
    }
}
