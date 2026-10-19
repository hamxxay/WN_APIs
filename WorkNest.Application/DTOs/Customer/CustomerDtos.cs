using WorkNest.Common.Responses;

namespace WorkNest.Application.DTOs.Customer
{
    /// <summary>GET api/customer/{id}/overview: the HTTP status (200 / 404 / 503) and the ApiResponse body.</summary>
    public sealed record CustomerOverviewResult(int StatusCode, ApiResponse Response);

    public class CustomerRequest
    {
        public string FirstName { get; set; } = string.Empty;
        public string? LastName { get; set; }
        public string? CustomerType { get; set; } // "Individual" or "Company" / "Business"
        public string? Company { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? CnicOrPassport { get; set; }
        public string? Address { get; set; }
        public int? CityId { get; set; }
        public string? Notes { get; set; }
        public bool? IsActive { get; set; }
        public string? Ntn { get; set; }
        public string? SecpRegistrationNo { get; set; }
        public string? CustomerTypeId { get; set; }
        public int? CountryId { get; set; }
        public string? IdentificationDocumentTypeId { get; set; }
    }
}
