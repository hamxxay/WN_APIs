using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Approval Settings (CMS Administration), super admin only: the approver e-mail, the categories and the people per
    /// location and category (script APPROVAL_01_approvals; before it every call answers 503).
    /// </summary>
    [ApiController]
    [Route("api/admin/approval-settings")]
    [Authorize(Roles = ServiceSwitchController.SuperAdminRoles)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ApprovalSettingsController : ControllerBase
    {
        private readonly IApprovalRepository _repo;

        public ApprovalSettingsController(IApprovalRepository repo) => _repo = repo;

        public sealed class SettingsRequest { public string? ApproverEmail { get; set; } }
        public sealed class CategoryRequest { public string? Name { get; set; } public bool IsActive { get; set; } = true; }
        public sealed class PersonRequest { public int LocationId { get; set; } public int CategoryId { get; set; } public int UserId { get; set; } }

        /// <summary>{ approverEmail, linkExpiryDays, updatedOn, updatedBy }.</summary>
        [HttpGet]
        public Task<IActionResult> GetSettings(CancellationToken ct = default) => Run(async () =>
        {
            var s = await _repo.GetSettingsAsync(ct);
            return Ok(ApiResponse.Ok(new { approverEmail = s?.ApproverEmail, linkExpiryDays = s?.LinkExpiryDays ?? 7, updatedOn = s?.UpdatedOn, updatedBy = s?.UpdatedBy }));
        });

        /// <summary>Saves the approver e-mail (blank clears it: new requests are then blocked).</summary>
        [HttpPut]
        public Task<IActionResult> SaveSettings([FromBody] SettingsRequest? request, CancellationToken ct = default) => Run(async () =>
        {
            var email = request?.ApproverEmail?.Trim() ?? "";
            if (email.Length > 0 && !ApprovalRules.ValidEmail(email)) return BadRequest(ApiResponse.Fail("Enter a valid e-mail address."));
            await _repo.SaveSettingsAsync(email, User.GetEmail(), ct);
            return Ok(ApiResponse.Ok(new { approverEmail = email.Length == 0 ? null : email }, email.Length == 0 ? "Approver e-mail cleared." : "Approver e-mail saved."));
        });

        /// <summary>Every category, switched off ones too.</summary>
        [HttpGet("categories")]
        public Task<IActionResult> ListCategories(CancellationToken ct = default) => Run(async () =>
            Ok(ApiResponse.Ok((await _repo.GetCategoriesAsync(false, ct)).Select(c => new { id = c.Id, name = c.Name, isActive = c.IsActive }).ToList())));

        [HttpPost("categories")]
        public Task<IActionResult> AddCategory([FromBody] CategoryRequest? request, CancellationToken ct = default) => Run(async () =>
        {
            var invalid = ApprovalRules.ValidateCategoryName(request?.Name);
            if (invalid != null) return BadRequest(ApiResponse.Fail(invalid));
            var id = await _repo.SaveCategoryAsync(null, request!.Name!.Trim(), true, User.GetEmail(), ct);
            return Ok(ApiResponse.Ok(new { id }, "Category added."));
        });

        /// <summary>Renames or turns a category on or off. Categories are never deleted.</summary>
        [HttpPut("categories/{id:int}")]
        public Task<IActionResult> UpdateCategory(int id, [FromBody] CategoryRequest? request, CancellationToken ct = default) => Run(async () =>
        {
            var invalid = ApprovalRules.ValidateCategoryName(request?.Name);
            if (invalid != null) return BadRequest(ApiResponse.Fail(invalid));
            if (!(await _repo.GetCategoriesAsync(false, ct)).Any(c => c.Id == id)) return NotFound(ApiResponse.Fail("Category not found."));
            await _repo.SaveCategoryAsync(id, request!.Name!.Trim(), request.IsActive, User.GetEmail(), ct);
            return Ok(ApiResponse.Ok(new { id }, "Category saved."));
        });

        /// <summary>?locationId= (none = every location).</summary>
        [HttpGet("people")]
        public Task<IActionResult> ListPeople([FromQuery] int? locationId, CancellationToken ct = default) => Run(async () =>
            Ok(ApiResponse.Ok((await _repo.GetAssigneesAsync(locationId, ct)).Select(p => new
            {
                id = p.Id, locationId = p.LocationId, locationName = p.LocationName, categoryId = p.CategoryId, categoryName = p.CategoryName,
                userId = p.UserId, userName = p.UserName, email = p.Email, userIsActive = p.UserIsActive
            }).ToList())));

        /// <summary>Adds a person for a location and category. The person must be in the staff list (no customers, CEO or Sales Manager).</summary>
        [HttpPost("people")]
        public Task<IActionResult> AddPerson([FromBody] PersonRequest? request, CancellationToken ct = default) => Run(async () =>
        {
            if (request == null || request.LocationId <= 0 || !(await _repo.GetLocationsAsync(ct)).Any(l => l.Id == request.LocationId))
                return BadRequest(ApiResponse.Fail("Choose a location."));
            if (!(await _repo.GetCategoriesAsync(false, ct)).Any(c => c.Id == request.CategoryId)) return BadRequest(ApiResponse.Fail("Choose a category."));
            if (!(await _repo.GetStaffAsync(ct)).Any(s => s.Id == request.UserId)) return BadRequest(ApiResponse.Fail("Choose a staff member."));
            await _repo.AddAssigneeAsync(request.LocationId, request.CategoryId, request.UserId, User.GetEmail(), ct);
            return Ok(ApiResponse.Ok(new { request.LocationId, request.CategoryId, request.UserId }, "Person added."));
        });

        [HttpDelete("people/{id:int}")]
        public Task<IActionResult> RemovePerson(int id, CancellationToken ct = default) => Run(async () =>
        {
            if (!(await _repo.GetAssigneesAsync(null, ct)).Any(p => p.Id == id)) return NotFound(ApiResponse.Fail("Person not found."));
            await _repo.DeleteAssigneeAsync(id, ct);
            return Ok(ApiResponse.Ok(new { id }, "Person removed."));
        });

        /// <summary>The people picker: active staff, without customers, CEO and Sales Manager.</summary>
        [HttpGet("staff")]
        public Task<IActionResult> Staff(CancellationToken ct = default) => Run(async () =>
            Ok(ApiResponse.Ok((await _repo.GetStaffAsync(ct)).Select(s => new { id = s.Id, name = s.Name, email = s.Email, role = s.Role }).ToList())));

        private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try { return await action(); }
            catch (ApprovalsMissingException) { return StatusCode(503, ApiResponse.Fail(ApprovalsMissingException.UserMessage)); }
            catch (ApprovalCategoryException ex) { return Conflict(ApiResponse.Fail(ex.Message)); }
        }
    }
}
