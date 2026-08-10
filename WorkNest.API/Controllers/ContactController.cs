using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Contact;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class ContactController : ControllerBase
    {
        private readonly IContactService _contacts;
        private readonly IDbRepository _db;
        public ContactController(IContactService contacts, IDbRepository db) { _contacts = contacts; _db = db; }

        [HttpGet("api/contact/recent")]
        public async Task<IActionResult> Recent([FromQuery] int top = 10) =>
            Ok(ApiResponse.Ok(await _contacts.GetRecentContactsAsync(top)));

        [HttpGet("api/contact")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null)
        {
            var (items, total) = await _contacts.GetContactsAsync(page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpPost("api/contact")]
        [AllowAnonymous]
        public async Task<IActionResult> CreateContact(
            [FromBody] ContactRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail) =>
            StatusCode(201, await _contacts.CreateContactAsync(request, "contact", userEmail));

        [HttpPost("api/book-tour")]
        [AllowAnonymous]
        public async Task<IActionResult> BookTour(
            [FromBody] ContactRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail) =>
            StatusCode(201, await _contacts.CreateContactAsync(request, "book_tour", userEmail));

        [HttpPatch("api/contact/{id:int}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] ContactStatusUpdateRequest request) =>
            Ok(await _contacts.UpdateContactStatusAsync(id, request.StatusId, null));

        [HttpPatch("api/contact/{publicId:guid}/status")]
        public async Task<IActionResult> UpdateStatusByGuid(Guid publicId, [FromBody] ContactStatusUpdateRequest request)
        {
            var (rows, _) = await _db.GetContactsAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Contact not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _contacts.UpdateContactStatusAsync(id, request.StatusId, null));
        }

        [HttpDelete("api/contact/{id:int}")]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _contacts.DeleteContactAsync(id));

        [HttpDelete("api/contact/{publicId:guid}")]
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
