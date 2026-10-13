namespace WorkNest.Application.DTOs.LoginHistory
{
    public class LoginHistoryFilterDto
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
        public string? Email { get; set; }
        public string? EventType { get; set; }
        public string? IpAddress { get; set; }
        public bool LatestOnly { get; set; }
    }
}
