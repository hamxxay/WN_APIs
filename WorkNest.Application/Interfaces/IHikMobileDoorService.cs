using WorkNest.Application.DTOs.Attendant;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// "Unlock door" in the app for a verified access user: the booked room's door only (entrances are opened with
    /// card, fingerprint or face) and a remote open on it. Callers verify the person first.
    /// </summary>
    public interface IHikMobileDoorService
    {
        Task<MobileDoorsResult> GetDoorsAsync(int bookingDetailId, int personId);
        Task<MobileOpenDoorResult> OpenDoorAsync(int bookingDetailId, int personId, int deviceId, string? byEmail);
    }
}
