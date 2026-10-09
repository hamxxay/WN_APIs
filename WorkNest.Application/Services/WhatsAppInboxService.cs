using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.WhatsApp;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// WhatsApp Inbox (staff) and every message the system sends outside the bot's own replies.
    /// WhatsApp only allows plain messages within 24 hours of the customer's last message; after that the
    /// pre-approved template WhatsApp:AgentTemplate (default "worknest_agent_message", body {{1}} name, {{2}} text) is used.
    /// </summary>
    public class WhatsAppInboxService : IWhatsAppInboxService
    {
        private readonly IWhatsAppRepository _repo;
        private readonly IWhatsAppClient _client;
        private readonly IConfiguration _config;

        private const int MaxMessageLength = 4000;
        private const int MaxBroadcastRecipients = 1000;

        public WhatsAppInboxService(IWhatsAppRepository repo, IWhatsAppClient client, IConfiguration config)
        {
            _repo = repo;
            _client = client;
            _config = config;
        }

        private string AgentTemplate => string.IsNullOrWhiteSpace(_config["WhatsApp:AgentTemplate"]) ? "worknest_agent_message" : _config["WhatsApp:AgentTemplate"]!;

        public Task<(List<WhatsAppConversationDto> Items, int Total)> GetConversationsAsync(int page, int limit, string? search) =>
            _repo.GetConversationsAsync(Math.Max(1, page), Math.Clamp(limit, 1, 100), string.IsNullOrWhiteSpace(search) ? null : search.Trim());

        public async Task<List<WhatsAppMessageDto>?> GetMessagesAsync(int conversationId, long? beforeId, int limit)
        {
            if (await _repo.GetConversationAsync(conversationId) == null) return null;
            var messages = await _repo.GetMessagesAsync(conversationId, beforeId, Math.Clamp(limit, 1, 200));
            if (beforeId == null) await _repo.MarkReadAsync(conversationId);   // staff opened the conversation
            return messages;
        }

        public async Task<ApiResponse> ReplyAsync(int conversationId, string message, int? userId)
        {
            var text = (message ?? "").Trim();
            if (text.Length == 0) return ApiResponse.Fail("Type a message.");
            if (text.Length > MaxMessageLength) return ApiResponse.Fail($"The message can be at most {MaxMessageLength} characters.");
            var conv = await _repo.GetConversationAsync(conversationId);
            if (conv == null) return ApiResponse.Fail("Conversation not found.");
            var (ok, method, error) = await SendToPhoneAsync(conv.PhoneNumber, null, text, userId);
            return ok ? ApiResponse.Ok(new { method }, method == "template" ? "Sent (as a template: the 24-hour window has closed)." : "Sent.")
                      : ApiResponse.Fail(error ?? "The message could not be sent.");
        }

        public async Task<ApiResponse> StartConversationAsync(WhatsAppStartConversationRequest request, int? userId)
        {
            var phone = ComplaintService.NormalizePhone(request.Phone);
            if (phone == null || phone.Length < 10) return ApiResponse.Fail("Enter a valid WhatsApp number (e.g. 03001234567).");
            var text = (request.Message ?? "").Trim();
            if (text.Length == 0) return ApiResponse.Fail("Type a message.");
            if (text.Length > MaxMessageLength) return ApiResponse.Fail($"The message can be at most {MaxMessageLength} characters.");
            var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
            var (ok, method, error) = await SendToPhoneAsync(phone, name, text, userId);
            if (!ok) return ApiResponse.Fail(error ?? "The message could not be sent.");
            var conv = await _repo.GetConversationByPhoneAsync(phone);
            return ApiResponse.Ok(new { conversationId = conv?.Id, method }, "Sent.");
        }

        public async Task<ApiResponse> UpdateContactAsync(int contactId, WhatsAppContactMetaRequest request)
        {
            if (request.Name is { Length: > 200 }) return ApiResponse.Fail("The name can be at most 200 characters.");
            if (request.Tags is { Length: > 500 }) return ApiResponse.Fail("Tags can be at most 500 characters.");
            if (request.AdminNotes is { Length: > 2000 }) return ApiResponse.Fail("Notes can be at most 2000 characters.");
            await _repo.UpdateContactMetaAsync(contactId, request.Name?.Trim(), request.Tags?.Trim(), request.AdminNotes?.Trim());
            return ApiResponse.Ok("Contact updated.");
        }

        public async Task<ApiResponse> BroadcastAsync(WhatsAppBroadcastRequest request, int? userId)
        {
            var text = (request.Message ?? "").Trim();
            if (text.Length == 0) return ApiResponse.Fail("Type the broadcast message.");
            if (text.Length > MaxMessageLength) return ApiResponse.Fail($"The message can be at most {MaxMessageLength} characters.");
            if (!_client.IsConfigured) return ApiResponse.Fail("WhatsApp is not configured on the server.");
            var tag = string.IsNullOrWhiteSpace(request.Tag) || request.Tag.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) ? null : request.Tag.Trim();

            var targets = await _repo.GetBroadcastTargetsAsync(tag);
            if (targets.Count == 0) return ApiResponse.Fail(tag == null ? "No WhatsApp contacts yet." : $"No contacts are tagged '{tag}'.");
            if (targets.Count > MaxBroadcastRecipients) targets = targets.Take(MaxBroadcastRecipients).ToList();

            int sent = 0, failed = 0;
            var failures = new List<object>();
            foreach (var t in targets)
            {
                var (ok, _, error) = await SendToPhoneAsync(t.PhoneNumber, null, text, userId);
                if (ok) sent++;
                else { failed++; if (failures.Count < 20) failures.Add(new { phone = t.PhoneNumber, name = t.Name, error }); }
            }
            return ApiResponse.Ok(new { sent, failed, failures }, $"Broadcast sent to {sent} contact(s)" + (failed > 0 ? $", {failed} failed." : "."));
        }

        public async Task<(bool Ok, string Method, string? Error)> SendToPhoneAsync(string phone, string? name, string message, int? userId)
        {
            var to = ComplaintService.NormalizePhone(phone);
            if (to == null) return (false, "", "No valid phone number.");
            if (!_client.IsConfigured) return (false, "", "WhatsApp is not configured on the server.");

            var (_, conversationId) = await _repo.EnsureConversationAsync(to, name);
            var conv = await _repo.GetConversationAsync(conversationId);
            var displayName = conv?.Name ?? name ?? "Valued Customer";

            string method = "text";
            WhatsAppSendResult result;
            if (conv?.InReplyWindow == true)
            {
                result = await _client.SendTextAsync(to, message);
                if (!result.Ok && (result.ErrorCode == 131047 || (result.Error ?? "").Contains("24 hours", StringComparison.OrdinalIgnoreCase)))
                {
                    method = "template";
                    result = await _client.SendTemplateAsync(to, AgentTemplate, new[] { displayName, message });
                }
            }
            else
            {
                method = "template";
                result = await _client.SendTemplateAsync(to, AgentTemplate, new[] { displayName, message });
            }

            if (!result.Ok)
            {
                var error = method == "template" && (result.ErrorCode is 100 or 132000 or 132001 or 132015 || (result.Error ?? "").Contains("template", StringComparison.OrdinalIgnoreCase))
                    ? $"The customer hasn't written in the last 24 hours, and the WhatsApp template '{AgentTemplate}' is not approved by Meta yet."
                    : "WhatsApp did not accept the message.";
                return (false, method, error);
            }

            await _repo.AddMessageAsync(conversationId, "outgoing", method, message, result.MessageId, userId);
            return (true, method, null);
        }
    }

    /// <summary>Complaint resolved: the customer is asked on WhatsApp whether the issue is really fixed.</summary>
    public class WhatsAppComplaintNotifier : IChatbotNotifier
    {
        private readonly IWhatsAppInboxService _inbox;
        public WhatsAppComplaintNotifier(IWhatsAppInboxService inbox) => _inbox = inbox;

        public async Task<(bool Sent, string? Error)> SendComplaintResolvedAsync(string phone, string complaintNo)
        {
            var message =
                "WorkNest Complaint Update\n\n" +
                $"We are pleased to confirm that your complaint {complaintNo} has been resolved.\n\n" +
                "To help us verify this, please let us know whether the issue is still occurring.\n\n" +
                "1. Yes, the issue persists\n" +
                "2. No, the issue is resolved\n\n" +
                "Please reply with 1 or 2.";
            var (ok, _, error) = await _inbox.SendToPhoneAsync(phone, null, message, null);
            return (ok, error);
        }
    }
}
