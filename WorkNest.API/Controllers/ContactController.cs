using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Contact;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;
using WorkNest.API.Extensions;
using Microsoft.AspNetCore.RateLimiting;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class ContactController : ControllerBase
    {
        private readonly IContactService _contacts;
        private readonly IDbRepository _db;
        public ContactController(IContactService contacts, IDbRepository db) { _contacts = contacts; _db = db; }

        // Staff = admin / super admin / sales executive / receptionist; customers (role "general") are not staff.
        private const string StaffRoles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive";
        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin";

        [HttpGet("api/contact/recent")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> Recent([FromQuery] int top = 10) =>
            Ok(ApiResponse.Ok(await _contacts.GetRecentContactsAsync(top)));

        [HttpGet("api/contact")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null)
        {
            var (items, total) = await _contacts.GetContactsAsync(page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [EnableRateLimiting("public-forms")]
        [HttpPost("api/contact")]
        [AllowAnonymous] // public contact form; the submitter is recorded only from a real login (JWT), never from a header
        public async Task<IActionResult> CreateContact([FromBody] ContactRequest request) =>
            StatusCode(201, await _contacts.CreateContactAsync(request, "contact", User.GetEmail()));

        [EnableRateLimiting("public-forms")]
        [HttpPost("api/book-tour")]
        [AllowAnonymous] // public book-a-tour form; the submitter is recorded only from a real login (JWT), never from a header
        public async Task<IActionResult> BookTour([FromBody] ContactRequest request) =>
            StatusCode(201, await _contacts.CreateContactAsync(request, "book_tour", User.GetEmail()));

        /// <summary>Feedback on a tour inquiry: not interested (reason, closes it), future prospect (follow-up date,
        /// alerted on that date) or converted into a quotation (quotationId).</summary>
        [HttpPost("api/contact/{id:int}/feedback")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> Feedback(int id, [FromBody] ContactFeedbackRequest request)
        {
            var email = User.GetEmail();
            int? actorId = string.IsNullOrWhiteSpace(email) ? null : (await _db.GetUserIdByEmailAsync(email)).Item1;
            var result = await _contacts.SaveFeedbackAsync(id, request, actorId);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>Same as above, by the inquiry's public id (lists that return only PublicId).</summary>
        [HttpPost("api/contact/{publicId:guid}/feedback")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> FeedbackByGuid(Guid publicId, [FromBody] ContactFeedbackRequest request)
        {
            var (rows, _) = await _db.GetContactsAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Inquiry not found."));
            var id = match.TryGetValue("Id", out var rid) && rid != null ? Convert.ToInt32(rid) : 0;
            return id > 0 ? await Feedback(id, request) : NotFound(ApiResponse.Fail("Inquiry not found."));
        }

        [HttpPatch("api/contact/{id:int}/status")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] ContactStatusUpdateRequest request) =>
            Ok(await _contacts.UpdateContactStatusAsync(id, request.StatusId, null));

        [HttpPatch("api/contact/{publicId:guid}/status")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> UpdateStatusByGuid(Guid publicId, [FromBody] ContactStatusUpdateRequest request)
        {
            var (rows, _) = await _db.GetContactsAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Contact not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _contacts.UpdateContactStatusAsync(id, request.StatusId, null));
        }

        [HttpDelete("api/contact/{id:int}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _contacts.DeleteContactAsync(id));

        [HttpDelete("api/contact/{publicId:guid}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> DeleteByGuid(Guid publicId)
        {
            var (rows, _) = await _db.GetContactsAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Contact not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _contacts.DeleteContactAsync(id));
        }
    }
}
