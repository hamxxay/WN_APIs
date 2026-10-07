using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Quotation;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    [ValidateLocationScope]
    public class QuotationController : ControllerBase
    {
        private readonly IQuotationService _quotations;
        private readonly IDbRepository _db;

        public QuotationController(IQuotationService quotations, IDbRepository db)
        {
            _quotations = quotations;
            _db = db;
        }

        // Staff = admin / super admin / sales executive / receptionist; customers (role "general") are not staff.
        private const string StaffRoles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive";

        /// <summary>Signed-in user's email from the JWT only (the x-user-email header is no longer trusted).</summary>
        private string? ResolveUserEmail() => User.GetEmail();

        private async Task<int?> ResolveActorIdAsync()
        {
            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email)) return null;
            var actorRow = await _db.GetUserByEmailAsync(email);
            return actorRow?.TryGetValue("Id", out var aid) == true && aid != null ? System.Convert.ToInt32(aid) : (int?)null;
        }

        /// <summary>The caller's customer id (0 when the signed-in user has no customer record).</summary>
        private async Task<int> ResolveCallerCustomerIdAsync()
        {
            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email)) return 0;
            var userRow = await _db.GetUserByEmailAsync(email);
            if (userRow == null) return 0;
            int customerId = userRow.TryGetValue("CustomerId", out var cid) && cid != null ? Convert.ToInt32(cid) : 0;
            int userId = userRow.TryGetValue("Id", out var uid) && uid != null ? Convert.ToInt32(uid) : 0;
            if (customerId <= 0 && userId > 0)
            {
                var custRow = await _db.GetCustomerByUserIdAsync(userId);
                if (custRow != null && custRow.TryGetValue("Id", out var custId) && custId != null) customerId = Convert.ToInt32(custId);
            }
            return customerId;
        }

        /// <summary>Staff see every quotation; a customer only quotations issued to their own customer record.</summary>
        private async Task<bool> CanSeeCustomerAsync(int customerId)
        {
            if (User.IsStaff()) return true;
            var own = await ResolveCallerCustomerIdAsync();
            return own > 0 && own == customerId;
        }

        [HttpPost("api/quotation")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> Create([FromBody] QuotationRequest request)
        {
            int? actorId = await ResolveActorIdAsync();

            try
            {
                var res = await _quotations.CreateQuotationAsync(request, actorId);
                return Ok(ApiResponse.Ok(res, "Quotation generated successfully."));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }

        [AllowAnonymous]
        [HttpGet("api/quotation/offering-types")]
        [HttpGet("api/offering-types")]
        public async Task<IActionResult> GetOfferingTypes([FromQuery] bool? activeOnly = null)
        {
            try
            {
                var types = await _quotations.GetOfferingTypesAsync(activeOnly);
                return Ok(ApiResponse.Ok(types));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }

        // Login required (was anonymous and trusted an x-user-email header): the caller's own quotations only.
        [HttpGet("api/quotations")]
        [HttpGet("api/quotation/my")]
        public async Task<IActionResult> GetMyQuotations()
        {
            try
            {
                var email = ResolveUserEmail();
                if (string.IsNullOrWhiteSpace(email)) return Unauthorized(ApiResponse.Fail("User identity required."));
                var userRow = await _db.GetUserByEmailAsync(email);
                if (userRow == null) return Unauthorized(ApiResponse.Fail("User not found."));
                int customerId = await ResolveCallerCustomerIdAsync();
                if (customerId <= 0) return Ok(ApiResponse.Ok(new List<object>()));
                var res = await _quotations.GetQuotationsByCustomerAsync(customerId);
                return Ok(ApiResponse.Ok(res));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }

        // Was anonymous: staff, or the customer themselves.
        [HttpGet("api/quotation/by-customer/{customerId:int}")]
        public async Task<IActionResult> GetByCustomer(int customerId)
        {
            try
            {
                if (!await CanSeeCustomerAsync(customerId))
                    return NotFound(ApiResponse.Fail("Quotations not found."));
                var res = await _quotations.GetQuotationsByCustomerAsync(customerId);
                return Ok(ApiResponse.Ok(res));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }

        // Was anonymous: login required; staff see any quotation, a customer only their own
        // (the public /quotation/:id page sends an anonymous visitor to the login page on 401 and back afterwards).
        [HttpGet("api/quotation/{id:int}")]
        [HttpGet("api/quotations/{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var res = await _quotations.GetQuotationByIdAsync(id);
            if (res == null || !await CanSeeCustomerAsync(res.CustomerId)) return NotFound(ApiResponse.Fail("Quotation not found."));
            return Ok(ApiResponse.Ok(res));
        }

        [HttpGet("api/quotation")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> GetList(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null,
            [FromQuery] int? locationId = null)
        {
            int? effectiveLocationId = locationId;
            if (User?.Identity?.IsAuthenticated == true && User.IsLocationBoundRole())
            {
                var claimLocId = User.GetLocationId();
                if (claimLocId.HasValue)
                {
                    effectiveLocationId = claimLocId.Value;
                }
            }

            var (rows, total) = await _quotations.GetQuotationsAsync(page, limit, search, effectiveLocationId);
            return Ok(new { data = rows, total = total, page = page, limit = limit });
        }

        [HttpGet("api/quotation/history")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> GetHistory([FromQuery] int customerId, [FromQuery] int spaceId)
        {
            var res = await _quotations.GetQuotationHistoryAsync(customerId, spaceId);
            return Ok(ApiResponse.Ok(res));
        }

        [HttpPost("api/quotation/{id:int}/send-email")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> SendEmail(int id, [FromBody] SendQuotationEmailRequest request)
        {
            try
            {
                await _quotations.SendQuotationEmailAsync(id, request.Email);
                return Ok(ApiResponse.Ok("Quotation emailed successfully."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }

        [HttpPost("api/quotation/{id:int}/convert")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> ConvertQuotation(int id)
        {
            // Bookings from quotations are created only when the signed agreement comes back
            // (POST api/agreements/{id}/sign). Admins can still create a booking directly from the booking form.
            await Task.CompletedTask;
            return BadRequest(ApiResponse.Fail("A quotation becomes a booking only when its signed agreement is uploaded. Send the agreement, then upload the signed copy."));
        }

        // Login required (was anonymous and trusted an x-user-email header); the caller must own the quotation.
        [HttpPost("api/quotation/{id:int}/accept")]
        [HttpPost("api/quotation/{id:int}/versions/{version:int}/accept")]
        [HttpPost("api/quotations/{id:int}/accept")]
        [HttpPost("api/quotations/{id:int}/versions/{version:int}/accept")]
        public async Task<IActionResult> AcceptQuotation(int id, [FromBody] AcceptQuotationRequest request, [FromRoute] int version = 1)
        {
            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(ApiResponse.Fail("User identity required."));

            var userRow = await _db.GetUserByEmailAsync(email);
            if (userRow == null) return Unauthorized(ApiResponse.Fail("User not found."));

            int customerId = await ResolveCallerCustomerIdAsync();
            int? userId = userRow.TryGetValue("Id", out var uid) && uid != null ? Convert.ToInt32(uid) : (int?)null;

            if (customerId <= 0)
                return BadRequest(ApiResponse.Fail("Customer account not associated with this user."));

            try
            {
                var q = await _quotations.GetQuotationByIdAsync(id);
                if (q == null || q.CustomerId != customerId)
                    return NotFound(ApiResponse.Fail("Quotation not found."));
                if (q != null && version == 1 && q.Version > 1) version = q.Version;

                var res = await _quotations.AcceptQuotationAsync(id, version, customerId, request?.Note, userId);
                return Ok(ApiResponse.Ok(res, "Quotation accepted successfully."));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail(ex.Message));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }

        // Login required (was anonymous and trusted an x-user-email header); the caller must own the quotation.
        [HttpPost("api/quotation/{id:int}/decline")]
        [HttpPost("api/quotation/{id:int}/versions/{version:int}/decline")]
        [HttpPost("api/quotations/{id:int}/decline")]
        [HttpPost("api/quotations/{id:int}/versions/{version:int}/decline")]
        public async Task<IActionResult> DeclineQuotation(int id, [FromBody] DeclineQuotationRequest request, [FromRoute] int version = 1)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Note))
                return BadRequest(ApiResponse.Fail("Decline reason note is mandatory."));

            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(ApiResponse.Fail("User identity required."));

            var userRow = await _db.GetUserByEmailAsync(email);
            if (userRow == null) return Unauthorized(ApiResponse.Fail("User not found."));

            int customerId = await ResolveCallerCustomerIdAsync();
            int? userId = userRow.TryGetValue("Id", out var uid) && uid != null ? Convert.ToInt32(uid) : (int?)null;

            if (customerId <= 0)
                return BadRequest(ApiResponse.Fail("Customer account not associated with this user."));

            try
            {
                var q = await _quotations.GetQuotationByIdAsync(id);
                if (q == null || q.CustomerId != customerId)
                    return NotFound(ApiResponse.Fail("Quotation not found."));
                if (q != null && version == 1 && q.Version > 1) version = q.Version;

                var res = await _quotations.DeclineQuotationAsync(id, version, customerId, request.Note, userId);
                return Ok(ApiResponse.Ok(res, "Quotation declined successfully."));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail(ex.Message));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }

        [HttpPost("api/quotation/{id:int}/create-version")]
        [Authorize(Roles = StaffRoles)]
        [HttpPost("api/quotation/{id:int}/versions")]
        [HttpPost("api/quotations/{id:int}/create-version")]
        [HttpPost("api/quotations/{id:int}/versions")]
        public async Task<IActionResult> CreateNewVersion(int id)
        {
            int? actorId = await ResolveActorIdAsync();

            try
            {
                var res = await _quotations.CreateNewVersionAsync(id, actorId);
                return Ok(ApiResponse.Ok(res, "New quotation version created successfully."));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }

        // Was anonymous: staff, or the customer the quotation was issued to.
        [HttpGet("api/quotation/{id:int}/versions")]
        [HttpGet("api/quotations/{id:int}/versions")]
        public async Task<IActionResult> GetVersions(int id)
        {
            var q = await _quotations.GetQuotationByIdAsync(id);
            if (q == null || !await CanSeeCustomerAsync(q.CustomerId)) return NotFound(ApiResponse.Fail("Quotation not found."));
            var res = await _quotations.GetVersionsAsync(id);
            return Ok(ApiResponse.Ok(res));
        }

        // Was anonymous: staff, or the customer the quotation was issued to.
        [HttpGet("api/quotation/{id:int}/versions/{version:int}")]
        [HttpGet("api/quotations/{id:int}/versions/{version:int}")]
        public async Task<IActionResult> GetVersionById(int id, int version)
        {
            var root = await _quotations.GetQuotationByIdAsync(id);
            if (root == null || !await CanSeeCustomerAsync(root.CustomerId)) return NotFound(ApiResponse.Fail("Quotation version not found."));
            var versions = await _quotations.GetVersionsAsync(id);
            var specificVersion = versions.FirstOrDefault(v => v.Version == version);
            if (specificVersion == null)
            {
                specificVersion = await _quotations.GetQuotationByIdAsync(version);
            }
            if (specificVersion == null || specificVersion.CustomerId != root.CustomerId) return NotFound(ApiResponse.Fail("Quotation version not found."));
            return Ok(ApiResponse.Ok(specificVersion));
        }

        // Was anonymous: activity feed across all quotations, staff only.
        [HttpGet("api/quotation/activities")]
        [Authorize(Roles = StaffRoles)]
        [HttpGet("api/quotations/activities")]
        public async Task<IActionResult> GetActivities([FromQuery] int? quotationId = null, [FromQuery] int limit = 20)
        {
            var res = await _quotations.GetActivitiesAsync(quotationId, limit);
            return Ok(ApiResponse.Ok(res));
        }

        [HttpGet("api/quotation/responses")]
        [Authorize(Roles = StaffRoles)]
        [HttpGet("api/quotations/responses")]
        [HttpGet("api/quotation-responses")]
        public async Task<IActionResult> GetResponses([FromQuery] int page = 1, [FromQuery] int limit = 20, [FromQuery] string? search = null)
        {
            var (rows, total) = await _quotations.GetQuotationResponsesAsync(page, limit, search);
            return Ok(new { data = rows, total = total, page = page, limit = limit });
        }

        [HttpPost("api/quotation/{id:int}/send")]
        [Authorize(Roles = StaffRoles)]
        [HttpPost("api/quotations/{id:int}/send")]
        [HttpPost("api/quotation/{id:int}/versions/{version:int}/send")]
        [HttpPost("api/quotations/{id:int}/versions/{version:int}/send")]
        public async Task<IActionResult> SendQuotation(int id, [FromRoute] int version = 1)
        {
            int? actorId = await ResolveActorIdAsync();

            try
            {
                int targetId = id;
                if (version > 0)
                {
                    var versions = await _quotations.GetVersionsAsync(id);
                    var specificVersion = versions.FirstOrDefault(v => v.Version == version);
                    if (specificVersion != null)
                    {
                        targetId = specificVersion.Id;
                    }
                }

                await _quotations.SendQuotationAsync(targetId, actorId);
                return Ok(ApiResponse.Ok("Quotation sent successfully."));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }
    }
}
