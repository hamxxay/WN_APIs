using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Transfer;

namespace WorkNest.Application.Interfaces
{
    public interface ITransferRepository
    {
        // Phase 1: Same-Location Space Change
        Task<TransferPreviewResponseDto> GetTransferPreviewAsync(int bookingId, int newSpaceId, DateTime effectiveDate, decimal transferFee);
        Task<TransferExecuteResponseDto> ExecuteTransferAsync(int oldBookingId, int newSpaceId, DateTime effectiveDate, decimal transferFee, string notes, int? updatedById, string? userEmail);
        Task<ActiveBookingSummaryDto?> GetBookingSummaryAsync(int bookingId);
        Task<TargetSpaceSummaryDto?> GetTargetSpaceSummaryAsync(int spaceId);
        Task<List<TargetSpaceSummaryDto>> GetAvailableTargetSpacesAsync(int locationId, DateTime effectiveDate, DateTime endDate, int excludeBookingId);
        Task<List<ActiveBookingSummaryDto>> GetActiveBookingsAsync(int? locationId, string? search);

        // Phase 2: Branch Transfers and Pending Payments
        Task<int> CreateBranchTransferRequestAsync(BranchTransferCreateRequestDto dto, int actorId, string actorEmail);
        Task<bool> CancelBranchTransferRequestAsync(int requestId, string? reason, int actorId, string actorEmail);
        Task<bool> DeclineBranchTransferRequestAsync(int requestId, string? reason, int actorId, string actorEmail);
        Task<int> ExpireOldTransferRequestsAsync();
        Task<List<TransferRequestSendingSummaryDto>> ListSendingRequestsAsync(int? companyId, int? locationId, string? userRole);
        Task<List<TargetSpaceSummaryDto>> GetIncomingAvailableSpacesAsync(int requestId);
        Task<List<TransferRequestIncomingSummaryDto>> ListIncomingRequestsAsync(int? companyId, int? locationId, string? userRole);
        Task<MatchResolutionDetailsDto?> GetMatchResolutionAsync(int requestId);
        Task<(string Status, string Message, int? NewBookingId)> AcceptWithSpaceAsync(int requestId, int spaceId, int actorId, string actorEmail);
        Task<(string Status, string Message, int? NewBookingId)> ResolveMatchAsync(int requestId, string resolutionType, int? selectedCustomerId, string? note, int actorId, string actorEmail);
        Task<List<PendingPaymentSummaryDto>> ListPendingPaymentsAsync(int? companyId, int? locationId, string? userRole);
        Task<bool> ConfirmPendingPaymentAsync(int paymentId, string bankAccount, string paymentReference, DateTime? paymentDate, int actorId, string actorEmail);
    }
}

