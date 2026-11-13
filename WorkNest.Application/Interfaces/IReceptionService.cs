using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.DTOs.Reception;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// The Receptionist's own endpoints (api/reception/*). Every method takes the locations the caller may see
    /// (null = every location: super admin) and the caller's WN_Users.Id; location checks that need the record
    /// (a booking's room) are made here, the request-level checks (the requested location is the caller's) in the controller.
    /// </summary>
    public interface IReceptionService
    {
        Task<MeetingRoomOutcome> GetTodayAsync(IReadOnlyList<int>? locations);
        Task<MeetingRoomOutcome> CreateTourInquiryAsync(TourInquiryRequest? request, string? staffEmail, WorkNest.Application.Helpers.BranchScope scope);
        Task<MeetingRoomOutcome> GetMeetingCalendarAsync(IReadOnlyList<int>? locations, string? from, string? to);
        Task<MeetingRoomOutcome> BookMeetingRoomAsync(ReceptionMeetingBookRequest? request, IReadOnlyList<int>? locations, int staffUserId);
        Task<MeetingRoomOutcome> CancelMeetingRoomAsync(int bookingId, IReadOnlyList<int>? locations, int staffUserId);
        Task<MeetingRoomOutcome> CheckInAsync(int bookingId, IReadOnlyList<int>? locations, int staffUserId);
        Task<MeetingRoomOutcome> CheckOutAsync(int bookingId, IReadOnlyList<int>? locations, int staffUserId);
        Task<MeetingRoomOutcome> GetNetworkAsync(IReadOnlyList<int>? locations, CancellationToken ct = default);
        Task<MeetingRoomOutcome> StartSpeedtestAsync(int locationId, IReadOnlyList<int>? locations, CancellationToken ct = default);
    }

    /// <summary>ROLE_01_receptionist has not been run (a column the call needs is missing): answered 503.</summary>
    public sealed class ReceptionNotSetUpException : Exception
    {
        public const string UserMessage = "Reception is not set up on this server yet.";
        public ReceptionNotSetUpException() : base(UserMessage) { }
    }
}
