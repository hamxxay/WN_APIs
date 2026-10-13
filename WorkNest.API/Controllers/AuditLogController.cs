using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.AuditLog;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Read-only Audit Log controller.
    /// Strictly visible and accessible ONLY to super_admin role.
    /// Non-super_admin users (admin, sales_executive, general) receive 403 Forbidden.
    /// </summary>
    [ApiController]
    [Route("api/audit-log")]
    [Authorize(Roles = Roles.SuperAdmin + ",SuperAdmin")]
    public class AuditLogController : ControllerBase
    {
        private readonly IAuditLogRepository _repository;
        private readonly ILogger<AuditLogController> _logger;

        public AuditLogController(IAuditLogRepository repository, ILogger<AuditLogController> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves a paginated list of audit logs based on the provided filters.
        /// Server-side pagination defaults to 50 items and is capped at 200 items.
        /// </summary>
        [HttpGet("")]
        [HttpGet("list")]
        public async Task<IActionResult> GetAuditLogs([FromQuery] AuditLogFilterDto filter, CancellationToken ct)
        {
            var result = await _repository.GetPagedAuditLogsAsync(filter, ct);
            return Ok(new
            {
                isSuccessful = true,
                data = result
            });
        }

        /// <summary>
        /// Retrieves complete audit log details for a single record by AuditId.
        /// Loaded on demand for modal / expandable view.
        /// </summary>
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetAuditLogDetail(long id, CancellationToken ct)
        {
            var detail = await _repository.GetAuditLogDetailAsync(id, ct);
            if (detail == null)
            {
                return NotFound(new
                {
                    isSuccessful = false,
                    message = $"Audit log with ID {id} was not found."
                });
            }

            return Ok(new
            {
                isSuccessful = true,
                data = detail
            });
        }

        /// <summary>
        /// Retrieves distinct entity names and source applications for filter dropdowns.
        /// </summary>
        [HttpGet("filter-options")]
        public async Task<IActionResult> GetFilterOptions(CancellationToken ct)
        {
            var options = await _repository.GetFilterOptionsAsync(ct);
            return Ok(new
            {
                isSuccessful = true,
                data = options
            });
        }
    }
}
