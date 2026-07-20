using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class AccountCoaController : ControllerBase
    {
        private readonly IAccountCoaService _accounts;
        public AccountCoaController(IAccountCoaService accounts) => _accounts = accounts;

        /// <summary>Returns all active bank accounts sorted alphabetically.</summary>
        [HttpGet("api/account-coa")]
        public async Task<IActionResult> GetAll() =>
            Ok(await _accounts.GetAllAsync());

        /// <summary>Returns a single bank account by its primary key.</summary>
        [HttpGet("api/account-coa/{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _accounts.GetByIdAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }
    }
}
