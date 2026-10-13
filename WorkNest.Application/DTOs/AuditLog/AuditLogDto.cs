namespace WorkNest.Application.DTOs.AuditLog
{
    public class AuditLogDto
    {
        public long AuditId { get; set; }
        public string? EntityName { get; set; }
        public string? RecordId { get; set; }
        public string Operation { get; set; } = string.Empty;

        public string OperationLabel => Operation?.ToUpperInvariant() switch
        {
            "I" => "Insert",
            "U" => "Update",
            "D" => "Delete",
            _ => Operation ?? string.Empty
        };

        public int? ChangedByUserId { get; set; }
        public string? ChangedByUserEmail { get; set; }
        
        /// <summary>ChangedAt in Pakistan Standard Time (Asia/Karachi, UTC+5)</summary>
        public DateTime ChangedAt { get; set; }

        /// <summary>Original ChangedAt in UTC</summary>
        public DateTime ChangedAtUtc { get; set; }

        public string? SourceApp { get; set; }
        public bool HasError { get; set; }
        public string? ErrorSeverity { get; set; }
    }
}
