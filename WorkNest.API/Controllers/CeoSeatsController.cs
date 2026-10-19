using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Filters;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// CEO dashboard, Seats section (SEAT_01): read-only, same roles and parameters as api/ceo/rooms (period = month |
    /// quarter | year | custom with from / to, yyyy-MM-dd, at most 366 days, not after today; locationId narrows to one
    /// location). Bad parameters answer 400. Before the database update SEAT_01: 200 with available = false and a message.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "ceo,super_admin,SuperAdmin")]
    [CeoAllowed]
    public class CeoSeatsController : ControllerBase
    {
        private readonly ICeoSeatsService _seats;

        public CeoSeatsController(ICeoSeatsService seats)
        {
            _seats = seats;
        }

        /// <summary>
        /// Seats of the rooms sold by the month (private offices, shared spaces): seats sold now (a seat is sold when any
        /// shift on it is sold) against room occupancy, revenue per seat against the list price, rent not earned on the
        /// empty seats, by location / space type / room, in the period and over the last 12 months.
        /// </summary>
        [HttpGet("api/ceo/seats")]
        public async Task<IActionResult> Seats([FromQuery] string? period, [FromQuery] int? locationId,
            [FromQuery] string? from = null, [FromQuery] string? to = null)
        {
            var result = await _seats.GetSeatsAsync(period, locationId, from, to);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }
    }
}
