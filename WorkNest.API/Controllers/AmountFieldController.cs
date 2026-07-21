using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [AllowAnonymous]
    public class AmountFieldController : ControllerBase
    {
        private readonly IAmountFieldService _svc;
        public AmountFieldController(IAmountFieldService svc) => _svc = svc;

        [HttpGet("api/amount-fields")]
        public async Task<IActionResult> GetAll() => Ok(await _svc.GetAllAsync());
    }
}
