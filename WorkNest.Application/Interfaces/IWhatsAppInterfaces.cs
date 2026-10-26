using WorkNest.Application.DTOs.WhatsApp;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>WhatsApp Cloud API (Meta Graph API). Configured by WhatsApp:Token / WhatsApp:PhoneNumberId.</summary>
    public interface IWhatsAppClient
    {
        bool IsConfigured { get; }
        Task<WhatsAppSendResult> SendTextAsync(string to, string text);
        /// <summary>Pre-approved template (needed outside the 24-hour window), body parameters in order.</summary>
        Task<WhatsAppSendResult> SendTemplateAsync(string to, string templateName, IReadOnlyList<string> bodyParameters);
        Task<WhatsAppSendResult> SendLocationAsync(string to, decimal latitude, decimal longitude, string name, string address);
    }

    /// <summary>Data access for the WN_WhatsApp_* tables (created by WN_WhatsApp.txt).</summary>
    public interface IWhatsAppRepository
    {
        /// <summary>Contact + conversation for this number (created when missing); the name is updated when given.</summary>
        Task<(int ContactId, int ConversationId)> EnsureConversationAsync(string phone, string? name);
        /// <summary>Saves a message. False when this WhatsApp message id was already saved (Meta re-delivery).</summary>
        Task<bool> AddMessageAsync(int conversationId, string direction, string messageType, string? text, string? whatsAppMessageId, int? sentByUserId);
        /// <summary>Where the customer is in the bot menu, and how many minutes ago they last wrote to the bot.</summary>
        Task<(string State, string? Data, int MinutesSinceUpdate)?> GetBotStateAsync(string phone);
        Task SaveBotStateAsync(string phone, string state, string? data);

        Task<(List<WhatsAppConversationDto> Items, int Total)> GetConversationsAsync(int page, int limit, string? search);
        Task<WhatsAppConversationDto?> GetConversationAsync(int id);
        Task<WhatsAppConversationDto?> GetConversationByPhoneAsync(string phone);
        Task<List<WhatsAppMessageDto>> GetMessagesAsync(int conversationId, long? beforeId, int limit);
        Task MarkReadAsync(int conversationId);
        Task UpdateContactMetaAsync(int contactId, string? name, string? tags, string? adminNotes);
        Task<List<WhatsAppConversationDto>> GetBroadcastTargetsAsync(string? tag);
        /// <summary>Conversations with unread customer messages (sidebar badge).</summary>
        Task<List<(int ConversationId, DateTime LastMessageAt)>> GetUnreadConversationsAsync();

        // ---- Simple inquiries: the "sales representative" follow-up (INQ_01; every call is a no-op before INQ_01) ----

        /// <summary>Free text at the main menu that is not an option: starts the pending state (first time) and adds the text.
        /// Nothing happens when a follow-up was sent in the last 24 hours.</summary>
        Task SetFollowUpPendingAsync(int conversationId, string text);
        /// <summary>A valid menu choice / any other bot flow: forgets the pending state.</summary>
        Task ClearFollowUpPendingAsync(int conversationId);
        /// <summary>Every conversation with a pending follow-up (empty before INQ_01).</summary>
        Task<List<WhatsAppFollowUpCandidate>> GetFollowUpCandidatesAsync();
        /// <summary>Takes one due conversation for this instance (row lock + lease) and creates its general inquiry once.
        /// Null before INQ_01.</summary>
        Task<WhatsAppFollowUpClaim?> ClaimFollowUpAsync(int conversationId, int quietSeconds, int leaseSeconds);
        /// <summary>The follow-up was sent: records it (24-hour cap) and ends the pending state.</summary>
        Task CompleteFollowUpAsync(int conversationId);
        /// <summary>The send failed: frees the conversation for the next tick (its inquiry is kept, not created again).</summary>
        Task ReleaseFollowUpAsync(int conversationId);
    }

    /// <summary>Runs the WorkNest WhatsApp bot for one incoming message (menu, Book a Tour, complaints, directions).</summary>
    public interface IWhatsAppBotService
    {
        Task HandleIncomingAsync(string phone, string? profileName, string? whatsAppMessageId, string messageType, string? text);
    }

    /// <summary>WhatsApp Inbox: conversations, staff replies, broadcasts, and messages sent by the system.</summary>
    public interface IWhatsAppInboxService
    {
        Task<(List<WhatsAppConversationDto> Items, int Total)> GetConversationsAsync(int page, int limit, string? search);
        Task<List<WhatsAppMessageDto>?> GetMessagesAsync(int conversationId, long? beforeId, int limit);
        Task<ApiResponse> ReplyAsync(int conversationId, string message, int? userId);
        Task<ApiResponse> StartConversationAsync(WhatsAppStartConversationRequest request, int? userId);
        Task<ApiResponse> UpdateContactAsync(int contactId, WhatsAppContactMetaRequest request);
        Task<ApiResponse> BroadcastAsync(WhatsAppBroadcastRequest request, int? userId);
        /// <summary>
        /// Sends a message to a number and records it: plain text inside the 24-hour window, otherwise the
        /// approved "agent message" template. Used by staff replies and system messages (complaint resolved).
        /// </summary>
        Task<(bool Ok, string Method, string? Error)> SendToPhoneAsync(string phone, string? name, string message, int? userId);
    }
}
