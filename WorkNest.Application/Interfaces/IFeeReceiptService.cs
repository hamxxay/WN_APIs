using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.FeeReceipt;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IFeeReceiptService
    {
        Task<ApiResponse> GetUnpaidInvoicesAsync(string? search, int? locationId, List<int>? allowedLocationIds, int top = 50);
        Task<ApiResponse> GetAccountsAsync(int? companyId);
        Task<FeeReceiptResult> CreateReceiptAsync(FeeReceiptCreateDto dto, int createdById, List<int>? allowedLocationIds, bool isSuperAdmin);
        Task<FeeReceiptResult> VoidReceiptAsync(FeeReceiptVoidDto dto, int updatedById, List<int>? allowedLocationIds);
        Task<ApiResponse> GetListAsync(int page, int limit, string? search, int? locationId, byte? status, List<int>? allowedLocationIds);
        Task<FeeReceiptResult> GetByIdAsync(int? id, Guid? guid, List<int>? allowedLocationIds);
    }
}
