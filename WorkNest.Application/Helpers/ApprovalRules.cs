using System.Net.Mail;

namespace WorkNest.Application.Helpers
{
    /// <summary>Request statuses (dbo.WN_ApprovalRequests.Status): Pending -> Approved -> Done, or Rejected, or Expired.</summary>
    public static class ApprovalStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Done = "Done";
        public const string Expired = "Expired";
    }

    /// <summary>
    /// Approvals rules: the one-time e-mail link (32 random bytes, base64url; only its SHA-256 is stored), input limits
    /// matching the columns of APPROVAL_01, and who may close a request.
    /// </summary>
    public static class ApprovalRules
    {
        public const int MaxTitle = 200;
        public const int MaxDetails = 4000;
        public const int MaxReason = 1000;
        public const int MaxNote = 1000;
        public const int MaxCategoryName = 100;
        public const int MaxEmail = 256;

        /// <summary>A new link token (same shape as the sign-in handoff codes).</summary>
        public static string NewToken() => AuthHandoffRules.NewCode();

        /// <summary>SHA-256 of the token as sent; null for a blank or implausibly long token.</summary>
        public static byte[]? HashToken(string? token) => AuthHandoffRules.HashCode(token);

        /// <summary>Null when the new request is acceptable; else the message to show. Reason is optional.</summary>
        public static string? ValidateRequest(string? title, string? details, string? reason)
        {
            if (string.IsNullOrWhiteSpace(title)) return "Title is required.";
            if (title.Trim().Length > MaxTitle) return $"Title must be at most {MaxTitle} characters.";
            if (string.IsNullOrWhiteSpace(details)) return "Details are required.";
            if (details.Trim().Length > MaxDetails) return $"Details must be at most {MaxDetails} characters.";
            if (reason != null && reason.Trim().Length > MaxReason) return $"Reason must be at most {MaxReason} characters.";
            return null;
        }

        /// <summary>Null when the optional decision / done note is acceptable.</summary>
        public static string? ValidateNote(string? note) =>
            note != null && note.Trim().Length > MaxNote ? $"Note must be at most {MaxNote} characters." : null;

        public static bool ValidEmail(string? email)
        {
            var e = email?.Trim();
            if (string.IsNullOrEmpty(e) || e.Length > MaxEmail) return false;
            try { return new MailAddress(e).Address == e; }
            catch (FormatException) { return false; }
        }

        /// <summary>The link still works: Pending and strictly before its expiry second.</summary>
        public static bool IsOpen(string status, DateTime expiresAtUtc, DateTime nowUtc) =>
            status == ApprovalStatuses.Pending && nowUtc < expiresAtUtc;

        /// <summary>The super admin, or a person the request was sent to.</summary>
        public static bool MayMarkDone(bool isSuperAdmin, int userId, IEnumerable<int> assigneeIds) =>
            isSuperAdmin || assigneeIds.Contains(userId);
    }
}
