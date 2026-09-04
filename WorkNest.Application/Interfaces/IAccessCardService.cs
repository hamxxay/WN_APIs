using WorkNest.Application.DTOs.AccessCard;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IAccessCardService
    {
        Task<ApiResponse> GetAllAccessCardsAsync(int page = 1, int limit = 20, string? search = null, int? bookingId = null, int? customerId = null, int? spaceId = null, int? status = null);
        Task<ApiResponse> GetAccessCardByIdAsync(string id);
        Task<ApiResponse> CreateAccessCardAsync(AccessCardRequest request, int? createdById = null);
        Task<ApiResponse> UpdateAccessCardAsync(string id, AccessCardRequest request, int? updatedById = null);
        Task<ApiResponse> DeleteAccessCardAsync(string id);
        Task<ApiResponse> GenerateAccessCardsForBookingAsync(int bookingId, int? createdById = null);
    }
}
