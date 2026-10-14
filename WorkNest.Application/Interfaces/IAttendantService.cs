using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Attendant;
using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    public interface IAttendantService
    {
        Task<(int PersonId, Guid PersonGuid)> AddAttendantAsync(CreateAttendantRequest request);
        Task UpdateAttendantAsync(int personId, UpdateAttendantRequest request);
        Task<IEnumerable<IDictionary<string, object?>>> GetCustomerAttendantsAsync(int customerId);
        Task<IEnumerable<BookingAttendantDetailDto>> GetBookingAttendantsAsync(int bookingDetailId);
        Task<AttendantCapacityCheckDto> CheckCapacityBeforeAssignAsync(int bookingDetailId);
        Task<IDictionary<string, object?>> AssignAttendantToBookingAsync(AssignBookingAttendantRequest request, bool allowOverCapacity = true);

        // Over-capacity requests: a sales executive asks to add a person to a full room; an admin approves or rejects.
        /// <summary>New request id; 0 if one is already pending for this person and room; -1 if the table isn't set up yet.</summary>
        Task<int> RequestOverCapacityAsync(int bookingDetailId, int personId, int customerId, string requestedByEmail);
        Task<List<IDictionary<string, object?>>> GetCapacityRequestsAsync(string? status, int? bookingDetailId, IReadOnlyCollection<int>? locationIds);
        Task<IDictionary<string, object?>?> GetCapacityRequestAsync(int id);
        /// <summary>Approves a pending request and assigns the person (over-capacity billing as usual).</summary>
        Task<IDictionary<string, object?>> ApproveCapacityRequestAsync(int id, string decidedByEmail);
        /// <summary>Rejects or cancels a pending request; false if it was no longer pending.</summary>
        Task<bool> CloseCapacityRequestAsync(int id, string status, string decidedByEmail, string? note);
        Task SoftRemoveAttendantFromBookingAsync(int bookingDetailId, int personId);
        /// <summary>
        /// Removes a person from a booked space and cancels their door access: blocks them on the room machine + every
        /// Entrance machine (queued in WN_HIK_PendingOps for offline machines; Entrance stays open when another active
        /// booking still allows them), then ends the assignment (AssignedTo) and the access status (WN_AccessStatus).
        /// </summary>
        Task<HikAccessSyncResultDto> RemoveAttendantAndRevokeAccessAsync(int bookingDetailId, int personId);
        Task<int> ToggleAccessStatusAsync(ToggleAccessStatusRequest request);
        Task<IEnumerable<AccessStatusExportDto>> GetAccessStatusExportAsync();
        Task<IEnumerable<CustomerActiveSpaceDto>> GetCustomerActiveSpacesAsync(int customerId);
        Task<MobileAccessVerifyResult> VerifyMobileAccessAsync(MobileAccessVerifyRequest request);
    }
}
