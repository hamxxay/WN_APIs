using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Discounts section of the CEO dashboard (DD_01): read-only, the CEO and super admins, as the other api/ceo/*
    /// endpoints. Query: period = month (default) | quarter | year | custom (from / to, yyyy-MM-dd; the records take the
    /// dashboard's range as periodFrom / periodTo) and locationId. Bad parameters answer 400. Before the database update
    /// DD_01 has run: the section answers 200 with available = false and a message; the records 503.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "ceo,super_admin,SuperAdmin")]
    [CeoAllowed]
    public class CeoDiscountsController : ControllerBase
    {
        private readonly ICeoDiscountsService _discounts;
        private readonly ILogger<CeoDiscountsController> _logger;

        public CeoDiscountsController(ICeoDiscountsService discounts, ILogger<CeoDiscountsController> logger)
        {
            _discounts = discounts;
            _logger = logger;
        }

        /// <summary>
        /// The discount a month on the rooms' bookings running in the period (below the expected list / shift price, as
        /// the Rooms section, plus fixed PKR discounts), the discount on the invoices issued in it, by location, by
        /// seller and by shift, and the biggest discounts.
        /// </summary>
        [HttpGet("api/ceo/discounts")]
        public Task<IActionResult> Discounts([FromQuery] string? period, [FromQuery] int? locationId,
            [FromQuery] string? from = null, [FromQuery] string? to = null) =>
            Run(() => _discounts.GetDiscountsAsync(period, locationId, from, to));

        /// <summary>
        /// The records behind the section, at most 200 (total = all that match). kind = bookings (the discounted ones,
        /// biggest first; locationId = id | none, sellerId = id | none, shift, newOnly, all) | invoices (the invoices
        /// with a discount issued in the period, newest first; locationId).
        /// </summary>
        [HttpGet("api/ceo/discounts/records/{kind}")]
        public Task<IActionResult> Records(string kind, [FromQuery] CeoDiscountRecordsQuery query) =>
            Run(() => _discounts.GetRecordsAsync(kind, query));

        private async Task<IActionResult> Run(Func<Task<ApiResponse>> action)
        {
            try
            {
                var result = await action();
                return result.IsSuccessful ? Ok(result) : BadRequest(result);
            }
            catch (CeoDiscountsUpdateMissingException ex)
            {
                _logger.LogWarning("CEO discounts called before the database update {Update}: {Error}", CeoDiscountsUpdateMissingException.Update, ex.InnerException?.Message);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(ex.Message));
            }
        }
    }
}
