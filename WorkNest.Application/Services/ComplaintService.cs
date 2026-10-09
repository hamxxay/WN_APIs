using System.Security.Cryptography;
using WorkNest.Application.DTOs.Complaint;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Complaints from the WhatsApp chatbot and from staff (Complaints page), stored in dbo.WN_Complaints.
    /// Same process as the chatbot had: open -> in progress -> resolved (customer asked on WhatsApp:
    /// "1. the issue persists" creates a linked follow-up complaint, "2. resolved" closes it) -> closed.
    /// </summary>
    public class ComplaintService : IComplaintService
    {
        private readonly IComplaintRepository _repo;
        private readonly IChatbotNotifier _bot;
        private readonly IBusinessClock _clock;

        /// <summary>A customer's 1 / 2 reply counts only within this many days of the "resolved" message.</summary>
        private const int ReplyWindowDays = 7;

        public ComplaintService(IComplaintRepository repo, IChatbotNotifier bot, IBusinessClock clock)
        {
            _repo = repo;
            _bot = bot;
            _clock = clock;
        }

        public Task<(List<ComplaintDto> Items, int Total)> GetListAsync(int page, int limit, string? search, string? status) =>
            _repo.GetListAsync(Math.Max(1, page), Math.Clamp(limit, 1, 200), search, NormalizeStatus(status));

        public async Task<ApiResponse> CreateManualAsync(ComplaintCreateRequest request, int? actorId)
        {
            var error = Validate(request.Category, request.Description);
            if (error != null) return ApiResponse.Fail(error);
            if (request.CustomerName is { Length: > 200 }) return ApiResponse.Fail("The customer name can be at most 200 characters.");
            if (request.Email is { Length: > 256 }) return ApiResponse.Fail("The email can be at most 256 characters.");

            var complaint = new ComplaintDto
            {
                Source = "manual",
                CustomerName = Clean(request.CustomerName),
                PhoneNumber = NormalizePhone(request.PhoneNumber),
                Email = Clean(request.Email),
                Branch = Clean(request.Branch),
                Category = request.Category.Trim(),
                Description = request.Description.Trim()
            };
            var saved = await InsertWithNumberAsync(complaint, actorId);
            return saved == null
                ? ApiResponse.Fail("The complaint could not be saved. Please try again.")
                : ApiResponse.Ok(new { id = saved.Value.Id, complaintNo = saved.Value.No }, $"Complaint {saved.Value.No} recorded.");
        }

        public async Task<ApiResponse> UpdateStatusAsync(int id, ComplaintStatusUpdateRequest request, int? actorId)
        {
            var status = NormalizeStatus(request.Status);
            if (status == null) return ApiResponse.Fail("Choose Open, In Progress, Resolved or Closed.");
            if (request.StaffNotes is { Length: > 1000 }) return ApiResponse.Fail("Notes can be at most 1000 characters.");

            var complaint = await _repo.GetByIdAsync(id);
            if (complaint == null) return ApiResponse.Fail("Complaint not found.");

            await _repo.UpdateStatusAsync(id, status, Clean(request.StaffNotes), actorId);

            // Resolved for the first time: ask the customer on WhatsApp whether the issue is really fixed.
            if (status == ComplaintStatus.Resolved && complaint.Status != ComplaintStatus.Resolved && !complaint.ResolvedNotificationSent)
            {
                if (string.IsNullOrWhiteSpace(complaint.PhoneNumber))
                    return ApiResponse.Ok(new { notified = false }, "Complaint resolved. No phone number, so the customer was not messaged.");
                var (sent, err) = await _bot.SendComplaintResolvedAsync(complaint.PhoneNumber, complaint.ComplaintNo);
                if (sent)
                {
                    await _repo.MarkResolvedNotificationSentAsync(id);
                    return ApiResponse.Ok(new { notified = true }, "Complaint resolved. The customer was asked on WhatsApp to confirm.");
                }
                return ApiResponse.Ok(new { notified = false, error = err },
                    "Complaint resolved, but the WhatsApp message could not be sent" + (string.IsNullOrWhiteSpace(err) ? "." : $": {err}"));
            }
            return ApiResponse.Ok(new { notified = false }, "Complaint updated.");
        }

        public async Task<ApiResponse> CreateFromChatbotAsync(ChatbotComplaintRequest request)
        {
            var error = Validate(request.Category, request.Description);
            if (error != null) return ApiResponse.Fail(error);
            var phone = NormalizePhone(request.Phone);
            if (phone == null) return ApiResponse.Fail("The phone number is required.");

            var saved = await InsertWithNumberAsync(new ComplaintDto
            {
                Source = "whatsapp",
                CustomerName = Clean(request.Name),
                PhoneNumber = phone,
                Category = request.Category.Trim(),
                Description = request.Description.Trim()
            }, null);
            return saved == null
                ? ApiResponse.Fail("The complaint could not be saved.")
                : ApiResponse.Ok(new { id = saved.Value.Id, complaintNo = saved.Value.No }, "Complaint recorded.");
        }

        public async Task<ApiResponse> HandleResolutionReplyAsync(ChatbotResolutionReplyRequest request)
        {
            var response = (request.Response ?? "").Trim().ToLowerInvariant();
            if (response is not ("persists" or "resolved")) return ApiResponse.Fail("Response must be 'persists' or 'resolved'.");
            var phone = NormalizePhone(request.Phone);
            if (phone == null) return ApiResponse.Fail("The phone number is required.");

            var complaint = await _repo.GetAwaitingResolutionReplyAsync(PhoneVariants(phone), _clock.Now.AddDays(-ReplyWindowDays));
            if (complaint == null) return ApiResponse.Ok(new { handled = false }, "No complaint is waiting for this customer's reply.");

            if (response == "resolved")
            {
                await _repo.SetResolutionResponseAsync(complaint.Id, "resolved", ComplaintStatus.Closed);
                return ApiResponse.Ok(new { handled = true, response, complaintNo = complaint.ComplaintNo }, "Complaint closed.");
            }

            // The issue persists: keep the old one resolved (with the reply) and open a linked follow-up complaint.
            await _repo.SetResolutionResponseAsync(complaint.Id, "persists", null);
            var followUp = await InsertWithNumberAsync(new ComplaintDto
            {
                Source = complaint.Source,
                CustomerName = complaint.CustomerName,
                PhoneNumber = complaint.PhoneNumber,
                Email = complaint.Email,
                Branch = complaint.Branch,
                Category = complaint.Category,
                Description = complaint.Description,
                IsFollowUp = true,
                PreviousComplaintId = complaint.Id
            }, null);
            return ApiResponse.Ok(new
            {
                handled = true,
                response,
                complaintNo = complaint.ComplaintNo,
                newComplaintNo = followUp?.No
            }, "Follow-up complaint opened.");
        }

        // ---- helpers ----

        private async Task<(int Id, string No)?> InsertWithNumberAsync(ComplaintDto complaint, int? actorId)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                complaint.ComplaintNo = NewComplaintNo();
                var id = await _repo.InsertAsync(complaint, actorId);
                if (id != null) return (id.Value, complaint.ComplaintNo);
            }
            return null;
        }

        private static string NewComplaintNo()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            Span<char> s = stackalloc char[6];
            for (var i = 0; i < s.Length; i++) s[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
            return "CMP-" + new string(s);
        }

        private static string? Validate(string? category, string? description)
        {
            if (string.IsNullOrWhiteSpace(category)) return "Choose the complaint category.";
            if (category.Trim().Length > 50) return "The category can be at most 50 characters.";
            if (string.IsNullOrWhiteSpace(description)) return "Describe the issue.";
            if (description.Trim().Length > 2000) return "The description can be at most 2000 characters.";
            return null;
        }

        private static string? NormalizeStatus(string? status)
        {
            var s = (status ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace("inprogress", "in_progress");
            return ComplaintStatus.All.Contains(s) ? s : null;
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>Digits only; a local number (03001234567) becomes 923001234567 like WhatsApp numbers.</summary>
        public static string? NormalizePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return null;
            if (digits.StartsWith("0092")) digits = digits[2..];
            else if (digits.Length == 11 && digits.StartsWith('0')) digits = "92" + digits[1..];
            return digits.Length > 30 ? digits[..30] : digits;
        }

        private static IEnumerable<string> PhoneVariants(string normalized)
        {
            yield return normalized;
            if (normalized.StartsWith("92") && normalized.Length == 12) yield return "0" + normalized[2..];
        }
    }
}
