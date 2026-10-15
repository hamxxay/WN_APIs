using WorkNest.Application.Helpers;
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

        public async Task<(IEnumerable<object> Items, int Total)> GetContactsAsync(int page, int limit, string? search, string? inquiryType = null,
            BranchFilter? branch = null)
        {
            var (rows, total) = await _db.GetContactsAsync(page, limit, search, InquiryTypes.Normalize(inquiryType), branch);
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

            // The branch the customer picked: its WN_Locations.Id, so the inquiry goes to that branch's staff. A bot
            // conversation started before the id was kept only has the name: matched to an active location.
            var locationId = await BotBranchAsync(request.LocationId, request.Branch);

            // WhatsApp gives no email: stored empty (the Tour Inquiries page shows "-").
            var (newId, publicId) = await _db.InsertContactAsync("book_tour", null, name, "", phone, message, InquiryTypes.WhatsApp, null, locationId);
            _ = Task.Run(async () =>
            {
                try { await _email.SendTourNotificationAsync(name, "", phone, message); }
                catch { }
            });
            return ApiResponse.Ok(new { id = newId, publicId, locationId }, "Tour inquiry recorded.");
        }

        /// <summary>The bot's branch as an active location id (by id, else by name); null when it matches none.</summary>
        private async Task<int?> BotBranchAsync(int? locationId, string? branchName)
        {
            if (locationId is not > 0 && string.IsNullOrWhiteSpace(branchName)) return null;
            List<BranchOption> active;
            try { active = BranchScope.Options(await _db.GetAllLocationsAsync()); }
            catch { return locationId is > 0 ? locationId : null; }   // keep the bot's id rather than lose the branch
            if (locationId is > 0 && active.Any(l => l.Id == locationId)) return locationId;
            return BranchScope.ByName(branchName, active)?.Id;
        }

        public async Task<BranchChoice> ChooseBranchAsync(int? requested, BranchScope scope, bool change = false)
        {
            // Active locations are read only when a branch was chosen or one has to be filled in.
            var needsLocations = requested is > 0 || (!change && scope.Locations is { Count: 1 });
            var active = needsLocations ? BranchScope.Options(await _db.GetAllLocationsAsync()) : new List<BranchOption>();
            return change ? scope.ForChange(requested, active) : scope.ForNew(requested, active);
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

        public async Task<IEnumerable<object>> GetRecentContactsAsync(int top, BranchFilter? branch = null) =>
            (await _db.GetRecentContactsAsync(top, branch)).Cast<object>();

        public async Task<ApiResponse> CreateContactAsync(ContactRequest request, string contactType, string? userEmail, bool fromStaff = false,
            int? locationId = null)
        {
            var phoneError = Phone.Validate(request.Phone);
            if (phoneError != null) return ApiResponse.Fail(phoneError);

            // Type: staff choose Walk-In (default) or Reference; website and app forms are always Website.
            string inquiryType = InquiryTypes.Website;
            string? referenceName = null;
            if (fromStaff)
            {
                inquiryType = InquiryTypes.Normalize(request.InquiryType) == InquiryTypes.Reference ? InquiryTypes.Reference : InquiryTypes.WalkIn;
                if (inquiryType == InquiryTypes.Reference)
                {
                    referenceName = Clean(request.ReferenceName) ?? await StaffNameAsync(userEmail);
                    if (referenceName is { Length: > 200 }) return ApiResponse.Fail("The reference name can be at most 200 characters.");
                }
            }

            int? userId = null;
            if (!string.IsNullOrWhiteSpace(userEmail))
            {
                var (id, _) = await _db.GetUserIdByEmailAsync(userEmail);
                userId = id;
            }

            var (newId, publicId) = await _db.InsertContactAsync(
                contactType, userId, request.FullName, request.Email, request.Phone, request.Message, inquiryType, referenceName, locationId);

            _ = Task.Run(async () =>
            {
                try { await _email.SendTourNotificationAsync(request.FullName, request.Email, request.Phone ?? "", request.Message ?? ""); }
                catch { }
            });

            return ApiResponse.Ok(new { id = newId, publicId, locationId }, "Contact recorded.");
        }

        public async Task<ApiResponse> UpdateLocationAsync(Guid publicId, BranchOption? location, BranchScope scope, string? actorEmail)
        {
            int? actorId = string.IsNullOrWhiteSpace(actorEmail) ? null : (await _db.GetUserIdByEmailAsync(actorEmail)).Item1;
            var scopeCsv = scope.Locations is null ? null : (scope.Locations.Count == 0 ? "-1" : string.Join(",", scope.Locations));
            var updated = await _db.UpdateContactLocationAsync(publicId, location?.Id, actorId, scopeCsv);
            if (updated is null) return ApiResponse.Fail(BranchMessages.ContactsUpdateMissing);
            if (updated == 0) return ApiResponse.Fail("Inquiry not found. Older website bookings can't be changed.");
            return ApiResponse.Ok(new { locationId = location?.Id, locationName = location?.Name }, "Branch saved.");
        }

        public async Task<ApiResponse> UpdateInquiryTypeAsync(Guid publicId, ContactInquiryTypeRequest request, string? actorEmail)
        {
            var type = InquiryTypes.Normalize(request.InquiryType);
            if (type is null) return ApiResponse.Fail("Choose Website, WhatsApp, Walk-In or Reference.");

            string? referenceName = null;
            if (type == InquiryTypes.Reference)
            {
                referenceName = Clean(request.ReferenceName) ?? await StaffNameAsync(actorEmail);
                if (referenceName is { Length: > 200 }) return ApiResponse.Fail("The reference name can be at most 200 characters.");
            }

            int? actorId = string.IsNullOrWhiteSpace(actorEmail) ? null : (await _db.GetUserIdByEmailAsync(actorEmail)).Item1;
            var updated = await _db.UpdateContactInquiryTypeAsync(publicId, type, referenceName, actorId);
            if (updated is null)
                return ApiResponse.Fail("Inquiry types are not set up on this database yet (Database/Contact/CT_01_inquiry_type.txt).");
            if (updated == 0)
                return ApiResponse.Fail("Inquiry not found. Older website bookings can't be changed.");
            return ApiResponse.Ok(new { inquiryType = type, referenceName }, "Inquiry type saved.");
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>The staff member's name for a Reference inquiry they add or edit; their email when the account has no name.</summary>
        private async Task<string?> StaffNameAsync(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            var user = await _db.GetUserByEmailAsync(email);
            var name = user is not null && user.TryGetValue("Name", out var n) ? n?.ToString() : null;
            return Clean(name) ?? email.Trim();
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
