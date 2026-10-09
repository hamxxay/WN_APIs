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
        Task<(string State, string? Data)?> GetBotStateAsync(string phone);
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
