using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Contact;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class ContactController : ControllerBase
    {
        private readonly IContactService _contacts;
        public ContactController(IContactService contacts) => _contacts = contacts;

        [HttpGet("api/contact/recent")]
        public async Task<IActionResult> Recent([FromQuery] int limit = 5)
        {
            var all = await _contacts.GetAllContactsAsync();
            return Ok(ApiResponse.Ok(all.Take(limit)));
        }

        [HttpGet("api/contact")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string search = "")
        {
            var all = await _contacts.GetAllContactsAsync();
            var (items, total) = PaginationHelper.Paginate(all, page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpPost("api/contact")]
        [HttpPost("api/book-tour")]
        [AllowAnonymous]
        public async Task<IActionResult> Create(
            [FromBody] ContactRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail) =>
            StatusCode(201, await _contacts.CreateContactAsync(request, userEmail));

        [HttpPatch("api/contact/{id}/status")]
        public async Task<IActionResult> UpdateStatus(string id, [FromQuery] string status) =>
            Ok(await _contacts.UpdateContactStatusAsync(id, status));

        [HttpDelete("api/contact/{id}")]
        public async Task<IActionResult> Delete(string id) =>
            Ok(await _contacts.DeleteContactAsync(id));
    }
}
