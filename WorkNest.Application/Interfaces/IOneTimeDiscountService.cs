using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.OneTimeDiscount;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IOneTimeDiscountService
    {
        Task<ApiResponse> GetByBookingAsync(int bookingId, IReadOnlyCollection<int>? allowedLocations = null);
        Task<ApiResponse> PreviewAsync(OneTimeDiscountPreviewRequest request, string? actorEmail = null, IReadOnlyCollection<int>? allowedLocations = null);
        Task<ApiResponse> CreateAsync(OneTimeDiscountCreateRequest request, int createdBy, string? actorEmail = null, IReadOnlyCollection<int>? allowedLocations = null);
        Task<ApiResponse> CancelAsync(int id, int cancelledBy, string? actorEmail = null, IReadOnlyCollection<int>? allowedLocations = null);
    }
}
