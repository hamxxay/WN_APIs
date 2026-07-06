using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Membership;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class MembershipController : ControllerBase
    {
        private readonly IMembershipService _memberships;
        public MembershipController(IMembershipService memberships) => _memberships = memberships;

        [HttpGet("api/membership")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string search = "")
        {
            var all = await _memberships.GetAllMembershipsAsync();
            var (items, total) = PaginationHelper.Paginate(all, page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/membership/{id}/summary")]
        public async Task<IActionResult> Summary(int id)
        {
            var result = await _memberships.GetMembershipSummaryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/membership")]
        public async Task<IActionResult> Create([FromBody] MembershipCreateRequest request) =>
            StatusCode(201, await _memberships.CreateMembershipAsync(request));

        [HttpPatch("api/membership/{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromQuery] string status) =>
            Ok(await _memberships.UpdateMembershipStatusAsync(id, status));

        [HttpDelete("api/membership/{id}")]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _memberships.DeleteMembershipAsync(id));
    }
}
