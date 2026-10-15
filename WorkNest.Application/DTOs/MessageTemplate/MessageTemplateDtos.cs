using System.Text.Json.Serialization;
using WorkNest.Application.Helpers;

namespace WorkNest.Application.DTOs.MessageTemplate
{
    /// <summary>PUT api/message-templates/{key} and POST .../preview: { "template": "..." } (preview: optional).</summary>
    public class MessageTemplateRequest
    {
        public string? Template { get; set; }
    }

    /// <summary>GET / PUT api/message-templates/{key}: the template, its default wording and placeholders, last save.</summary>
    public class MessageTemplateView
    {
        public string Key { get; set; } = string.Empty;
        /// <summary>The wording in use: the saved one, else <see cref="DefaultTemplate"/>.</summary>
        public string Template { get; set; } = string.Empty;
        public string DefaultTemplate { get; set; } = string.Empty;
        public IReadOnlyList<MessagePlaceholder> Placeholders { get; set; } = Array.Empty<MessagePlaceholder>();
        /// <summary>Last save (UTC, JSON "Z"); null = never saved by a person (the default wording).</summary>
        public DateTime? UpdatedOn { get; set; }
        /// <summary>Who saved it last: WN_Users.Name, else their email; null = nobody yet.</summary>
        public string? UpdatedBy { get; set; }
        /// <summary>True until the database update WT_01_whatsapp_template has run (saving is refused until then).</summary>
        public bool NeedsDatabaseUpdate { get; set; }
    }

    /// <summary>POST api/message-templates/{key}/preview: the template filled in for a sample inquiry.</summary>
    public class MessagePreview
    {
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>GET api/contact/{publicId}/whatsapp-message: the message for that inquiry, ready for wa.me.</summary>
    public class InquiryWhatsAppMessage
    {
        /// <summary>International digits for wa.me ("923001234567"); null when the inquiry has no phone number.</summary>
        public string? Phone { get; set; }
        public string Text { get; set; } = string.Empty;
        /// <summary>https://wa.me/&lt;phone&gt;?text=&lt;text, URL-encoded&gt;</summary>
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>One row of dbo.WN_MessageTemplates_Get / _Save (script WT_01).</summary>
    public class MessageTemplateRow
    {
        public string TemplateKey { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public int? UpdatedById { get; set; }
        /// <summary>UTC (Kind Utc).</summary>
        public DateTime? UpdatedOn { get; set; }
        /// <summary>WN_Users.Name of UpdatedById, else its email.</summary>
        public string? UpdatedByName { get; set; }
    }

    /// <summary>A service answer: HTTP status, plain-language message and the data.</summary>
    public class MessageTemplateOutcome
    {
        public int StatusCode { get; set; } = 200;
        public string Message { get; set; } = "Success";
        public object? Data { get; set; }
        [JsonIgnore]
        public bool IsSuccessful => StatusCode is >= 200 and < 300;

        public static MessageTemplateOutcome Ok(object? data, string message = "Success") => new() { Data = data, Message = message };
        public static MessageTemplateOutcome Fail(int status, string message) => new() { StatusCode = status, Message = message };
    }
}
