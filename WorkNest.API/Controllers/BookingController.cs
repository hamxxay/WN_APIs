using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    public class SendChallanEmailRequest
    {
        public string? PdfBase64 { get; set; }
    }
    [ApiController]
    [Authorize]
    [ValidateLocationScope]
    public class BookingController : ControllerBase
    {
        private readonly IBookingService _bookings;
        private readonly IDbRepository _db;
        private readonly IPdfService _pdf;
        public BookingController(IBookingService bookings, IDbRepository db, IPdfService pdf)
        {
            _bookings = bookings;
            _db = db;
            _pdf = pdf;
        }

        // Staff = admin / super admin / sales executive / receptionist; customers (role "general") are not staff.
        private const string StaffRoles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive";

        /// <summary>Signed-in user's email from the JWT only (the x-user-email header is no longer trusted).</summary>
        private string? ResolveUserEmail() => User.GetEmail();

        [HttpGet("api/booking/available-spaces")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailableSpaces(
            [FromQuery] int spaceTypeId,
            [FromQuery] DateTime startOn,
            [FromQuery] DateTime endOn,
            [FromQuery] int? capacity,
            [FromQuery] string? shiftType = "24_7") =>
            Ok(await _bookings.GetAvailableSpacesForBookingAsync(spaceTypeId, startOn, endOn, capacity, shiftType));

        [HttpGet("api/booking/available-spaces-reassignment")]
        [Authorize(Roles = StaffRoles)] // used by the admin reassign dialog only
        public async Task<IActionResult> AvailableForReassignment(
            [FromQuery] int spaceTypeId,
            [FromQuery] DateTime startOn,
            [FromQuery] DateTime endOn,
            [FromQuery] int excludeBookingId,
            [FromQuery] string? shiftType = "24_7") =>
            Ok(await _bookings.GetAvailableSpacesForReassignmentAsync(spaceTypeId, startOn, endOn, excludeBookingId, shiftType));

        [HttpGet("api/booking/smart/available")]
        [AllowAnonymous]
        public async Task<IActionResult> SmartAvailable(
            [FromQuery] string categoryCode,
            [FromQuery] DateTime startOn,
            [FromQuery] DateTime endOn,
            [FromQuery] int? capacity,
            [FromQuery] string? shiftType = "24_7") =>
            Ok(await _bookings.GetSmartAvailableSpacesAsync(categoryCode, startOn, endOn, capacity, shiftType));

        [HttpGet("api/booking/my")]
        public async Task<IActionResult> MyBookings()
        {
            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity required" });
            return Ok(await _bookings.GetMyBookingsAsync(email));
        }

        [HttpGet("api/booking/recent")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")]
        public async Task<IActionResult> Recent([FromQuery] int top = 10) =>
            Ok(await _bookings.GetRecentBookingsAsync(top));

        [HttpGet("api/booking/calendar")]
        [AllowAnonymous]
        public async Task<IActionResult> Calendar(
            [FromQuery] string spaceId,
            [FromQuery] int year,
            [FromQuery] int month)
        {
            if (int.TryParse(spaceId, out var intId))
                return Ok(await _bookings.GetBookingCalendarAsync(intId, year, month));

            if (Guid.TryParse(spaceId, out var guid))
            {
                var (rows, _) = await _db.GetSpacesAsync(1, 10000, null);
                var match = rows.FirstOrDefault(r => (r.TryGetValue("IdGUID", out var idg) && idg?.ToString() == guid.ToString()) || (r.TryGetValue("PublicId", out var g) && g?.ToString() == guid.ToString()));
                if (match is null) return NotFound(new { isSuccessful = false, message = "Space not found" });
                intId = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
                return Ok(await _bookings.GetBookingCalendarAsync(intId, year, month));
            }

            return BadRequest(new { isSuccessful = false, message = "Invalid spaceId" });
        }

        [HttpGet("api/booking")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null,
            [FromQuery] int? locationId = null)
        {
            int? effectiveLocationId = locationId;
            if (User?.Identity?.IsAuthenticated == true && User.IsLocationBoundRole())
            {
                var claimLocId = User.GetLocationId();
                if (claimLocId.HasValue)
                {
                    effectiveLocationId = claimLocId.Value;
                }
            }

            var (items, total) = await _bookings.GetBookingsAsync(page, limit, search, effectiveLocationId);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/booking/{publicId:guid}")]
        public async Task<IActionResult> Get(Guid publicId)
        {
            bool isAdmin = User.IsStaff();
            var email = isAdmin ? null : ResolveUserEmail();
            if (!isAdmin && string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity required" });
            var result = await _bookings.GetBookingByIdAsync(publicId, email);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("api/booking/{id:int}")]
        public async Task<IActionResult> GetDetails(int id)
        {
            bool isAdmin = User.IsStaff();
            var email = isAdmin ? null : ResolveUserEmail();
            if (!isAdmin && string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity required" });
            var result = await _bookings.GetBookingDetailsAsync(id.ToString(), email);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        /// <summary>One booking in the same shape as a row of GET api/booking (used to open the first-invoice window).</summary>
        [HttpGet("api/booking/{id:int}/row")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive,receptionist,Receptionist")]
        public async Task<IActionResult> GetListRow(int id, [FromServices] IDbRepository db)
        {
            var row = await db.GetBookingSummaryRowDbAsync(id);
            return row == null ? NotFound(ApiResponse.Fail($"Booking #{id} not found.")) : Ok(ApiResponse.Ok(row));
        }

        // Staff see every booking; a customer only their own (it used to call GetChallan(id, null), so ownership was never checked).
        [HttpGet("api/booking/{id:int}/billing-summary")]
        public async Task<IActionResult> GetBillingSummary(int id)
        {
            if (!await CanSeeBookingAsync(id))
                return NotFound(ApiResponse.Fail("Booking not found."));
            return await GetChallan(id);
        }

        [HttpGet("api/booking/{id:int}/challan")]
        public async Task<IActionResult> GetChallan(int id)
        {
            bool isAdmin = User.IsStaff();
            var email = isAdmin ? null : ResolveUserEmail();
            if (!isAdmin && string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity required" });
            var result = await _bookings.GetChallanAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            
            if (email != null)
            {
                var dto = result.Data as WorkNest.Application.DTOs.Booking.ChallanResponseDto;
                if (dto == null || !string.Equals(dto.CustomerEmail, email, StringComparison.OrdinalIgnoreCase))
                {
                    return Forbid();
                }
            }
            return Ok(result);
        }

        [HttpPost("api/booking/create-admin")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")]
        public async Task<IActionResult> AdminCreate([FromBody] AdminBookingRequest request)
        {
            if (request == null)
                return BadRequest(new { isSuccessful = false, message = "Booking request payload is required." });

            if ((request.SpaceId ?? 0) == 0 && string.IsNullOrWhiteSpace(request.SpaceIdGuid))
                return BadRequest(new { isSuccessful = false, message = "SpaceId or SpaceIdGuid is required." });

            if ((request.UserId ?? 0) == 0 && string.IsNullOrWhiteSpace(request.UserIdGuid) && string.IsNullOrWhiteSpace(request.CustomerEmail))
                return BadRequest(new { isSuccessful = false, message = "UserId, UserIdGuid, or CustomerEmail is required." });

            var actor = ResolveUserEmail();
            var result = await _bookings.CreateAdminBookingAsync(request, actor);
            if (!result.IsSuccessful)
                return BadRequest(result);

            return StatusCode(201, result);
        }

        [HttpPost("api/booking")]
        public async Task<IActionResult> Create([FromBody] BookingRequest request)
        {
            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity required" });
            
            var result = await _bookings.CreateBookingAsync(request, email);
            if (!result.IsSuccessful && result.Message == "CustomerProfileRequired")
            {
                return BadRequest(result);
            }
            return StatusCode(201, result);
        }

        [HttpPost("api/booking/smart")]
        public async Task<IActionResult> Smart([FromBody] SmartBookingRequest request)
        {
            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity required" });
            
            var result = await _bookings.CreateSmartBookingAsync(request, email);
            if (!result.IsSuccessful && result.Message == "CustomerProfileRequired")
            {
                return BadRequest(result);
            }
            return StatusCode(201, result);
        }

        [HttpPut("api/booking/{id:int}")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> Update(int id, [FromBody] BookingUpdateRequest request) =>
            Ok(await _bookings.UpdateBookingAsync(id, request, null));

        [HttpPut("api/booking/{publicId:guid}")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> UpdateByGuid(Guid publicId, [FromBody] BookingUpdateRequest request)
        {
            var booking = await _db.GetBookingByPublicIdAsync(publicId, null);
            if (booking is null) return NotFound(new { isSuccessful = false, message = "Booking not found" });
            var id = booking.TryGetValue("Id", out var bid) ? Convert.ToInt32(bid) : 0;
            return Ok(await _bookings.UpdateBookingAsync(id, request, null));
        }

        [HttpPatch("api/booking/{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, [FromBody] CancelBookingRequest? request)
        {
            // WN_Bookings_Cancel checks that the booking belongs to this (JWT) email.
            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity required" });
            var result = await _bookings.CancelBookingAsync(id, email, request?.CancelReason);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPatch("api/booking/{publicId:guid}/cancel")]
        public async Task<IActionResult> CancelByGuid(Guid publicId, [FromBody] CancelBookingRequest? request)
        {
            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity required" });
            var booking = await _db.GetBookingByPublicIdAsync(publicId, email);
            if (booking is null) return NotFound(new { isSuccessful = false, message = "Booking not found" });
            var id = booking.TryGetValue("Id", out var bid) ? Convert.ToInt32(bid) : 0;
            var result = await _bookings.CancelBookingAsync(id, email, request?.CancelReason);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPatch("api/booking/{id:int}/status")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] BookingStatusUpdateRequest request) =>
            Ok(await _bookings.UpdateBookingStatusAsync(id, request.StatusId, null));

        [HttpPatch("api/booking/{publicId:guid}/status")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> UpdateStatusByGuid(Guid publicId, [FromBody] BookingStatusUpdateRequest request)
        {
            var booking = await _db.GetBookingByPublicIdAsync(publicId, null);
            if (booking is null) return NotFound(new { isSuccessful = false, message = "Booking not found" });
            var id = booking.TryGetValue("Id", out var bid) ? Convert.ToInt32(bid) : 0;
            return Ok(await _bookings.UpdateBookingStatusAsync(id, request.StatusId, null));
        }

        
        [HttpGet("api/booking/{id:int}/challan-pdf")]
        public async Task<IActionResult> GetChallanPdf(int id)
        {
            if (!await CanSeeBookingAsync(id))
                return NotFound(new { isSuccessful = false, message = "Challan details not found." });
            var challanResult = await _bookings.GetChallanAsync(id);
            if (!challanResult.IsSuccessful || challanResult.Data is null)
                return NotFound(new { isSuccessful = false, message = "Challan details not found." });

            var dto = (ChallanResponseDto)challanResult.Data;
            var pdfBytes = _pdf.GenerateBookingConfirmationPdf(dto);
            var filename = $"Challan-{dto.ChallanNumber ?? id.ToString()}.pdf";
            return File(pdfBytes, "application/pdf", filename);
        }

        [HttpPost("api/booking/{id:int}/send-challan-email")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")]
        public async Task<IActionResult> SendChallanEmail(int id, [FromBody] SendChallanEmailRequest? request)
        {
            byte[]? pdfBytes = null;
            if (!string.IsNullOrWhiteSpace(request?.PdfBase64))
            {
                try { pdfBytes = Convert.FromBase64String(request.PdfBase64); }
                catch { return BadRequest(new { isSuccessful = false, message = "Invalid base64 PDF data." }); }
            }
            var result = await _bookings.SendChallanEmailAsync(id, pdfBytes);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/booking/{id:int}/send-confirmation-email")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")]
        public async Task<IActionResult> SendConfirmationEmail(int id)
        {
            var result = await _bookings.SendBookingConfirmationEmailAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPatch("api/booking/{id:int}/reassign")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> Reassign(int id, [FromBody] ReassignBookingRequest request)
        {
            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity required" });
            var result = await _bookings.ReassignBookingAsync(id, request, email);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPatch("api/booking/{publicId:guid}/reassign")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> ReassignByGuid(Guid publicId, [FromBody] ReassignBookingRequest request)
        {
            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity required" });
            var booking = await _db.GetBookingByPublicIdAsync(publicId, null);
            if (booking is null) return NotFound(new { isSuccessful = false, message = "Booking not found" });
            var id = booking.TryGetValue("Id", out var bid) ? Convert.ToInt32(bid) : 0;
            var result = await _bookings.ReassignBookingAsync(id, request, email);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        // Was open to any logged-in user with amounts taken from the query string: now staff only,
        // month counts capped, and the rent / discount come from the booking in the DB when it has them.
        [HttpGet("api/booking/{id:int}/advance-invoice-pdf")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> GetAdvanceInvoicePdf(
            int id,
            [FromQuery] int advMonths = 3,
            [FromQuery] int secMonths = 2,
            [FromQuery] decimal monthlyRate = 0,
            [FromQuery] decimal discount = 0)
        {
            if (!await CanSeeBookingAsync(id))
                return NotFound(ApiResponse.Fail("Booking not found."));

            advMonths = Math.Clamp(advMonths, 1, 36);
            secMonths = Math.Clamp(secMonths, 0, 36);

            var challan = await _bookings.GetChallanAsync(id);
            if (!challan.IsSuccessful || challan.Data is not ChallanResponseDto booking)
                return NotFound(ApiResponse.Fail("Booking not found."));

            if (booking.MonthlyRent > 0) monthlyRate = booking.MonthlyRent;
            monthlyRate = Math.Max(0m, monthlyRate);
            decimal grossAdvance = monthlyRate * advMonths;
            if (booking.DiscountPercentage > 0)
                discount = Math.Round(grossAdvance * Math.Min(100m, booking.DiscountPercentage) / 100m, 2);
            discount = Math.Clamp(discount, 0m, grossAdvance);

            var pdfBytes = await _bookings.GenerateAdvanceInvoicePdfAsync(id, advMonths, secMonths, monthlyRate, discount);
            return File(pdfBytes, "application/pdf", $"AdvanceInvoice-{id}.pdf");
        }

        [HttpGet("api/booking/{bookingId:int}/financial-breakdown")]
        public async Task<IActionResult> FinancialBreakdown(int bookingId)
        {
            if (!await CanSeeBookingAsync(bookingId))
                return NotFound(ApiResponse.Fail("Booking not found."));
            return Ok(await _bookings.GetBookingFinancialBreakdownAsync(bookingId));
        }

        /// <summary>Staff see every booking; a customer only their own (these were anonymous before).</summary>
        private async Task<bool> CanSeeBookingAsync(int bookingId)
        {
            if (User.IsStaff()) return true;
            var email = User.GetEmail();
            if (string.IsNullOrWhiteSpace(email)) return false;
            var db = HttpContext.RequestServices.GetRequiredService<IDbRepository>();
            return await db.IsBookingOwnedByDbAsync(bookingId, email);
        }
}

}