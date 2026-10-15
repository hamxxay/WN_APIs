using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Contact;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;
using WorkNest.API.Extensions;
using Microsoft.AspNetCore.RateLimiting;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class ContactController : ControllerBase
    {
        private readonly IContactService _contacts;
        private readonly IDbRepository _db;
        public ContactController(IContactService contacts, IDbRepository db) { _contacts = contacts; _db = db; }

        // Staff = admin / super admin / sales executive / receptionist; customers (role "general") are not staff.
        private const string StaffRoles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive";
        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin";

        /// <summary>Newest tour inquiries (with locationId / locationName). Location-bound staff: their branches + those without one.</summary>
        [HttpGet("api/contact/recent")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> Recent([FromQuery] int top = 10) =>
            Ok(ApiResponse.Ok(await _contacts.GetRecentContactsAsync(top, User.GetBranchScope().ForList(null))));

        /// <summary>
        /// Tour inquiries; every row has locationId / locationName (null = no branch). Location-bound staff (admin, sales
        /// executive) see their branches plus the inquiries without a branch; others see all. locationId: only that branch
        /// (403 when it is not one of the user's), 0 = only the inquiries without a branch.
        /// </summary>
        [HttpGet("api/contact")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null,
            [FromQuery] string? type = null,
            [FromQuery] int? locationId = null)
        {
            var branch = User.GetBranchScope().ForList(locationId);
            if (branch is null) return StatusCode(403, ApiResponse.Fail(BranchMessages.ListForbidden));
            try
            {
                var (items, total) = await _contacts.GetContactsAsync(page, limit, search, type, branch);
                return Ok(new PaginatedResponse<object> { Data = items, Total = total });
            }
            catch (InvalidOperationException ex) { return BadRequest(ApiResponse.Fail(ex.Message)); } // Type / branch filter before CT_01 / BR_01
        }

        [EnableRateLimiting("public-forms")]
        [HttpPost("api/contact")]
        [AllowAnonymous] // public contact form; the submitter is recorded only from a real login (JWT), never from a header
        public Task<IActionResult> CreateContact([FromBody] ContactRequest request) => CreateAsync(request, "contact");

        [EnableRateLimiting("public-forms")]
        [HttpPost("api/book-tour")]
        [AllowAnonymous] // public book-a-tour form; the submitter is recorded only from a real login (JWT), never from a header
        public Task<IActionResult> BookTour([FromBody] ContactRequest request) => CreateAsync(request, "book_tour");

        /// <summary>
        /// Both forms (website, app, customer, staff "Add inquiry"). locationId = the branch: staff signed in with a
        /// location-bound role get only their own branches (one branch: always that one); everyone else may give any
        /// active location (or none).
        /// </summary>
        private async Task<IActionResult> CreateAsync(ContactRequest request, string contactType)
        {
            var fromStaff = User.IsStaff();
            var scope = fromStaff ? User.GetBranchScope() : BranchScope.Everything;
            var branch = await _contacts.ChooseBranchAsync(request.LocationId, scope);
            if (branch.IsError) return StatusCode(branch.ErrorStatus, ApiResponse.Fail(branch.Error!));
            return StatusCode(201, await _contacts.CreateContactAsync(request, contactType, User.GetEmail(), fromStaff, branch.LocationId));
        }

        /// <summary>
        /// Set / change an inquiry's branch: { "locationId": 2 }. Location-bound staff: only to one of their own branches
        /// (403) and only inquiries they can see; others: any active location, or null = no branch.
        /// </summary>
        [HttpPatch("api/contact/{publicId:guid}/location")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> UpdateLocation(Guid publicId, [FromBody] ContactLocationRequest request)
        {
            var scope = User.GetBranchScope();
            var branch = await _contacts.ChooseBranchAsync(request?.LocationId, scope, change: true);
            if (branch.IsError) return StatusCode(branch.ErrorStatus, ApiResponse.Fail(branch.Error!));
            var result = await _contacts.UpdateLocationAsync(publicId, branch.Location, scope, User.GetEmail());
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>The inquiry with this public id among those the user can see (their branches + those without one).</summary>
        private async Task<IDictionary<string, object?>?> FindVisibleAsync(Guid publicId)
        {
            var (rows, _) = await _db.GetContactsAsync(1, 10000, null, null, User.GetBranchScope().ForList(null));
            return rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
        }

        /// <summary>Feedback on a tour inquiry: not interested (reason, closes it), future prospect (follow-up date,
        /// alerted on that date) or converted into a quotation (quotationId).</summary>
        [HttpPost("api/contact/{id:int}/feedback")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> Feedback(int id, [FromBody] ContactFeedbackRequest request)
        {
            var email = User.GetEmail();
            int? actorId = string.IsNullOrWhiteSpace(email) ? null : (await _db.GetUserIdByEmailAsync(email)).Item1;
            var result = await _contacts.SaveFeedbackAsync(id, request, actorId);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>Same as above, by the inquiry's public id (lists that return only PublicId).</summary>
        [HttpPost("api/contact/{publicId:guid}/feedback")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> FeedbackByGuid(Guid publicId, [FromBody] ContactFeedbackRequest request)
        {
            var match = await FindVisibleAsync(publicId);
            if (match is null) return NotFound(ApiResponse.Fail("Inquiry not found."));
            var id = match.TryGetValue("Id", out var rid) && rid != null ? Convert.ToInt32(rid) : 0;
            return id > 0 ? await Feedback(id, request) : NotFound(ApiResponse.Fail("Inquiry not found."));
        }

        /// <summary>Change an inquiry's Type: Website, WhatsApp, WalkIn or Reference (+ referenceName; empty = the staff member).</summary>
        [HttpPatch("api/contact/{publicId:guid}/inquiry-type")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> UpdateInquiryType(Guid publicId, [FromBody] ContactInquiryTypeRequest request)
        {
            var result = await _contacts.UpdateInquiryTypeAsync(publicId, request, User.GetEmail());
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        [HttpPatch("api/contact/{id:int}/status")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] ContactStatusUpdateRequest request) =>
            Ok(await _contacts.UpdateContactStatusAsync(id, request.StatusId, null));

        [HttpPatch("api/contact/{publicId:guid}/status")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> UpdateStatusByGuid(Guid publicId, [FromBody] ContactStatusUpdateRequest request)
        {
            var match = await FindVisibleAsync(publicId);
            if (match is null) return NotFound(ApiResponse.Fail("Contact not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _contacts.UpdateContactStatusAsync(id, request.StatusId, null));
        }

        [HttpDelete("api/contact/{id:int}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _contacts.DeleteContactAsync(id));

        [HttpDelete("api/contact/{publicId:guid}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> DeleteByGuid(Guid publicId)
        {
            var match = await FindVisibleAsync(publicId);
            if (match is null) return NotFound(ApiResponse.Fail("Contact not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _contacts.DeleteContactAsync(id));
        }
    }
}
