using WorkNest.Application.DTOs.MessageTemplate;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Message templates (whatsapp-template contract, 15 Oct 2026): the tour inquiry WhatsApp message admins edit in the
    /// CMS, stored in dbo.WN_MessageTemplates (script WT_01_whatsapp_template) and filled in by
    /// <see cref="InquiryMessageRenderer"/> for the CMS preview and for each inquiry (staff app / CMS WhatsApp icon).
    /// Before WT_01 the default wording from code is used everywhere and saving answers 400.
    /// </summary>
    public sealed class MessageTemplateService : IMessageTemplateService
    {
        public const string UnknownTemplate = "Unknown message template.";
        public const string InquiryNotFound = "Inquiry not found.";

        private readonly IMessageTemplateRepository _templates;
        private readonly IDbRepository _db;
        private readonly IBusinessClock _clock;

        public MessageTemplateService(IMessageTemplateRepository templates, IDbRepository db, IBusinessClock clock)
        {
            _templates = templates;
            _db = db;
            _clock = clock;
        }

        /// <summary>The known key ("inquiry_whatsapp", any case), else null.</summary>
        private static string? KnownKey(string? key) =>
            string.Equals((key ?? "").Trim(), InquiryMessageRenderer.TemplateKey, StringComparison.OrdinalIgnoreCase)
                ? InquiryMessageRenderer.TemplateKey
                : null;

        public async Task<MessageTemplateOutcome> GetAsync(string key)
        {
            var known = KnownKey(key);
            if (known is null) return MessageTemplateOutcome.Fail(404, UnknownTemplate);
            var (row, ready) = await ReadAsync(known);
            return MessageTemplateOutcome.Ok(View(known, row, ready));
        }

        public async Task<MessageTemplateOutcome> SaveAsync(string key, string? template, string? actorEmail)
        {
            var known = KnownKey(key);
            if (known is null) return MessageTemplateOutcome.Fail(404, UnknownTemplate);
            var wording = Normalize(template);
            var error = InquiryMessageRenderer.Validate(wording);
            if (error is not null) return MessageTemplateOutcome.Fail(400, error);

            int? actorId = string.IsNullOrWhiteSpace(actorEmail) ? null : (await _db.GetUserIdByEmailAsync(actorEmail)).Id;
            MessageTemplateRow? saved;
            try { saved = await _templates.SaveAsync(known, wording!, actorId); }
            catch (MessageTemplatesMissingException) { return MessageTemplateOutcome.Fail(400, MessageTemplatesMissingException.SaveMessage); }
            return MessageTemplateOutcome.Ok(View(known, saved ?? new MessageTemplateRow { TemplateKey = known, Template = wording! }, true),
                "Message template saved.");
        }

        public async Task<MessageTemplateOutcome> PreviewAsync(string key, string? template, string? actorEmail)
        {
            var known = KnownKey(key);
            if (known is null) return MessageTemplateOutcome.Fail(404, UnknownTemplate);
            string wording;
            if (string.IsNullOrWhiteSpace(template))
            {
                wording = InUse((await ReadAsync(known)).Row);
            }
            else
            {
                wording = Normalize(template)!;
                var error = InquiryMessageRenderer.Validate(wording);
                if (error is not null) return MessageTemplateOutcome.Fail(400, error);
            }

            var (staffName, email) = await StaffAsync(actorEmail);
            var values = InquiryMessageRenderer.FromInquiry(await SampleInquiryAsync(), staffName, email, _clock.Today, _clock.FromUtc);
            return MessageTemplateOutcome.Ok(new MessagePreview { Text = InquiryMessageRenderer.Render(wording, values) });
        }

        public async Task<MessageTemplateOutcome> InquiryWhatsAppAsync(Guid publicId, BranchFilter? branch, string? actorEmail)
        {
            // The same rows the Tour Inquiries page lists for this user (their branches + those without one).
            var (rows, _) = await _db.GetContactsAsync(1, 10000, null, null, branch);
            var id = publicId.ToString();
            var row = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g is not null && g is not DBNull
                                               && string.Equals(g.ToString(), id, StringComparison.OrdinalIgnoreCase));
            if (row is null) return MessageTemplateOutcome.Fail(404, InquiryNotFound);

            var wording = InUse((await ReadAsync(InquiryMessageRenderer.TemplateKey)).Row);
            var (staffName, email) = await StaffAsync(actorEmail);
            var values = InquiryMessageRenderer.FromInquiry(row, staffName, email, _clock.Today, _clock.FromUtc);
            var text = InquiryMessageRenderer.Render(wording, values);
            var phone = InquiryMessageRenderer.WhatsAppNumber(row.TryGetValue("PhoneNumber", out var p) ? p?.ToString() : null);
            return MessageTemplateOutcome.Ok(new InquiryWhatsAppMessage
            {
                Phone = phone.Length == 0 ? null : phone,
                Text = text,
                Url = InquiryMessageRenderer.WhatsAppUrl(phone, text)
            });
        }

        // ---- helpers -----------------------------------------------------------------------------------------------

        /// <summary>The saved row (null = none) and whether WT_01 has run.</summary>
        private async Task<(MessageTemplateRow? Row, bool Ready)> ReadAsync(string key)
        {
            try { return (await _templates.GetAsync(key), true); }
            catch (MessageTemplatesMissingException) { return (null, false); }
        }

        private static string InUse(MessageTemplateRow? row) =>
            string.IsNullOrWhiteSpace(row?.Template) ? InquiryMessageRenderer.DefaultTemplate : row!.Template;

        private static MessageTemplateView View(string key, MessageTemplateRow? row, bool ready) => new()
        {
            Key = key,
            Template = InUse(row),
            DefaultTemplate = InquiryMessageRenderer.DefaultTemplate,
            Placeholders = InquiryMessageRenderer.Placeholders,
            UpdatedOn = row?.UpdatedById is null && row?.UpdatedByName is null ? null : row?.UpdatedOn,
            UpdatedBy = row?.UpdatedByName,
            NeedsDatabaseUpdate = !ready
        };

        /// <summary>Line ends as "\n", trimmed; null when empty.</summary>
        private static string? Normalize(string? template)
        {
            if (template is null) return null;
            var text = template.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            return text.Length == 0 ? null : text;
        }

        /// <summary>The signed-in staff member's WN_Users.Name and email.</summary>
        private async Task<(string? Name, string? Email)> StaffAsync(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return (null, null);
            var user = await _db.GetUserByEmailAsync(email);
            var name = user is not null && user.TryGetValue("Name", out var n) && n is not null && n is not DBNull ? n.ToString() : null;
            return (name, email);
        }

        /// <summary>A sample inquiry for the CMS preview: a WhatsApp bot inquiry for the first active branch, today.</summary>
        private async Task<IDictionary<string, object?>> SampleInquiryAsync()
        {
            string branch;
            try { branch = BranchScope.Options(await _db.GetAllLocationsAsync()).Select(l => l.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "I-8"; }
            catch { branch = "I-8"; }
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "Ali Khan",
                ["PhoneNumber"] = "03001234567",
                ["Message"] = $"WhatsApp Book a Tour | Branch: {branch} | Workspace: Private Office | Seats / Persons: 4",
                ["InquiryType"] = "WhatsApp",
                ["ReferenceName"] = null,
                ["LocationName"] = branch,
                ["CreatedOn"] = _clock.Today,
            };
        }
    }
}
