namespace WorkNest.Application.DTOs.Contact
{
    public class ContactRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Message { get; set; }
        public string? Phone { get; set; }
    }

    public class ContactStatusUpdateRequest
    {
        public byte StatusId { get; set; }
    }

    public class ContactDto
    {
        public int? Id { get; set; }
        public string? PublicId { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Message { get; set; }
        public string? ContactType { get; set; }
        public string? CreatedOn { get; set; }
    }
}
