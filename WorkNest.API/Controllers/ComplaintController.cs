using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Complaint;
using WorkNest.Application.Helpers;
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

        /// <summary>
        /// Complaints; every row has branch plus locationId / locationName (the location whose name is the branch; null =
        /// no branch or no such location). Location-bound staff (admin, sales executive) see their branches plus the
        /// complaints without one; others see all. locationId: only that branch (403 when it is not one of the user's),
        /// 0 = only the complaints without a branch.
        /// </summary>
        [HttpGet("api/complaints")]
        public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int limit = 10,
            [FromQuery] string? search = null, [FromQuery] string? status = null, [FromQuery] int? locationId = null)
        {
            var branch = User.GetBranchScope().ForList(locationId);
            if (branch is null) return StatusCode(403, ApiResponse.Fail(BranchMessages.ListForbidden));
            var (items, total) = await _complaints.GetListAsync(page, limit, search, status, branch);
            return Ok(new PaginatedResponse<object> { Data = items.Cast<object>(), Total = total });
        }

        /// <summary>
        /// Complaint entered by staff (phone call, walk-in, email). locationId = the branch (older forms: branch = its
        /// name), saved as the location's name. Location-bound staff: one of their own branches (one branch: always that
        /// one); others: any active location, or none.
        /// </summary>
        [HttpPost("api/complaints")]
        public async Task<IActionResult> Create([FromBody] ComplaintCreateRequest request)
        {
            var scope = User.GetBranchScope();
            var branch = await _complaints.ChooseBranchAsync(request.LocationId, request.Branch, scope);
            if (branch.IsError) return StatusCode(branch.ErrorStatus, ApiResponse.Fail(branch.Error!));
            var result = await _complaints.CreateManualAsync(request, await ActorIdAsync(), branch, scope);
            return result.IsSuccessful ? StatusCode(201, result) : BadRequest(result);
        }

        /// <summary>open | in_progress | resolved | closed. Resolved asks the customer on WhatsApp to confirm.</summary>
        [HttpPatch("api/complaints/{id:int}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] ComplaintStatusUpdateRequest request)
        {
            var result = await _complaints.UpdateStatusAsync(id, request, await ActorIdAsync(), User.GetBranchScope());
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Set / change a complaint's branch: { "locationId": 2 } (saved as that location's name). Location-bound staff:
        /// only to one of their own branches (403) and only complaints they can see; others: any active location, or
        /// null = no branch.
        /// </summary>
        [HttpPatch("api/complaints/{id:int}/branch")]
        public async Task<IActionResult> UpdateBranch(int id, [FromBody] ComplaintBranchUpdateRequest request)
        {
            var scope = User.GetBranchScope();
            var branch = await _complaints.ChooseBranchAsync(request?.LocationId, null, scope, change: true);
            if (branch.IsError) return StatusCode(branch.ErrorStatus, ApiResponse.Fail(branch.Error!));
            var result = await _complaints.UpdateBranchAsync(id, branch.Location, scope, await ActorIdAsync());
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        private async Task<int?> ActorIdAsync()
        {
            var email = User.GetEmail();
            return string.IsNullOrWhiteSpace(email) ? null : (await _db.GetUserIdByEmailAsync(email)).Item1;
        }
    }
}
