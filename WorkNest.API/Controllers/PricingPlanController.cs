using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.PricingPlan;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class PricingPlanController : ControllerBase
    {
        private readonly IPricingPlanService _plans;
        private readonly IDbRepository _db;
        public PricingPlanController(IPricingPlanService plans, IDbRepository db) { _plans = plans; _db = db; }

        [HttpGet("api/pricingplan/all")]
        [AllowAnonymous]
        public async Task<IActionResult> All() =>
            Ok(ApiResponse.Ok(await _plans.GetAllPlansAsync()));

        [HttpGet("api/pricingplan")]
        [AllowAnonymous]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10)
        {
            var (items, total) = await _plans.GetPlansAsync(page, limit);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/pricingplan/{id:int}/summary")]
        [AllowAnonymous]
        public async Task<IActionResult> Summary(int id)
        {
            var result = await _plans.GetPlanSummaryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("api/pricingplan/{publicId:guid}/summary")]
        [AllowAnonymous]
        public async Task<IActionResult> SummaryByGuid(Guid publicId)
        {
            var (rows, _) = await _db.GetPricingPlansAsync(1, 10000);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Plan not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            var result = await _plans.GetPlanSummaryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/pricingplan")]
        public async Task<IActionResult> Create([FromBody] PricingPlanUpsertRequest request) =>
            StatusCode(201, await _plans.CreatePlanAsync(request, null));

        [HttpPut("api/pricingplan/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] PricingPlanUpsertRequest request) =>
            Ok(await _plans.UpdatePlanAsync(id, request));

        [HttpPut("api/pricingplan/{publicId:guid}")]
        public async Task<IActionResult> UpdateByGuid(Guid publicId, [FromBody] PricingPlanUpsertRequest request)
        {
            var (rows, _) = await _db.GetPricingPlansAsync(1, 10000);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Plan not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _plans.UpdatePlanAsync(id, request));
        }

        [HttpDelete("api/pricingplan/{id:int}")]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _plans.DeletePlanAsync(id));

        [HttpDelete("api/pricingplan/{publicId:guid}")]
        public async Task<IActionResult> DeleteByGuid(Guid publicId)
        {
            var (rows, _) = await _db.GetPricingPlansAsync(1, 10000);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Plan not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _plans.DeletePlanAsync(id));
        }
    }
}
