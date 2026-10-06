using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Membership;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")] // memberships: staff only
    public class MembershipController : ControllerBase
    {
        private readonly IMembershipService _memberships;
        private readonly IDbRepository _db;
        public MembershipController(IMembershipService memberships, IDbRepository db) { _memberships = memberships; _db = db; }

        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin";

        [HttpGet("api/membership")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null)
        {
            var (items, total) = await _memberships.GetMembershipsAsync(page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/membership/{id:int}/summary")]
        public async Task<IActionResult> Summary(int id)
        {
            var result = await _memberships.GetMembershipSummaryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("api/membership/{publicId:guid}/summary")]
        public async Task<IActionResult> SummaryByGuid(Guid publicId)
        {
            var (rows, _) = await _db.GetMembershipsAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Membership not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            var result = await _memberships.GetMembershipSummaryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/membership")]
        public async Task<IActionResult> Create([FromBody] MembershipCreateRequest request) =>
            StatusCode(201, await _memberships.CreateMembershipAsync(request, null));

        [HttpPatch("api/membership/{id:int}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] MembershipStatusUpdateRequest request) =>
            Ok(await _memberships.UpdateMembershipStatusAsync(id, request.StatusId, null));

        [HttpPatch("api/membership/{publicId:guid}/status")]
        public async Task<IActionResult> UpdateStatusByGuid(Guid publicId, [FromBody] MembershipStatusUpdateRequest request)
        {
            var (rows, _) = await _db.GetMembershipsAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Membership not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _memberships.UpdateMembershipStatusAsync(id, request.StatusId, null));
        }

        [HttpDelete("api/membership/{id:int}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _memberships.DeleteMembershipAsync(id));

        [HttpDelete("api/membership/{publicId:guid}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> DeleteByGuid(Guid publicId)
        {
            var (rows, _) = await _db.GetMembershipsAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Membership not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _memberships.DeleteMembershipAsync(id));
        }
    }
}
