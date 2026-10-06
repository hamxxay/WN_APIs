using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.PlanFeature;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class PlanFeatureController : ControllerBase
    {
        private readonly IPlanFeatureService _features;
        public PlanFeatureController(IPlanFeatureService features) => _features = features;

        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin";

        [HttpGet("api/planfeature/by-plan/{planId:int}")]
        public async Task<IActionResult> GetByPlan(int planId) =>
            Ok(await _features.GetByPlanAsync(planId));

        [HttpPost("api/planfeature")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Create([FromBody] PlanFeatureRequest request) =>
            StatusCode(201, await _features.CreateAsync(request, null));

        [HttpPut("api/planfeature/{id:int}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Update(int id, [FromBody] PlanFeatureUpdateRequest request) =>
            Ok(await _features.UpdateAsync(id, request));

        [HttpDelete("api/planfeature/{id:int}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _features.DeleteAsync(id));
    }
}
