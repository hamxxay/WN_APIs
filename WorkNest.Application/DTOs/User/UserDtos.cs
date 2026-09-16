namespace WorkNest.Application.DTOs.User
{
    public class UserCreateRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Password { get; set; }
        public string? Phone { get; set; }
        public string? CnicOrPassport { get; set; }
        public string? Address { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Notes { get; set; }
        public string? Role { get; set; }
        public int? CompanyId { get; set; }
        public int? CityId { get; set; }
        public int? LocationId { get; set; }
    }

    public class UserUpdateRequest
    {
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? Role { get; set; }
        public int? CompanyId { get; set; }
        public int? CityId { get; set; }
        public int? LocationId { get; set; }
        public string? Address { get; set; }
        public string? CnicOrPassport { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Notes { get; set; }
    }

    public class UserRoleUpdateRequest
    {
        public string Role { get; set; } = string.Empty;
        public int? LocationId { get; set; }
    }

    public class UserDto
    {
        public int? Id { get; set; }
        public string? PublicId { get; set; }
        public string? Email { get; set; }
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public bool IsActive { get; set; }
        public string? CreatedOn { get; set; }
        public string? Role { get; set; }
        public int? LocationId { get; set; }
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
