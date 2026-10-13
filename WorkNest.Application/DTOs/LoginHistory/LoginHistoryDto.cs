namespace WorkNest.Application.DTOs.LoginHistory
{
    public class LoginHistoryDto
    {
        public long LoginId { get; set; }
        public int? UserId { get; set; }
        public string? Email { get; set; }
        public string EventType { get; set; } = string.Empty; // LOGIN, FAILED, LOGOUT
        public string? FailureReason { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string DeviceSummary { get; set; } = "Unknown";

        /// <summary>EventAt in Pakistan Standard Time (Asia/Karachi, UTC+5)</summary>
        public DateTime EventAt { get; set; }

        /// <summary>Original EventAt in UTC</summary>
        public DateTime EventAtUtc { get; set; }
    }
}
