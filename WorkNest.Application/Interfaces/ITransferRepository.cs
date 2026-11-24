using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Transfer;

namespace WorkNest.Application.Interfaces
{
    public interface ITransferRepository
    {
        // Phase 1: Same-Location Space Change
        Task<TransferPreviewResponseDto> GetTransferPreviewAsync(int bookingId, int newSpaceId, DateTime effectiveDate, decimal transferFee, decimal? perSeatPrice = null);
        Task<TransferExecuteResponseDto> ExecuteTransferAsync(int oldBookingId, int newSpaceId, DateTime effectiveDate, decimal transferFee, string notes, int? updatedById, string? userEmail, TransferExecutionArgs? args = null);
        /// <summary>The procedures take @PerSeatPrice (TRF_02).</summary>
        Task<bool> IsPerSeatPricingReadyAsync();
        /// <summary>The redesign has run: @ScheduleJson on WN_Booking_TransferSpace and the table WN_TransferRequests.</summary>
        Task<bool> IsRedesignReadyAsync();
        /// <summary>True for a meeting / conference room (not a monthly transfer target).</summary>
        Task<bool> IsMeetingRoomAsync(int spaceId);
        Task<int> GetSecurityDepositMonthsAsync(int bookingId);
        /// <summary>Adds a Pending request; null when the booking already has one waiting.</summary>
        Task<int?> InsertTransferRequestAsync(int bookingId, int newSpaceId, DateTime effectiveDate, int? locationId, string planJson, TransferPlanDto plan, string? note, int? requestedById);
        Task<TransferRequestRecord?> GetTransferRequestAsync(int id);
        /// <summary>locationIds null = all; empty = nothing. requestedById set = only that user's.</summary>
        Task<List<TransferRequestDto>> ListTransferRequestsAsync(bool pendingOnly, IReadOnlyCollection<int>? locationIds, int? requestedById);
        /// <summary>Pending to SentBack in one UPDATE (false when no longer Pending).</summary>
        Task<bool> SendBackTransferRequestAsync(int id, int? decidedById, string note);
        /// <summary>After the procedure approved it: stores the deciding user, the note and the final values.</summary>
        Task FinalizeApprovalAsync(int id, int? decidedById, string? note, string planJson, TransferPlanDto plan);
        /// <summary>Completed transfers, newest first. locationIds null = all; empty = nothing.</summary>
        Task<TransferHistoryPageDto> ListTransferHistoryAsync(string? search, DateTime? from, DateTime? to, int page, int pageSize, IReadOnlyCollection<int>? locationIds);
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

