using WorkNest.Application.DTOs.AccessCard;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IAccessCardService
    {
        /// <summary>locationIds: null = every location; otherwise only cards of these locations (location-bound staff).</summary>
        Task<ApiResponse> GetAllAccessCardsAsync(int page = 1, int limit = 20, string? search = null, int? bookingId = null, int? customerId = null, int? spaceId = null, int? status = null, IReadOnlyCollection<int>? locationIds = null);
        Task<ApiResponse> GetAccessCardByIdAsync(string id);
        Task<ApiResponse> CreateAccessCardAsync(AccessCardRequest request, int? createdById = null);
        Task<ApiResponse> UpdateAccessCardAsync(string id, AccessCardRequest request, int? updatedById = null);
        Task<ApiResponse> DeleteAccessCardAsync(string id);
        Task<ApiResponse> GenerateAccessCardsForBookingAsync(int bookingId, int? createdById = null);
        /// <summary>
        /// The booking a create (existingId null) or update would leave the card on, and whether its customer and space
        /// belong to that booking. Error: the reason to refuse (null = fine); BookingId: the effective booking (0 = none).
        /// </summary>
        Task<(string? Error, int BookingId)> CheckCardPartiesAsync(AccessCardRequest request, string? existingId = null);
    }
}
