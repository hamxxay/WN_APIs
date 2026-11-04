using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Transfer;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ITransferService
    {
        Task<ApiResponse> GetActiveBookingsAsync(int? locationId, string? search, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> GetPreviewAsync(TransferPreviewRequest request, string? userEmail, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> ExecuteTransferAsync(TransferExecuteRequest request, string? userEmail, int? actorId, IReadOnlyCollection<int>? allowedLocationIds);
        Task<ApiResponse> GetAvailableTargetSpacesAsync(int bookingId, DateTime effectiveDate, IReadOnlyCollection<int>? allowedLocationIds);
        Task<byte[]> GenerateAddendumPdfAsync(int oldBookingId, int newBookingId, int newSpaceId, DateTime effectiveDate, string note);
    }
}
