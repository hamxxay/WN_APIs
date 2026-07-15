namespace WorkNest.Application.DTOs.Customer
{
    public class CustomerRequest
    {
        public string FirstName { get; set; } = string.Empty;
        public string? LastName { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? CnicOrPassport { get; set; }
        public string? Address { get; set; }
        public int? CityId { get; set; }
        public string? Notes { get; set; }
        public bool? IsActive { get; set; }
    }
}
