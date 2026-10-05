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

        private string? ResolveUserEmail(string? headerEmail)
        {
            var claimEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                             ?? User.FindFirst("email")?.Value;
            return !string.IsNullOrWhiteSpace(claimEmail) ? claimEmail : headerEmail;
        }

        [HttpPost("api/quotation")]
        public async Task<IActionResult> Create([FromBody] QuotationRequest request, [FromHeader(Name = "x-user-email")] string? actorEmail)
        {
            var email = ResolveUserEmail(actorEmail);
            int? actorId = null;
            if (!string.IsNullOrWhiteSpace(email))
            {
                var actorRow = await _db.GetUserByEmailAsync(email);
                actorId = actorRow?.TryGetValue("Id", out var aid) == true ? System.Convert.ToInt32(aid) : (int?)null;
            }

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

        [AllowAnonymous]
        [HttpGet("api/quotations")]
        [HttpGet("api/quotation/my")]
        public async Task<IActionResult> GetMyQuotations([FromHeader(Name = "x-user-email")] string? actorEmail)
        {
            try
            {
                var email = ResolveUserEmail(actorEmail);
                if (string.IsNullOrWhiteSpace(email)) return Unauthorized(ApiResponse.Fail("User identity required."));
                var userRow = await _db.GetUserByEmailAsync(email);
                if (userRow == null) return Unauthorized(ApiResponse.Fail("User not found."));
                int customerId = userRow.TryGetValue("CustomerId", out var cid) && cid != null ? Convert.ToInt32(cid) : 0;
                int userId = userRow.TryGetValue("Id", out var uid) && uid != null ? Convert.ToInt32(uid) : 0;
                if (customerId <= 0 && userId > 0)
                {
                    var custRow = await _db.GetCustomerByUserIdAsync(userId);
                    if (custRow != null && custRow.TryGetValue("Id", out var custId) && custId != null) customerId = Convert.ToInt32(custId);
                }
                if (customerId <= 0) return Ok(ApiResponse.Ok(new List<object>()));
                var res = await _quotations.GetQuotationsByCustomerAsync(customerId);
                return Ok(ApiResponse.Ok(res));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }

        [AllowAnonymous]
        [HttpGet("api/quotation/by-customer/{customerId:int}")]
        public async Task<IActionResult> GetByCustomer(int customerId)
        {
            try
            {
                var res = await _quotations.GetQuotationsByCustomerAsync(customerId);
                return Ok(ApiResponse.Ok(res));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }

        [AllowAnonymous]
        [HttpGet("api/quotation/{id:int}")]
        [HttpGet("api/quotations/{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var res = await _quotations.GetQuotationByIdAsync(id);
            if (res == null) return NotFound(ApiResponse.Fail("Quotation not found."));
            return Ok(ApiResponse.Ok(res));
        }

        [HttpGet("api/quotation")]
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
        public async Task<IActionResult> GetHistory([FromQuery] int customerId, [FromQuery] int spaceId)
        {
            var res = await _quotations.GetQuotationHistoryAsync(customerId, spaceId);
            return Ok(ApiResponse.Ok(res));
        }

        [HttpPost("api/quotation/{id:int}/send-email")]
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
        public async Task<IActionResult> ConvertQuotation(int id, [FromHeader(Name = "x-user-email")] string? actorEmail)
        {
            // Bookings from quotations are created only when the signed agreement comes back
            // (POST api/agreements/{id}/sign). Admins can still create a booking directly from the booking form.
            await Task.CompletedTask;
            return BadRequest(ApiResponse.Fail("A quotation becomes a booking only when its signed agreement is uploaded. Send the agreement, then upload the signed copy."));
        }

        [AllowAnonymous]
        [HttpPost("api/quotation/{id:int}/accept")]
        [HttpPost("api/quotation/{id:int}/versions/{version:int}/accept")]
        [HttpPost("api/quotations/{id:int}/accept")]
        [HttpPost("api/quotations/{id:int}/versions/{version:int}/accept")]
        public async Task<IActionResult> AcceptQuotation(int id, [FromBody] AcceptQuotationRequest request, [FromRoute] int version = 1, [FromHeader(Name = "x-user-email")] string? actorEmail = null)
        {
            var email = ResolveUserEmail(actorEmail);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(ApiResponse.Fail("User identity required."));

            var userRow = await _db.GetUserByEmailAsync(email);
            if (userRow == null) return Unauthorized(ApiResponse.Fail("User not found."));

            int customerId = userRow.TryGetValue("CustomerId", out var cid) && cid != null ? Convert.ToInt32(cid) : 0;
            int? userId = userRow.TryGetValue("Id", out var uid) && uid != null ? Convert.ToInt32(uid) : (int?)null;

            if (customerId <= 0)
                return BadRequest(ApiResponse.Fail("Customer account not associated with this user."));

            try
            {
                var q = await _quotations.GetQuotationByIdAsync(id);
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

        [AllowAnonymous]
        [HttpPost("api/quotation/{id:int}/decline")]
        [HttpPost("api/quotation/{id:int}/versions/{version:int}/decline")]
        [HttpPost("api/quotations/{id:int}/decline")]
        [HttpPost("api/quotations/{id:int}/versions/{version:int}/decline")]
        public async Task<IActionResult> DeclineQuotation(int id, [FromBody] DeclineQuotationRequest request, [FromRoute] int version = 1, [FromHeader(Name = "x-user-email")] string? actorEmail = null)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Note))
                return BadRequest(ApiResponse.Fail("Decline reason note is mandatory."));

            var email = ResolveUserEmail(actorEmail);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(ApiResponse.Fail("User identity required."));

            var userRow = await _db.GetUserByEmailAsync(email);
            if (userRow == null) return Unauthorized(ApiResponse.Fail("User not found."));

            int customerId = userRow.TryGetValue("CustomerId", out var cid) && cid != null ? Convert.ToInt32(cid) : 0;
            int? userId = userRow.TryGetValue("Id", out var uid) && uid != null ? Convert.ToInt32(uid) : (int?)null;

            if (customerId <= 0)
                return BadRequest(ApiResponse.Fail("Customer account not associated with this user."));

            try
            {
                var q = await _quotations.GetQuotationByIdAsync(id);
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
        [HttpPost("api/quotation/{id:int}/versions")]
        [HttpPost("api/quotations/{id:int}/create-version")]
        [HttpPost("api/quotations/{id:int}/versions")]
        public async Task<IActionResult> CreateNewVersion(int id, [FromHeader(Name = "x-user-email")] string? actorEmail = null)
        {
            var email = ResolveUserEmail(actorEmail);
            int? actorId = null;
            if (!string.IsNullOrWhiteSpace(email))
            {
                var actorRow = await _db.GetUserByEmailAsync(email);
                actorId = actorRow?.TryGetValue("Id", out var aid) == true ? System.Convert.ToInt32(aid) : (int?)null;
            }

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

        [AllowAnonymous]
        [HttpGet("api/quotation/{id:int}/versions")]
        [HttpGet("api/quotations/{id:int}/versions")]
        public async Task<IActionResult> GetVersions(int id)
        {
            var res = await _quotations.GetVersionsAsync(id);
            return Ok(ApiResponse.Ok(res));
        }

        [AllowAnonymous]
        [HttpGet("api/quotation/{id:int}/versions/{version:int}")]
        [HttpGet("api/quotations/{id:int}/versions/{version:int}")]
        public async Task<IActionResult> GetVersionById(int id, int version)
        {
            var versions = await _quotations.GetVersionsAsync(id);
            var specificVersion = versions.FirstOrDefault(v => v.Version == version);
            if (specificVersion == null)
            {
                specificVersion = await _quotations.GetQuotationByIdAsync(version);
            }
            if (specificVersion == null) return NotFound(ApiResponse.Fail("Quotation version not found."));
            return Ok(ApiResponse.Ok(specificVersion));
        }

        [AllowAnonymous]
        [HttpGet("api/quotation/activities")]
        [HttpGet("api/quotations/activities")]
        public async Task<IActionResult> GetActivities([FromQuery] int? quotationId = null, [FromQuery] int limit = 20)
        {
            var res = await _quotations.GetActivitiesAsync(quotationId, limit);
            return Ok(ApiResponse.Ok(res));
        }

        [HttpPost("api/quotation/{id:int}/send")]
        [HttpPost("api/quotations/{id:int}/send")]
        [HttpPost("api/quotation/{id:int}/versions/{version:int}/send")]
        [HttpPost("api/quotations/{id:int}/versions/{version:int}/send")]
        public async Task<IActionResult> SendQuotation(int id, [FromRoute] int version = 1, [FromHeader(Name = "x-user-email")] string? actorEmail = null)
        {
            var email = ResolveUserEmail(actorEmail);
            int? actorId = null;
            if (!string.IsNullOrWhiteSpace(email))
            {
                var actorRow = await _db.GetUserByEmailAsync(email);
                actorId = actorRow?.TryGetValue("Id", out var aid) == true ? System.Convert.ToInt32(aid) : (int?)null;
            }

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
