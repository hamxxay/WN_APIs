using WorkNest.Application.DTOs.Attendant;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// "Unlock door" in the app for a verified access user: the doors of their booking (the booked room's machine +
    /// the entrances of that location) and a remote open on one of them. Callers verify the person first.
    /// </summary>
    public interface IHikMobileDoorService
    {
        Task<MobileDoorsResult> GetDoorsAsync(int bookingDetailId, int personId);
        Task<MobileOpenDoorResult> OpenDoorAsync(int bookingDetailId, int personId, int deviceId, string? byEmail);
    }
}
