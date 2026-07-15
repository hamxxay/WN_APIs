using WorkNest.Application.DTOs.Booking;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Booking lifecycle and smart-assignment operations.</summary>
    public interface IBookingService
    {
        Task<IEnumerable<object>> GetAllBookingsAsync();
        Task<ApiResponse> GetBookingByIdAsync(string id, string userEmail);
        Task<IEnumerable<object>> GetMyBookingsAsync(string userEmail);
        Task<ApiResponse> GetBookingCalendarAsync(int spaceId, int year, int month);
        Task<ApiResponse> CreateBookingAsync(BookingRequest request, string userEmail);
        Task<ApiResponse> CreateAdminBookingAsync(AdminBookingRequest request);
        Task<ApiResponse> CreateSmartBookingAsync(SmartBookingRequest request, string userEmail);
        Task<ApiResponse> CancelBookingAsync(string id, string userEmail);
        Task<ApiResponse> UpdateBookingStatusAsync(string id, string status);
        Task<ApiResponse> UpdateBookingAsync(string id, BookingRequest request);
        Task<ApiResponse> ReassignBookingAsync(string id, ReassignBookingRequest request, string adminEmail);
        Task<ApiResponse> GetAvailableSpacesAsync(string spaceType, string start, string end);
        Task<ApiResponse> GetAvailableSpacesForReassignmentAsync(string spaceType, string start, string end, int? excludeBookingId);
        Task<ApiResponse> GetSmartAvailableSpacesAsync(string spaceCategory, string start, string end, int? capacity);
    }
}
