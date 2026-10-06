using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Attendant;

namespace WorkNest.Application.Interfaces
{
    public interface IAttendantService
    {
        Task<(int PersonId, Guid PersonGuid)> AddAttendantAsync(CreateAttendantRequest request);
        Task UpdateAttendantAsync(int personId, UpdateAttendantRequest request);
        Task<IEnumerable<IDictionary<string, object?>>> GetCustomerAttendantsAsync(int customerId);
        Task<IEnumerable<BookingAttendantDetailDto>> GetBookingAttendantsAsync(int bookingDetailId);
        Task<AttendantCapacityCheckDto> CheckCapacityBeforeAssignAsync(int bookingDetailId);
        Task<IDictionary<string, object?>> AssignAttendantToBookingAsync(AssignBookingAttendantRequest request);
        Task SoftRemoveAttendantFromBookingAsync(int bookingDetailId, int personId);
        Task<int> ToggleAccessStatusAsync(ToggleAccessStatusRequest request);
        Task<IEnumerable<AccessStatusExportDto>> GetAccessStatusExportAsync();
        Task<IEnumerable<CustomerActiveSpaceDto>> GetCustomerActiveSpacesAsync(int customerId);
        Task<MobileAccessVerifyResult> VerifyMobileAccessAsync(MobileAccessVerifyRequest request);
    }
}
