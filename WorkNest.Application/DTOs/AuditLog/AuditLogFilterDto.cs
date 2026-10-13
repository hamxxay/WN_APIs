namespace WorkNest.Application.DTOs.AuditLog
{
    public class AuditLogFilterDto
    {
        private int _pageSize = 50;
        private int _page = 1;

        public int Page
        {
            get => _page;
            set => _page = value < 1 ? 1 : value;
        }

        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (value < 1) _pageSize = 50;
                else if (value > 200) _pageSize = 200;
                else _pageSize = value;
            }
        }

        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? Operation { get; set; }
        public string? EntityName { get; set; }
        public string? ChangedByUserEmail { get; set; }
        public string? RecordId { get; set; }
        public string? SourceApp { get; set; }
        public bool ErrorsOnly { get; set; }
    }
}
