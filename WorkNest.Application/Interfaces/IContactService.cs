using WorkNest.Application.DTOs.Contact;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IContactService
    {
        Task<(IEnumerable<object> Items, int Total)> GetContactsAsync(int page, int limit, string? search);
        Task<IEnumerable<object>> GetRecentContactsAsync(int top);
        Task<ApiResponse> CreateContactAsync(ContactRequest request, string contactType, string? userEmail);
        Task<ApiResponse> UpdateContactStatusAsync(int id, byte statusId, int? actorId);
        Task<ApiResponse> DeleteContactAsync(int id);
    }
}
