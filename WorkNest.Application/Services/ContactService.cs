using WorkNest.Application.DTOs.Contact;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class ContactService : IContactService
    {
        private readonly IDbRepository _db;
        private readonly IEmailService _email;

        public ContactService(IDbRepository db, IEmailService email)
        {
            _db    = db;
            _email = email;
        }

        public async Task<IEnumerable<object>> GetAllContactsAsync() =>
            (await _db.GetAllContactsAsync()).Cast<object>();

        public async Task<ApiResponse> CreateContactAsync(ContactRequest request, string? userEmail)
        {
            var emailToResolve = userEmail ?? request.Email;
            var (userId, _)    = await _db.GetUserIdByEmailAsync(emailToResolve);
            var newId          = await _db.BookTourAsync(request.FullName, request.Email,
                request.Message, request.Phone, userId);

            // Fire-and-forget email notification — mirrors Python try/except pattern
            _ = Task.Run(async () =>
            {
                try { await _email.SendTourNotificationAsync(request.FullName, request.Email, request.Phone, request.Message); }
                catch { /* swallow — notification is non-critical */ }
            });

            return ApiResponse.Ok(new { id = newId, fullName = request.FullName, email = request.Email },
                "Contact recorded.");
        }

        public async Task<ApiResponse> UpdateContactStatusAsync(string id, string status)
        {
            await _db.UpdateContactStatusAsync(id, status);
            return ApiResponse.Ok("Contact status updated.");
        }

        public async Task<ApiResponse> DeleteContactAsync(string id)
        {
            await _db.SoftDeleteContactAsync(id);
            return ApiResponse.Ok("Contact deleted.");
        }
    }
}
