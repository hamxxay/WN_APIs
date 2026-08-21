using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    public class SendChallanEmailRequest
    {
        public string? PdfBase64 { get; set; }
    }
    [ApiController]
    [Authorize]
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

        private string? ResolveUserEmail(string? headerEmail)
        {
            var claimEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value 
                             ?? User.FindFirst("email")?.Value;
            return !string.IsNullOrWhiteSpace(claimEmail) ? claimEmail : headerEmail;
        }

        [HttpGet("api/booking/available-spaces")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailableSpaces(
            [FromQuery] int spaceTypeId,
            [FromQuery] DateTime startOn,
            [FromQuery] DateTime endOn,
            [FromQuery] int? capacity) =>
            Ok(await _bookings.GetAvailableSpacesForBookingAsync(spaceTypeId, startOn, endOn, capacity));

        [HttpGet("api/booking/available-spaces-reassignment")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailableForReassignment(
            [FromQuery] int spaceTypeId,
            [FromQuery] DateTime startOn,
            [FromQuery] DateTime endOn,
            [FromQuery] int excludeBookingId) =>
            Ok(await _bookings.GetAvailableSpacesForReassignmentAsync(spaceTypeId, startOn, endOn, excludeBookingId));

        [HttpGet("api/booking/smart/available")]
        [AllowAnonymous]
        public async Task<IActionResult> SmartAvailable(
            [FromQuery] string categoryCode,
            [FromQuery] DateTime startOn,
            [FromQuery] DateTime endOn,
            [FromQuery] int? capacity) =>
            Ok(await _bookings.GetSmartAvailableSpacesAsync(categoryCode, startOn, endOn, capacity));

        [HttpGet("api/booking/my")]
        public async Task<IActionResult> MyBookings([FromHeader(Name = "x-user-email")] string? userEmail)
        {
            var email = ResolveUserEmail(userEmail);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity or email header required" });
            return Ok(await _bookings.GetMyBookingsAsync(email));
        }

        [HttpGet("api/booking/recent")]
        [Authorize(Roles = "admin,super_admin,receptionist")]
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
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null)
        {
            var (items, total) = await _bookings.GetBookingsAsync(page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/booking/{publicId:guid}")]
        public async Task<IActionResult> Get(Guid publicId, [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            bool isAdmin = User.IsInRole("admin") || User.IsInRole("super_admin") || User.IsInRole("receptionist");
            var email = isAdmin ? null : ResolveUserEmail(userEmail);
            var result = await _bookings.GetBookingByIdAsync(publicId, email);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("api/booking/{id:int}")]
        public async Task<IActionResult> GetDetails(int id, [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            bool isAdmin = User.IsInRole("admin") || User.IsInRole("super_admin") || User.IsInRole("receptionist");
            var email = isAdmin ? null : ResolveUserEmail(userEmail);
            var result = await _bookings.GetBookingDetailsAsync(id.ToString(), email);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("api/booking/{id:int}/billing-summary")]
        public async Task<IActionResult> GetBillingSummary(int id) =>
            await GetChallan(id, null);

        [HttpGet("api/booking/{id:int}/challan")]
        public async Task<IActionResult> GetChallan(int id, [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            bool isAdmin = User.IsInRole("admin") || User.IsInRole("super_admin") || User.IsInRole("receptionist");
            var email = isAdmin ? null : ResolveUserEmail(userEmail);
            var result = await _bookings.GetChallanAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            
            if (email != null)
            {
                var dto = result.Data as WorkNest.Application.DTOs.Booking.ChallanResponseDto;
                if (dto != null && !string.Equals(dto.CustomerEmail, email, StringComparison.OrdinalIgnoreCase))
                {
                    return Forbid();
                }
            }
            return Ok(result);
        }

        [HttpGet("api/booking/migrate-db")]
        [AllowAnonymous]
        public async Task<IActionResult> MigrateDb()
        {
            try
            {
                string sqlPath = @"F:\WN_APIs\SQL\WN_Challans_GetFullByBooking.sql";
                if (!System.IO.File.Exists(sqlPath))
                {
                    return NotFound(new { isSuccessful = false, message = $"SQL file not found at {sqlPath}" });
                }
                string sql = await System.IO.File.ReadAllTextAsync(sqlPath);
                
                // Split by GO statements
                var parts = System.Text.RegularExpressions.Regex.Split(
                    sql, 
                    @"^\s*GO\s*$", 
                    System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                
                foreach (var part in parts)
                {
                    if (!string.IsNullOrWhiteSpace(part))
                    {
                        await _db.ExecuteRawSqlAsync(part);
                    }
                }
                
                return Ok(new { isSuccessful = true, message = "Stored procedure migrated successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, error = ex.Message });
            }
        }

        [HttpPost("api/booking/create-admin")]
        [Authorize(Roles = "admin,super_admin,receptionist")]
        public async Task<IActionResult> AdminCreate(
            [FromBody] AdminBookingRequest request,
            [FromHeader(Name = "x-user-email")] string? actorEmail)
        {
            if (request.SpaceId == 0)
                return BadRequest(new { isSuccessful = false, message = "SpaceId is required" });
            if (request.UserId == 0 && string.IsNullOrWhiteSpace(request.CustomerEmail))
                return BadRequest(new { isSuccessful = false, message = "UserId or CustomerEmail is required" });
            var actor = ResolveUserEmail(actorEmail);
            return StatusCode(201, await _bookings.CreateAdminBookingAsync(request, actor));
        }

        [HttpPost("api/booking")]
        public async Task<IActionResult> Create(
            [FromBody] BookingRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            var email = ResolveUserEmail(userEmail);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity or email header required" });
            
            var result = await _bookings.CreateBookingAsync(request, email);
            if (!result.IsSuccessful && result.Message == "CustomerProfileRequired")
            {
                return BadRequest(result);
            }
            return StatusCode(201, result);
        }

        [HttpPost("api/booking/smart")]
        public async Task<IActionResult> Smart(
            [FromBody] SmartBookingRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            var email = ResolveUserEmail(userEmail);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity or email header required" });
            
            var result = await _bookings.CreateSmartBookingAsync(request, email);
            if (!result.IsSuccessful && result.Message == "CustomerProfileRequired")
            {
                return BadRequest(result);
            }
            return StatusCode(201, result);
        }

        [HttpPut("api/booking/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] BookingUpdateRequest request) =>
            Ok(await _bookings.UpdateBookingAsync(id, request, null));

        [HttpPut("api/booking/{publicId:guid}")]
        public async Task<IActionResult> UpdateByGuid(Guid publicId, [FromBody] BookingUpdateRequest request)
        {
            var booking = await _db.GetBookingByPublicIdAsync(publicId, null);
            if (booking is null) return NotFound(new { isSuccessful = false, message = "Booking not found" });
            var id = booking.TryGetValue("Id", out var bid) ? Convert.ToInt32(bid) : 0;
            return Ok(await _bookings.UpdateBookingAsync(id, request, null));
        }

        [HttpPatch("api/booking/{id:int}/cancel")]
        public async Task<IActionResult> Cancel(
            int id,
            [FromBody] CancelBookingRequest? request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            var email = ResolveUserEmail(userEmail);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity or email header required" });
            var result = await _bookings.CancelBookingAsync(id, email, request?.CancelReason);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPatch("api/booking/{publicId:guid}/cancel")]
        public async Task<IActionResult> CancelByGuid(
            Guid publicId,
            [FromBody] CancelBookingRequest? request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            var email = ResolveUserEmail(userEmail);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity or email header required" });
            var booking = await _db.GetBookingByPublicIdAsync(publicId, email);
            if (booking is null) return NotFound(new { isSuccessful = false, message = "Booking not found" });
            var id = booking.TryGetValue("Id", out var bid) ? Convert.ToInt32(bid) : 0;
            var result = await _bookings.CancelBookingAsync(id, email, request?.CancelReason);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPatch("api/booking/{id:int}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] BookingStatusUpdateRequest request) =>
            Ok(await _bookings.UpdateBookingStatusAsync(id, request.StatusId, null));

        [HttpPatch("api/booking/{publicId:guid}/status")]
        public async Task<IActionResult> UpdateStatusByGuid(Guid publicId, [FromBody] BookingStatusUpdateRequest request)
        {
            var booking = await _db.GetBookingByPublicIdAsync(publicId, null);
            if (booking is null) return NotFound(new { isSuccessful = false, message = "Booking not found" });
            var id = booking.TryGetValue("Id", out var bid) ? Convert.ToInt32(bid) : 0;
            return Ok(await _bookings.UpdateBookingStatusAsync(id, request.StatusId, null));
        }

        
        [HttpGet("api/booking/{id:int}/challan-pdf")]
        [AllowAnonymous]
        public async Task<IActionResult> GetChallanPdf(int id)
        {
            var challanResult = await _bookings.GetChallanAsync(id);
            if (!challanResult.IsSuccessful || challanResult.Data is null)
                return NotFound(new { isSuccessful = false, message = "Challan details not found." });

            var dto = (ChallanResponseDto)challanResult.Data;
            var pdfBytes = _pdf.GenerateBookingConfirmationPdf(dto);
            var filename = $"Challan-{dto.ChallanNumber ?? id.ToString()}.pdf";
            return File(pdfBytes, "application/pdf", filename);
        }

        [HttpPost("api/booking/{id:int}/send-challan-email")]
        [Authorize(Roles = "admin,super_admin,receptionist")]
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
        [Authorize(Roles = "admin,super_admin,receptionist")]
        public async Task<IActionResult> SendConfirmationEmail(int id)
        {
            var result = await _bookings.SendBookingConfirmationEmailAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPatch("api/booking/{id:int}/reassign")]
        public async Task<IActionResult> Reassign(
            int id,
            [FromBody] ReassignBookingRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            var email = ResolveUserEmail(userEmail);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity or email header required" });
            var result = await _bookings.ReassignBookingAsync(id, request, email);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPatch("api/booking/{publicId:guid}/reassign")]
        public async Task<IActionResult> ReassignByGuid(
            Guid publicId,
            [FromBody] ReassignBookingRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            var email = ResolveUserEmail(userEmail);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity or email header required" });
            var booking = await _db.GetBookingByPublicIdAsync(publicId, null);
            if (booking is null) return NotFound(new { isSuccessful = false, message = "Booking not found" });
            var id = booking.TryGetValue("Id", out var bid) ? Convert.ToInt32(bid) : 0;
            var result = await _bookings.ReassignBookingAsync(id, request, email);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("api/booking/{id:int}/advance-invoice-pdf")]
        public async Task<IActionResult> GetAdvanceInvoicePdf(
            int id,
            [FromQuery] int advMonths = 3,
            [FromQuery] int secMonths = 2,
            [FromQuery] decimal monthlyRate = 0,
            [FromQuery] decimal discount = 0)
        {
            var pdfBytes = await _bookings.GenerateAdvanceInvoicePdfAsync(id, advMonths, secMonths, monthlyRate, discount);
            return File(pdfBytes, "application/pdf", $"AdvanceInvoice-{id}.pdf");
        }
    }
}

