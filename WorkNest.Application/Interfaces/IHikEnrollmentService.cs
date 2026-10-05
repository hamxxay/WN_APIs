using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    public interface IHikEnrollmentService
    {
        Task<HikEnrollResultDto> EnrollFingerprintAsync(int bookingDetailId, int personId, HikEnrollRequest request);
        Task<HikEnrollResultDto> EnrollCardAsync(int bookingDetailId, int personId, HikEnrollRequest request);
        Task<HikEnrollResultDto> EnrollFaceAsync(int bookingDetailId, int personId, HikEnrollRequest request);
        Task<HikAccessSyncResultDto> SetAccessEnabledAsync(int bookingDetailId, int? personId, bool isEnabled);
    }
}
