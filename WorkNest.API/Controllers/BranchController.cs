using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class BranchController : ControllerBase
    {
        private readonly IBranchService _branch;
        public BranchController(IBranchService branch) => _branch = branch;

        [HttpGet("api/branch")]
        [AllowAnonymous]
        public async Task<IActionResult> Branches() =>
            Ok(await _branch.GetAllBranchesAsync());

        [HttpGet("api/company")]
        [AllowAnonymous]
        public async Task<IActionResult> Companies() =>
            Ok(await _branch.GetAllCompaniesAsync());

        [HttpGet("api/city")]
        [AllowAnonymous]
        public async Task<IActionResult> Cities() =>
            Ok(await _branch.GetAllCitiesAsync());
    }
}
