using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Complaint;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;
using WorkNest.API.Extensions;

namespace WorkNest.API.Controllers
{
    /// <summary>Complaints page: complaints from the WhatsApp chatbot and entered by staff.</summary>
    [ApiController]
    [Authorize(Roles = StaffRoles)]
    public class ComplaintController : ControllerBase
    {
        private const string StaffRoles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive";
        private readonly IComplaintService _complaints;
        private readonly IDbRepository _db;

        public ComplaintController(IComplaintService complaints, IDbRepository db)
        {
            _complaints = complaints;
            _db = db;
        }

        [HttpGet("api/complaints")]
        public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int limit = 10,
            [FromQuery] string? search = null, [FromQuery] string? status = null)
        {
            var (items, total) = await _complaints.GetListAsync(page, limit, search, status);
            return Ok(new PaginatedResponse<object> { Data = items.Cast<object>(), Total = total });
        }

        /// <summary>Complaint entered by staff (phone call, walk-in, email).</summary>
        [HttpPost("api/complaints")]
        public async Task<IActionResult> Create([FromBody] ComplaintCreateRequest request)
        {
            var result = await _complaints.CreateManualAsync(request, await ActorIdAsync());
            return result.IsSuccessful ? StatusCode(201, result) : BadRequest(result);
        }

        /// <summary>open | in_progress | resolved | closed. Resolved asks the customer on WhatsApp to confirm.</summary>
        [HttpPatch("api/complaints/{id:int}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] ComplaintStatusUpdateRequest request)
        {
            var result = await _complaints.UpdateStatusAsync(id, request, await ActorIdAsync());
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        private async Task<int?> ActorIdAsync()
        {
            var email = User.GetEmail();
            return string.IsNullOrWhiteSpace(email) ? null : (await _db.GetUserIdByEmailAsync(email)).Item1;
        }
    }
}
