using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.WhatsApp;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;
using WorkNest.API.Extensions;

namespace WorkNest.API.Controllers
{
    /// <summary>WhatsApp Inbox page: conversations with customers, staff replies and broadcasts.</summary>
    [ApiController]
    [Authorize(Roles = StaffRoles)]
    public class WhatsAppInboxController : ControllerBase
    {
        private const string StaffRoles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive";
        private const string BroadcastRoles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive";
        private readonly IWhatsAppInboxService _inbox;
        private readonly IDbRepository _db;

        public WhatsAppInboxController(IWhatsAppInboxService inbox, IDbRepository db)
        {
            _inbox = inbox;
            _db = db;
        }

        [HttpGet("api/whatsapp/conversations")]
        public async Task<IActionResult> Conversations([FromQuery] int page = 1, [FromQuery] int limit = 30, [FromQuery] string? search = null)
        {
            var (items, total) = await _inbox.GetConversationsAsync(page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items.Cast<object>(), Total = total });
        }

        /// <summary>Messages, oldest first. Without beforeId the newest page is returned and the conversation is marked read.</summary>
        [HttpGet("api/whatsapp/conversations/{id:int}/messages")]
        public async Task<IActionResult> Messages(int id, [FromQuery] long? beforeId = null, [FromQuery] int limit = 50)
        {
            var messages = await _inbox.GetMessagesAsync(id, beforeId, limit);
            return messages == null ? NotFound(ApiResponse.Fail("Conversation not found.")) : Ok(ApiResponse.Ok(messages));
        }

        [HttpPost("api/whatsapp/conversations/{id:int}/messages")]
        public async Task<IActionResult> Reply(int id, [FromBody] WhatsAppSendMessageRequest request)
        {
            var result = await _inbox.ReplyAsync(id, request.Message, await ActorIdAsync());
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        [HttpPost("api/whatsapp/conversations/start")]
        public async Task<IActionResult> Start([FromBody] WhatsAppStartConversationRequest request)
        {
            var result = await _inbox.StartConversationAsync(request, await ActorIdAsync());
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        [HttpPatch("api/whatsapp/contacts/{contactId:int}")]
        public async Task<IActionResult> UpdateContact(int contactId, [FromBody] WhatsAppContactMetaRequest request)
        {
            var result = await _inbox.UpdateContactAsync(contactId, request);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        [HttpPost("api/whatsapp/broadcast")]
        [Authorize(Roles = BroadcastRoles)]
        public async Task<IActionResult> Broadcast([FromBody] WhatsAppBroadcastRequest request)
        {
            var result = await _inbox.BroadcastAsync(request, await ActorIdAsync());
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        private async Task<int?> ActorIdAsync()
        {
            var email = User.GetEmail();
            return string.IsNullOrWhiteSpace(email) ? null : (await _db.GetUserIdByEmailAsync(email)).Item1;
        }
    }
}
