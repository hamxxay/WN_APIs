namespace WorkNest.Application.DTOs.AuditLog
{
    public class AuditLogPagedResultDto
    {
        public List<AuditLogDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    }

    public class AuditLogFilterOptionsDto
    {
        public List<string> EntityNames { get; set; } = new();
        public List<string> SourceApps { get; set; } = new();
        public List<string> Operations { get; set; } = new() { "I", "U", "D" };
    }
}
