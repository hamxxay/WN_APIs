using WorkNest.Application.DTOs.Contact;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IContactService
    {
        Task<IEnumerable<object>> GetAllContactsAsync();
        Task<ApiResponse> CreateContactAsync(ContactRequest request, string? userEmail);
        Task<ApiResponse> UpdateContactStatusAsync(string id, string status);
        Task<ApiResponse> DeleteContactAsync(string id);
    }
}
