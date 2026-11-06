using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Transfer;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ITransferService
    {
        // Phase 1
        Task<ApiResponse> GetActiveBookingsAsync(int? locationId, string? search, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> GetPreviewAsync(TransferPreviewRequest request, string? userEmail, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> ExecuteTransferAsync(TransferExecuteRequest request, string? userEmail, int? actorId, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> GetAvailableTargetSpacesAsync(int bookingId, DateTime effectiveDate, IReadOnlyCollection<int>? allowedLocationIds);
        Task<byte[]> GenerateAddendumPdfAsync(int oldBookingId, int newBookingId, int newSpaceId, DateTime effectiveDate, string note);

        // Phase 2
        Task<ApiResponse> CreateBranchTransferRequestAsync(BranchTransferCreateRequestDto request, string? userEmail, int? actorId, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> CancelBranchTransferRequestAsync(int requestId, BranchTransferCancelRequestDto request, string? userEmail, int? actorId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> DeclineBranchTransferRequestAsync(int requestId, BranchTransferDeclineRequestDto request, string? userEmail, int? actorId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> ListSendingRequestsAsync(int? companyId, int? locationId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> GetIncomingAvailableSpacesAsync(int requestId, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> ListIncomingRequestsAsync(int? companyId, int? locationId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> GetMatchResolutionAsync(int requestId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> AcceptWithSpaceAsync(int requestId, BranchTransferAcceptWithSpaceRequestDto request, string? userEmail, int? actorId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> ResolveMatchAsync(int requestId, BranchTransferResolveMatchRequestDto request, string? userEmail, int? actorId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> ListPendingPaymentsAsync(int? companyId, int? locationId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> ConfirmPendingPaymentAsync(int paymentId, PendingPaymentConfirmRequestDto request, string? userEmail, int? actorId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds);
    }
}

