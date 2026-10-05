using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Attendant;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;

using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")]
    [ValidateLocationScope]
    public class AttendantController : ControllerBase
    {
        private readonly IAttendantService _attendants;
        private readonly IHikEnrollmentService _hikEnrollment;
        private readonly IHikAccessSuspensionService _accessSuspension;
        private readonly IConfiguration _configuration;

        public AttendantController(IAttendantService attendants, IHikEnrollmentService hikEnrollment,
            IHikAccessSuspensionService accessSuspension, IConfiguration configuration)
        {
            _attendants = attendants;
            _hikEnrollment = hikEnrollment;
            _accessSuspension = accessSuspension;
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

            // Apply the tick on the machines: block (untick) / unblock (tick) on the room + Entrance machines.
            var machines = await _hikEnrollment.SetAccessEnabledAsync(request.BookingDetailId, request.PersonId, request.IsEnabled);

            return Ok(new
            {
                message = request.PersonId.HasValue 
                    ? $"Access status toggled to {(request.IsEnabled ? "Enabled" : "Disabled")} for attendant." 
                    : $"Batch access status toggled to {(request.IsEnabled ? "Enabled" : "Disabled")} for {rows} attendants.",
                rowsUpdated = rows,
                machines
            });
        }

        /// <summary>
        /// Enroll a fingerprint for a booking attendant. The terminal prompts for the finger,
        /// then the template is copied to the other selected machines.
        /// </summary>
        [Authorize]
        [HttpPost("api/bookings/{bookingDetailId}/attendants/{personId}/hik/fingerprint")]
        public async Task<IActionResult> EnrollFingerprint(int bookingDetailId, int personId, [FromBody] HikEnrollRequest request)
        {
            var result = await _hikEnrollment.EnrollFingerprintAsync(bookingDetailId, personId, request);
            return result.Ok ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Enroll an RFID card for a booking attendant. The terminal waits for the card to be tapped,
        /// then the card is attached on the other selected machines.
        /// </summary>
        [Authorize]
        [HttpPost("api/bookings/{bookingDetailId}/attendants/{personId}/hik/card")]
        public async Task<IActionResult> EnrollCard(int bookingDetailId, int personId, [FromBody] HikEnrollRequest request)
        {
            var result = await _hikEnrollment.EnrollCardAsync(bookingDetailId, personId, request);
            return result.Ok ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Enroll a face for a booking attendant. The terminal opens its face-capture screen,
        /// then the photo is uploaded to the other selected machines.
        /// </summary>
        [Authorize]
        [HttpPost("api/bookings/{bookingDetailId}/attendants/{personId}/hik/face")]
        public async Task<IActionResult> EnrollFace(int bookingDetailId, int personId, [FromBody] HikEnrollRequest request)
        {
            var result = await _hikEnrollment.EnrollFaceAsync(bookingDetailId, personId, request);
            return result.Ok ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Challan-based access suspension for the booking behind a booked space (overdue unpaid challan).
        /// </summary>
        [HttpGet("api/bookings/{bookingDetailId}/access-suspension")]
        public async Task<IActionResult> GetAccessSuspension(int bookingDetailId)
        {
            var result = await _accessSuspension.GetAccessSuspensionAsync(bookingDetailId);
            return Ok(result);
        }

        /// <summary>
        /// Challans (invoices) of the booking behind a booked space, with paid / overdue status.
        /// </summary>
        [HttpGet("api/bookings/{bookingDetailId}/challans")]
        public async Task<IActionResult> GetBookingChallans(int bookingDetailId)
        {
            var result = await _accessSuspension.GetBookingChallansAsync(bookingDetailId);
            return Ok(result);
        }

        /// <summary>
        /// Extend door access manually until a date while the challan is unpaid (sales executive / admin / super admin).
        /// </summary>
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
        [HttpPost("api/bookings/{bookingDetailId}/access-suspension/extend")]
        public async Task<IActionResult> ExtendAccess(int bookingDetailId, [FromBody] HikAccessExtendRequest request)
        {
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            var result = await _accessSuspension.ExtendAccessAsync(bookingDetailId, request, email);
            return result.Error == null ? Ok(result) : BadRequest(result);
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
