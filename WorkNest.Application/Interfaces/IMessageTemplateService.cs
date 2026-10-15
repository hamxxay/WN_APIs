using WorkNest.Application.DTOs.MessageTemplate;
using WorkNest.Application.Helpers;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// dbo.WN_MessageTemplates through dbo.WN_MessageTemplates_Get / _Save (script WT_01_whatsapp_template). Before
    /// WT_01 has run (SQL errors 208 / 2812) both throw <see cref="MessageTemplatesMissingException"/>.
    /// </summary>
    public interface IMessageTemplateRepository
    {
        /// <summary>The saved template; null when there is no row for the key.</summary>
        Task<MessageTemplateRow?> GetAsync(string templateKey);
        /// <summary>Saves (insert or update) and returns the saved row.</summary>
        Task<MessageTemplateRow?> SaveAsync(string templateKey, string template, int? updatedById);
    }

    /// <summary>The database update WT_01_whatsapp_template has not run: no table / procedures.</summary>
    public sealed class MessageTemplatesMissingException : Exception
    {
        public const string SaveMessage = "Saving the WhatsApp template needs the database update WT_01_whatsapp_template.";

        public MessageTemplatesMissingException(Exception inner) : base(SaveMessage, inner) { }
    }

    /// <summary>
    /// Message templates admins edit in the CMS (whatsapp-template contract): for now the tour inquiry WhatsApp message
    /// (key inquiry_whatsapp), filled in by <see cref="InquiryMessageRenderer"/>. Before WT_01 the default wording from
    /// code is used and saving answers 400.
    /// </summary>
    public interface IMessageTemplateService
    {
        /// <summary>The template (saved wording or the default), default wording, placeholders, last save. 404 for an unknown key.</summary>
        Task<MessageTemplateOutcome> GetAsync(string key);
        /// <summary>Checks and saves the wording (400 with a plain-language reason; 400 before WT_01). Returns it as GET does.</summary>
        Task<MessageTemplateOutcome> SaveAsync(string key, string? template, string? actorEmail);
        /// <summary>{ text }: <paramref name="template"/> (empty = the one in use) filled in for a sample inquiry.</summary>
        Task<MessageTemplateOutcome> PreviewAsync(string key, string? template, string? actorEmail);
        /// <summary>
        /// { phone, text, url } for the tour inquiry with this public id, for the signed-in staff member; 404 when it is
        /// not among the inquiries <paramref name="branch"/> lets them see.
        /// </summary>
        Task<MessageTemplateOutcome> InquiryWhatsAppAsync(Guid publicId, BranchFilter? branch, string? actorEmail);
    }
}
