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
            _db = db;
            _email = email;
        }

        public async Task<(IEnumerable<object> Items, int Total)> GetContactsAsync(int page, int limit, string? search)
        {
            var (rows, total) = await _db.GetContactsAsync(page, limit, search);
            return (rows.Cast<object>(), total);
        }

        public async Task<IEnumerable<object>> GetRecentContactsAsync(int top) =>
            (await _db.GetRecentContactsAsync(top)).Cast<object>();

        public async Task<ApiResponse> CreateContactAsync(ContactRequest request, string contactType, string? userEmail)
        {
            int? userId = null;
            if (!string.IsNullOrWhiteSpace(userEmail))
            {
                var (id, _) = await _db.GetUserIdByEmailAsync(userEmail);
                userId = id;
            }

            var (newId, publicId) = await _db.InsertContactAsync(
                contactType, userId, request.FullName, request.Email, request.Phone, request.Message);

            _ = Task.Run(async () =>
            {
                try { await _email.SendTourNotificationAsync(request.FullName, request.Email, request.Phone ?? "", request.Message ?? ""); }
                catch { }
            });

            return ApiResponse.Ok(new { id = newId, publicId }, "Contact recorded.");
        }

        public async Task<ApiResponse> UpdateContactStatusAsync(int id, byte statusId, int? actorId)
        {
            await _db.UpdateContactStatusAsync(id, statusId, actorId);
            return ApiResponse.Ok("Contact status updated.");
        }

        public async Task<ApiResponse> DeleteContactAsync(int id)
        {
            await _db.DeleteContactAsync(id);
            return ApiResponse.Ok("Contact deleted.");
        }
    }
}
