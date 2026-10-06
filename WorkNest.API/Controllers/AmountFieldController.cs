using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.AmountField;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")] // amount-field / GL account mapping: staff read, admin change (was anonymous at class level, which also made the PATCH anonymous)
    public class AmountFieldController : ControllerBase
    {
        private readonly IAmountFieldService _svc;
        public AmountFieldController(IAmountFieldService svc) => _svc = svc;

        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin";

        [HttpGet("api/amount-fields")]
        public async Task<IActionResult> GetAll() => Ok(await _svc.GetAllAsync());

        [HttpPatch("api/amount-fields/{id:int}/account")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> UpdateAccount(int id, [FromBody] AmountFieldUpdateAccountRequest request)
            => Ok(await _svc.UpdateAccountAsync(id, request.AccountId));
    }
}
