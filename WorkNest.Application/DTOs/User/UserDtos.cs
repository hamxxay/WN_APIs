namespace WorkNest.Application.DTOs.User
{
    public class UserCreateRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Password { get; set; }
    }

    public class UserUpdateRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
    }

    public class UserRoleUpdateRequest
    {
        public string Role { get; set; } = string.Empty;
    }

    public class UserDto
    {
        public string? Id { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? Phone { get; set; }
        public bool IsActive { get; set; }
        public string? CreatedAt { get; set; }
        public string? Role { get; set; }
    }

    public class UserHistoryResponse
    {
        public UserHistoryStats Stats { get; set; } = new();
        public IEnumerable<object> RecentBookings { get; set; } = [];
        public IEnumerable<object> RecentPayments { get; set; } = [];
    }

    public class UserHistoryStats
    {
        public int TotalBookings { get; set; }
        public int TotalPayments { get; set; }
        public double TotalPaidAmount { get; set; }
        public int FailedPayments { get; set; }
        public int CancelledBookings { get; set; }
    }
}
