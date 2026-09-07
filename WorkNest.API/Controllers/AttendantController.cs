using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Attendant;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    [ApiController]
    public class AttendantController : ControllerBase
    {
        private readonly IAttendantService _attendants;
        private readonly IConfiguration _configuration;

        public AttendantController(IAttendantService attendants, IConfiguration configuration)
        {
            _attendants = attendants;
            _configuration = configuration;
        }

        /// <summary>
        /// Add attendant to a customer (upsert into WN_Persons, link to WN_CustomerAttendants).
        /// </summary>
        [Authorize]
        [HttpPost("api/attendants")]
        public async Task<IActionResult> AddAttendant([FromBody] CreateAttendantRequest request)
        {
            if (request == null || request.CustomerId <= 0)
                return BadRequest(new { message = "Valid CustomerId is required." });

            var result = await _attendants.AddAttendantAsync(request);
            return StatusCode(201, new
            {
                message = "Attendant successfully added/linked to customer.",
                personId = result.PersonId,
                personGuid = result.PersonGuid
            });
        }

        /// <summary>
        /// Update contact details for an existing person.
        /// </summary>
        [Authorize]
        [HttpPut("api/attendants/{personId}")]
        public async Task<IActionResult> UpdateAttendant(int personId, [FromBody] UpdateAttendantRequest request)
        {
            await _attendants.UpdateAttendantAsync(personId, request);
            return Ok(new { message = "Attendant contact details updated." });
        }

        /// <summary>
        /// Get all active attendants for a given customer.
        /// </summary>
        [Authorize]
        [HttpGet("api/customers/{customerId}/attendants")]
        public async Task<IActionResult> GetCustomerAttendants(int customerId)
        {
            var list = await _attendants.GetCustomerAttendantsAsync(customerId);
            return Ok(list);
        }

        /// <summary>
        /// Get active booked spaces/rooms for a customer.
        /// </summary>
        [Authorize]
        [HttpGet("api/customers/{customerId}/active-spaces")]
        public async Task<IActionResult> GetCustomerActiveSpaces(int customerId)
        {
            var list = await _attendants.GetCustomerActiveSpacesAsync(customerId);
            return Ok(list);
        }

        /// <summary>
        /// Get attendants currently assigned to a specific booking detail.
        /// </summary>
        [Authorize]
        [HttpGet("api/bookings/{bookingDetailId}/attendants")]
        public async Task<IActionResult> GetBookingAttendants(int bookingDetailId)
        {
            var list = await _attendants.GetBookingAttendantsAsync(bookingDetailId);
            return Ok(list);
        }

        /// <summary>
        /// Check room capacity and estimated surcharge before committing an assignment.
        /// </summary>
        [Authorize]
        [HttpGet("api/bookings/{bookingDetailId}/capacity-check")]
        public async Task<IActionResult> CheckCapacity(int bookingDetailId)
        {
            var check = await _attendants.CheckCapacityBeforeAssignAsync(bookingDetailId);
            return Ok(check);
        }

        /// <summary>
        /// Assign an existing attendant to a booking detail.
        /// </summary>
        [Authorize]
        [HttpPost("api/bookings/{bookingDetailId}/attendants")]
        public async Task<IActionResult> AssignToBooking(int bookingDetailId, [FromBody] AssignBookingAttendantRequest request)
        {
            if (request == null) return BadRequest("Invalid request payload.");
            request.BookingDetailId = bookingDetailId;

            var result = await _attendants.AssignAttendantToBookingAsync(request);
            return Ok(new
            {
                message = "Attendant successfully assigned to booking.",
                data = result
            });
        }

        /// <summary>
        /// Soft remove an attendant from a booking detail (sets AssignedTo date and revokes access status).
        /// </summary>
        [Authorize]
        [HttpDelete("api/bookings/{bookingDetailId}/attendants/{personId}")]
        public async Task<IActionResult> SoftRemoveFromBooking(int bookingDetailId, int personId)
        {
            await _attendants.SoftRemoveAttendantFromBookingAsync(bookingDetailId, personId);
            return Ok(new { message = "Attendant soft-removed from booking assignment and access revoked." });
        }

        /// <summary>
        /// Toggle access status (single attendant or batch for entire customer/booking).
        /// </summary>
        [Authorize]
        [HttpPatch("api/access-status")]
        public async Task<IActionResult> ToggleAccessStatus([FromBody] ToggleAccessStatusRequest request)
        {
            if (request == null || request.BookingDetailId <= 0 || request.CustomerId <= 0)
                return BadRequest("BookingDetailId and CustomerId are required.");

            int rows = await _attendants.ToggleAccessStatusAsync(request);
            return Ok(new
            {
                message = request.PersonId.HasValue 
                    ? $"Access status toggled to {(request.IsEnabled ? "Enabled" : "Disabled")} for attendant." 
                    : $"Batch access status toggled to {(request.IsEnabled ? "Enabled" : "Disabled")} for {rows} attendants.",
                rowsUpdated = rows
            });
        }

        /// <summary>
        /// External Export API for Hikvision Fingerprint / Access Control Integration.
        /// Auth Mechanism: Option A — Dedicated API Key Header (X-Hikvision-Api-Key) or Bearer Token.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("api/access-status/export")]
        public async Task<IActionResult> ExportAccessStatus()
        {
            var expectedApiKey = _configuration["Hikvision:ApiKey"] ?? "WN-Hikvision-Secret-Key-2026";
            
            if (Request.Headers.TryGetValue("X-Hikvision-Api-Key", out var apiKeyHeader))
            {
                if (string.Equals(apiKeyHeader, expectedApiKey, StringComparison.Ordinal))
                {
                    var data = await _attendants.GetAccessStatusExportAsync();
                    return Ok(data);
                }
            }

            // Fallback check if caller has a valid JWT token
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var data = await _attendants.GetAccessStatusExportAsync();
                return Ok(data);
            }

            return Unauthorized(new { message = "Invalid or missing X-Hikvision-Api-Key header authentication." });
        }
    }
}
