using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.PricingPlan;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class PricingPlanController : ControllerBase
    {
        private readonly IPricingPlanService _plans;
        public PricingPlanController(IPricingPlanService plans) => _plans = plans;

        [HttpGet("api/pricingplan/all")]
        [AllowAnonymous]
        public async Task<IActionResult> All()
        {
            var items = await _plans.GetAllPlansAsync();
            return Ok(ApiResponse.Ok(items));
        }

        [HttpGet("api/pricingplan")]
        [AllowAnonymous]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string search = "")
        {
            var all = await _plans.GetAllPlansAsync();
            var (items, total) = PaginationHelper.Paginate(all, page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/pricingplan/{id}/summary")]
        [AllowAnonymous]
        public async Task<IActionResult> Summary(int id)
        {
            var result = await _plans.GetPlanSummaryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/pricingplan")]
        public async Task<IActionResult> Create([FromBody] PricingPlanUpsertRequest request) =>
            StatusCode(201, await _plans.CreatePlanAsync(request));

        [HttpPut("api/pricingplan/{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] PricingPlanUpsertRequest request) =>
            Ok(await _plans.UpdatePlanAsync(id, request));

        [HttpDelete("api/pricingplan/{id}")]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _plans.DeletePlanAsync(id));
    }
}
