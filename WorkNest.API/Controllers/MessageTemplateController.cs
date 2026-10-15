using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.MessageTemplate;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Message templates (whatsapp-template contract, 15 Oct 2026). The tour inquiry WhatsApp message (key
    /// inquiry_whatsapp) is not hardcoded: admins edit its wording in the CMS (dbo.WN_MessageTemplates, script
    /// WT_01_whatsapp_template) and the API fills it in for each inquiry and the signed-in staff member
    /// (GET api/contact/{publicId}/whatsapp-message, used by the staff app and the CMS Tour Inquiries WhatsApp icon).
    /// Placeholders {name} {staff} {branch} {date} {source} {workspace} {seats}; [ ... ] = optional part, dropped when a
    /// placeholder in it has no value. Before WT_01 the default wording from code is used and saving answers 400.
    /// Roles: reading = the staff who see tour inquiries (admin, super admin, sales executive, receptionist, sales
    /// manager); saving = admin and super admin.
    /// </summary>
    [ApiController]
    [Authorize]
    public class MessageTemplateController : ControllerBase
    {
        /// <summary>The staff who see tour inquiries (the Sales Manager through the dashboard drill-downs).</summary>
        public const string ViewerRoles =
            "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive,sales_manager,SalesManager";
        public const string EditorRoles = "admin,Admin,super_admin,SuperAdmin";

        private readonly IMessageTemplateService _templates;

        public MessageTemplateController(IMessageTemplateService templates) => _templates = templates;

        /// <summary>{ key, template, defaultTemplate, placeholders: [{ name, description }], updatedOn, updatedBy, needsDatabaseUpdate }.</summary>
        [HttpGet("api/message-templates/{key}")]
        [Authorize(Roles = ViewerRoles)]
        [SalesManagerAllowed]
        public async Task<IActionResult> Get(string key) => Answer(await _templates.GetAsync(key));

        /// <summary>
        /// { "template": "..." }: not empty, at most 2000 characters, only known placeholders, every [ closed by ]
        /// (plain-language 400 otherwise). Answers the saved template as GET does. Before WT_01: 400.
        /// </summary>
        [HttpPut("api/message-templates/{key}")]
        [Authorize(Roles = EditorRoles)]
        public async Task<IActionResult> Save(string key,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MessageTemplateRequest? request) =>
            Answer(await _templates.SaveAsync(key, request?.Template, User.GetEmail()));

        /// <summary>{ "template": "..." } (optional; empty = the wording in use) -> { text } filled in for a sample inquiry.</summary>
        [HttpPost("api/message-templates/{key}/preview")]
        [Authorize(Roles = ViewerRoles)]
        [SalesManagerAllowed]
        public async Task<IActionResult> Preview(string key,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MessageTemplateRequest? request) =>
            Answer(await _templates.PreviewAsync(key, request?.Template, User.GetEmail()));

        /// <summary>
        /// { phone, text, url }: the WhatsApp message for this tour inquiry, filled in for the signed-in staff member;
        /// url = https://wa.me/&lt;phone&gt;?text=... Same visibility as the Tour Inquiries list: location-bound staff see
        /// their branches plus the inquiries without one; 404 when the user can't see it.
        /// </summary>
        [HttpGet("api/contact/{publicId:guid}/whatsapp-message")]
        [Authorize(Roles = ViewerRoles)]
        [SalesManagerAllowed]
        public async Task<IActionResult> InquiryWhatsApp(Guid publicId) =>
            Answer(await _templates.InquiryWhatsAppAsync(publicId, User.GetBranchScope().ForList(null), User.GetEmail()));

        private IActionResult Answer(MessageTemplateOutcome outcome) =>
            outcome.IsSuccessful
                ? Ok(ApiResponse.Ok(outcome.Data, outcome.Message))
                : StatusCode(outcome.StatusCode, ApiResponse.Fail(outcome.Message));
    }
}
