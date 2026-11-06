using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Services;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>Maps an approvals service result to the status codes of the API contract: 200, 400, 403, 404, 409 and 503.</summary>
    internal static class ApprovalReply
    {
        public static IActionResult From<T>(ControllerBase c, ApprovalResult<T> r, Func<T, object?> map, string? okMessage = null)
        {
            var message = r.Message ?? "";
            return r.Outcome switch
            {
                ApprovalOutcome.Ok => c.Ok(okMessage == null ? ApiResponse.Ok(map(r.Data!)) : ApiResponse.Ok(map(r.Data!), okMessage)),
                ApprovalOutcome.Invalid => c.BadRequest(ApiResponse.Fail(message)),
                ApprovalOutcome.Forbidden => c.StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail(message)),
                ApprovalOutcome.NotFound => c.NotFound(ApiResponse.Fail(message)),
                ApprovalOutcome.Conflict => c.Conflict(ApiResponse.Fail(message)),
                _ => c.StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(message))
            };
        }
    }
}
