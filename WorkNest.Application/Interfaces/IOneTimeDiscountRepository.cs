using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.OneTimeDiscount;

namespace WorkNest.Application.Interfaces
{
    public interface IOneTimeDiscountRepository
    {
        Task<List<OneTimeDiscountDto>> GetByBookingDbAsync(int bookingId, string? allowedLocationIds = null);
        Task<OneTimeDiscountPreviewDto> PreviewDbAsync(OneTimeDiscountPreviewRequest request, string? allowedLocationIds = null, int? actorUserId = null, string? actorEmail = null);
        Task<OneTimeDiscountDto> CreateDbAsync(OneTimeDiscountCreateRequest request, int createdBy, string? allowedLocationIds = null, string? actorEmail = null);
        Task<OneTimeDiscountDto> CancelDbAsync(int id, int cancelledBy, string? allowedLocationIds = null, string? actorEmail = null);
        Task<BookingOneTimeDiscountsSummaryDto> GetBookingSummaryDbAsync(int bookingId, string? allowedLocationIds = null);
    }
}
