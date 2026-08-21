using WorkNest.Application.DTOs.Booking;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IBookingService
    {
        Task<(IEnumerable<object> Items, int Total)> GetBookingsAsync(int page, int limit, string? search);
        Task<ApiResponse> GetBookingByIdAsync(Guid publicId, string? userEmail);
        Task<IEnumerable<object>> GetMyBookingsAsync(string userEmail);
        Task<IEnumerable<object>> GetRecentBookingsAsync(int top = 10);
        Task<ApiResponse> GetBookingCalendarAsync(int spaceId, int year, int month);
        Task<ApiResponse> CreateBookingAsync(BookingRequest request, string userEmail);
        Task<ApiResponse> CreateAdminBookingAsync(AdminBookingRequest request, string? actorEmail);
        Task<ApiResponse> CreateSmartBookingAsync(SmartBookingRequest request, string userEmail);
        Task<ApiResponse> UpdateBookingAsync(int id, BookingUpdateRequest request, int? actorId);
        Task<ApiResponse> UpdateBookingStatusAsync(int id, byte statusId, int? actorId);
        Task<ApiResponse> CancelBookingAsync(int id, string userEmail, string? cancelReason);
        Task<ApiResponse> ReassignBookingAsync(int id, ReassignBookingRequest request, string userEmail);
        Task<ApiResponse> GetAvailableSpacesForBookingAsync(int spaceTypeId, DateTime startOn, DateTime endOn, int? capacity);
        Task<ApiResponse> GetAvailableSpacesForReassignmentAsync(int spaceTypeId, DateTime startOn, DateTime endOn, int excludeBookingId);
        Task<ApiResponse> GetSmartAvailableSpacesAsync(string categoryCode, DateTime startOn, DateTime endOn, int? capacity);
        Task<ApiResponse> GetBookingDetailsAsync(string bookingIdentifier, string? userEmail);
        Task<ApiResponse> GetChallanAsync(int bookingId);
        Task<ApiResponse> SendChallanEmailAsync(int bookingId, byte[]? pdfBytes = null);
        Task<ApiResponse> SendBookingConfirmationEmailAsync(int bookingId);
        Task<byte[]> GenerateAdvanceInvoicePdfAsync(int bookingId, int advanceMonths, int secDepositMonths, decimal monthlyRate, decimal discountAmount);
    }
}
