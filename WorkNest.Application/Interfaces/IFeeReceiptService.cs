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
        Task<ApiResponse> CreateReceiptAsync(FeeReceiptCreateDto dto, int createdById, List<int>? allowedLocationIds, bool isSuperAdmin);
        Task<ApiResponse> VoidReceiptAsync(FeeReceiptVoidDto dto, int updatedById);
        Task<ApiResponse> GetListAsync(int page, int limit, string? search, int? locationId, byte? status, List<int>? allowedLocationIds);
        Task<ApiResponse> GetByIdAsync(int? id, Guid? guid);
    }
}
