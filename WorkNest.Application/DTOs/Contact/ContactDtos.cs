namespace WorkNest.Application.DTOs.Contact
{
    public class ContactRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }

    public class ContactDto
    {
        public int? Id { get; set; }
        public string? IdGuid { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Message { get; set; }
        public object? Status { get; set; }
        public string? CreatedAt { get; set; }
    }
}
