using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Transfer;

namespace WorkNest.Application.Interfaces
{
    public interface ITransferRepository
    {
        Task<TransferPreviewResponseDto> GetTransferPreviewAsync(int bookingId, int newSpaceId, DateTime effectiveDate, decimal transferFee);
        Task<TransferExecuteResponseDto> ExecuteTransferAsync(int oldBookingId, int newSpaceId, DateTime effectiveDate, decimal transferFee, string notes, int? updatedById, string? userEmail);
        Task<ActiveBookingSummaryDto?> GetBookingSummaryAsync(int bookingId);
        Task<TargetSpaceSummaryDto?> GetTargetSpaceSummaryAsync(int spaceId);
        Task<List<TargetSpaceSummaryDto>> GetAvailableTargetSpacesAsync(int locationId, DateTime effectiveDate, DateTime endDate, int excludeBookingId);
    }
}

