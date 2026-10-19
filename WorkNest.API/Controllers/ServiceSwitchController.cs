using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using WorkNest.API.Extensions;
using WorkNest.Application.DTOs.ServiceSwitch;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Common.Constants;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Background service on/off switches (CMS page Background Services), super admin only. Off pauses the service's work
    /// without stopping the API; on resumes it, with no restart (the services read the switches every 10 seconds at most).
    /// dbo.WN_ServiceSwitches (script SS_01_service_switches); before it is run both calls answer 503 "Database update
    /// needed: run the script SS_01_service_switches_TEST ...". Changes are recorded in dbo.WN_AuditLog with who made them.
    /// </summary>
    [ApiController]
    [Route("api/admin/services")]
    [Authorize(Roles = SuperAdminRoles)]
    public class ServiceSwitchController : ControllerBase
    {
        public const string SuperAdminRoles = Roles.SuperAdmin + ",SuperAdmin";
        public const string UnknownServiceMessage = "Unknown service.";
        public const string MissingBodyMessage = "Send isEnabled: true to turn the service on or false to turn it off.";

        private readonly IServiceSwitch _switches;

        public ServiceSwitchController(IServiceSwitch switches) => _switches = switches;

        /// <summary>
        /// Every background service: [{ name, displayName, description, isEnabled, configKey, configEnabled, running,
        /// updatedBy, updatedAt }].
        /// </summary>
        [HttpGet("")]
        [Authorize(Roles = SuperAdminRoles)]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            try
            {
                return Ok(ApiResponse.Ok(await _switches.ListAsync(ct)));
            }
            catch (ServiceSwitchesMissingException)
            {
                return DatabaseUpdateNeeded();
            }
        }

        /// <summary>{ "isEnabled": true | false } turns the service on or off; answers the service as the list shows it.</summary>
        [HttpPost("{name}")]
        [Authorize(Roles = SuperAdminRoles)]
        public async Task<IActionResult> Set(string name,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ServiceSwitchRequest? request, CancellationToken ct)
        {
            var info = BackgroundServiceCatalog.Find(name);
            if (info == null)
                return NotFound(ApiResponse.Fail(UnknownServiceMessage));
            if (request?.IsEnabled is not bool enabled)
                return BadRequest(ApiResponse.Fail(MissingBodyMessage));

            try
            {
                var item = await _switches.SetAsync(info.Name, enabled, User.GetEmail() ?? User.Identity?.Name, ct);
                if (item == null) // in the catalogue but not in the table
                    return NotFound(ApiResponse.Fail(UnknownServiceMessage));
                return Ok(ApiResponse.Ok(item, $"{info.DisplayName} turned {(enabled ? "on" : "off")}."));
            }
            catch (ServiceSwitchesMissingException)
            {
                return DatabaseUpdateNeeded();
            }
        }

        private IActionResult DatabaseUpdateNeeded() =>
            StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(ServiceSwitchesMissingException.UserMessage));
    }
}
