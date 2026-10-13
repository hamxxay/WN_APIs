namespace WorkNest.Application.DTOs.AuditLog
{
    public class AuditLogDetailDto : AuditLogDto
    {
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StackTrace { get; set; }
    }
}
