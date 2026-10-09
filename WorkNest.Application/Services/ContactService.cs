using WorkNest.Application.DTOs.Contact;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class ContactService : IContactService
    {
        private readonly IDbRepository _db;
        private readonly IEmailService _email;
        private readonly IBusinessClock _clock;

        // dbo.WN_Contacts.StatusId
        private const byte StatusInProgress = 2, StatusResolved = 3;

        public ContactService(IDbRepository db, IEmailService email, IBusinessClock clock)
        {
            _db = db;
            _email = email;
            _clock = clock;
        }

        public async Task<(IEnumerable<object> Items, int Total)> GetContactsAsync(int page, int limit, string? search)
        {
            var (rows, total) = await _db.GetContactsAsync(page, limit, search);
            var list = rows.ToList();
            // Each inquiry's latest feedback (outcome, reason, follow-up date, quotation) for the Feedback column.
            var ids = list.Select(r => r.TryGetValue("Id", out var v) && v is not null && v is not DBNull ? Convert.ToInt32(v) : 0);
            var feedback = await _db.GetLatestContactFeedbackAsync(ids);
            var today = _clock.Today.Date;
            foreach (var row in list)
            {
                if (!(row.TryGetValue("Id", out var idObj) && idObj is not null && idObj is not DBNull)) continue;
                if (!feedback.TryGetValue(Convert.ToInt32(idObj), out var f)) continue;
                DateTime? followUp = f["FollowUpOn"] is DateTime d ? d.Date : null;
                string outcome = f["Outcome"]?.ToString() ?? "";
                row["FeedbackOutcome"] = outcome;
                row["FeedbackReason"] = f["Reason"] is DBNull ? null : f["Reason"];
                row["FollowUpOn"] = followUp?.ToString("yyyy-MM-dd");
                row["FollowUpDue"] = outcome == "future_prospect" && followUp.HasValue && followUp.Value <= today;
                row["QuotationId"] = f["QuotationId"] is DBNull ? null : f["QuotationId"];
            }
            return (list.Cast<object>(), total);
        }

        public async Task<ApiResponse> CreateChatbotTourInquiryAsync(WorkNest.Application.DTOs.Complaint.ChatbotTourInquiryRequest request)
        {
            var name = (request.Name ?? "").Trim();
            var phone = (request.Phone ?? "").Trim();
            if (name.Length < 2 || name.Length > 200) return ApiResponse.Fail("The name is required (2-200 characters).");
            if (phone.Length < 7 || phone.Length > 20) return ApiResponse.Fail("The phone number is required.");

            var parts = new List<string> { "WhatsApp Book a Tour" };
            if (!string.IsNullOrWhiteSpace(request.Reference)) parts.Add($"Ref: {request.Reference.Trim()}");
            if (!string.IsNullOrWhiteSpace(request.Branch)) parts.Add($"Branch: {request.Branch.Trim()}");
            if (!string.IsNullOrWhiteSpace(request.WorkspaceType)) parts.Add($"Workspace: {request.WorkspaceType.Trim()}");
            if (request.Seats is > 0) parts.Add($"Seats / Persons: {request.Seats}");
            var message = string.Join(" | ", parts);
            if (message.Length > 1000) message = message[..1000];

            // WhatsApp gives no email: stored empty (the Tour Inquiries page shows "-").
            var (newId, publicId) = await _db.InsertContactAsync("book_tour", null, name, "", phone, message);
            _ = Task.Run(async () =>
            {
                try { await _email.SendTourNotificationAsync(name, "", phone, message); }
                catch { }
            });
            return ApiResponse.Ok(new { id = newId, publicId }, "Tour inquiry recorded.");
        }

        public async Task<ApiResponse> SaveFeedbackAsync(int id, ContactFeedbackRequest request, int? actorId)
        {
            var outcome = (request.Outcome ?? "").Trim().ToLowerInvariant();
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            if (reason is { Length: > 1000 }) return ApiResponse.Fail("The reason can be at most 1000 characters.");
            DateTime? followUp = null;
            byte status;
            switch (outcome)
            {
                case "not_interested":
                    if (reason is null) return ApiResponse.Fail("Enter the reason the customer is not interested.");
                    status = StatusResolved;                 // closes the inquiry
                    break;
                case "future_prospect":
                    if (request.FollowUpOn is null) return ApiResponse.Fail("Choose the follow-up date.");
                    followUp = request.FollowUpOn.Value.Date;
                    if (followUp < _clock.Today.Date) return ApiResponse.Fail("The follow-up date cannot be in the past.");
                    status = StatusInProgress;               // stays open until the follow-up
                    break;
                case "converted":
                    status = StatusResolved;
                    break;
                default:
                    return ApiResponse.Fail("Choose Not interested, Future prospect or Conversion into quotation.");
            }

            if (!await _db.InsertContactFeedbackAsync(id, outcome, reason, followUp, request.QuotationId, actorId))
                return ApiResponse.Fail("Feedback can't be saved yet: the feedback table has not been set up on this database.");
            await _db.UpdateContactStatusAsync(id, status, actorId);
            return ApiResponse.Ok(new { outcome, followUpOn = followUp?.ToString("yyyy-MM-dd") }, "Feedback saved.");
        }

        public async Task<IEnumerable<object>> GetRecentContactsAsync(int top) =>
            (await _db.GetRecentContactsAsync(top)).Cast<object>();

        public async Task<ApiResponse> CreateContactAsync(ContactRequest request, string contactType, string? userEmail)
        {
            int? userId = null;
            if (!string.IsNullOrWhiteSpace(userEmail))
            {
                var (id, _) = await _db.GetUserIdByEmailAsync(userEmail);
                userId = id;
            }

            var (newId, publicId) = await _db.InsertContactAsync(
                contactType, userId, request.FullName, request.Email, request.Phone, request.Message);

            _ = Task.Run(async () =>
            {
                try { await _email.SendTourNotificationAsync(request.FullName, request.Email, request.Phone ?? "", request.Message ?? ""); }
                catch { }
            });

            return ApiResponse.Ok(new { id = newId, publicId }, "Contact recorded.");
        }

        public async Task<ApiResponse> UpdateContactStatusAsync(int id, byte statusId, int? actorId)
        {
            await _db.UpdateContactStatusAsync(id, statusId, actorId);
            return ApiResponse.Ok("Contact status updated.");
        }

        public async Task<ApiResponse> DeleteContactAsync(int id)
        {
            await _db.DeleteContactAsync(id);
            return ApiResponse.Ok("Contact deleted.");
        }
    }
}
