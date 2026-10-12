using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Data.Common;
using WorkNest.Application.DTOs.FeeReceipt;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class FeeReceiptService : IFeeReceiptService
    {
        private readonly IDbRepository _db;

        public FeeReceiptService(IDbRepository db)
        {
            _db = db;
        }

        public async Task<ApiResponse> GetUnpaidInvoicesAsync(string? search, int? locationId, List<int>? allowedLocationIds, int top = 50)
        {
            string? allowedCsv = allowedLocationIds != null && allowedLocationIds.Count > 0
                ? string.Join(",", allowedLocationIds)
                : null;

            var rows = await _db.GetFeeReceiptUnpaidInvoicesDbAsync(search, locationId, allowedCsv, top);
            var items = rows.Select(MapToUnpaidInvoiceDto).ToList();
            return ApiResponse.Ok(items);
        }

        public async Task<ApiResponse> GetAccountsAsync(int? companyId)
        {
            var rows = await _db.GetFeeReceiptAccountsDbAsync(companyId);
            var items = rows.Select(MapToAccountDto).ToList();
            return ApiResponse.Ok(items);
        }

        public async Task<ApiResponse> CreateReceiptAsync(FeeReceiptCreateDto dto, int createdById, List<int>? allowedLocationIds, bool isSuperAdmin)
        {
            var invoiceGuid = dto.GetResolvedInvoiceIdGuid();
            var accountId = dto.GetResolvedAccountId();

            if (invoiceGuid == Guid.Empty)
                return ApiResponse.Fail("A valid invoice must be selected.");

            if (accountId <= 0)
                return ApiResponse.Fail("A valid Cash / Bank account must be selected.");

            if (dto.ReceiptDate == default)
                return ApiResponse.Fail("A valid receipt date is required.");

            string? allowedCsv = !isSuperAdmin && allowedLocationIds != null && allowedLocationIds.Count > 0
                ? string.Join(",", allowedLocationIds)
                : null;

            try
            {
                var row = await _db.InsertFeeReceiptDbAsync(
                    invoiceGuid,
                    accountId,
                    dto.PaymentType,
                    dto.ReceiptDate,
                    dto.Remarks,
                    createdById,
                    allowedCsv
                );

                if (row == null)
                    return ApiResponse.Fail("Failed to create fee receipt.");

                var receiptDto = MapToReceiptDto(row);
                return ApiResponse.Ok(receiptDto, "Fee receipt created successfully.");
            }
            catch (DbException ex)
            {
                return ApiResponse.Fail(ex.Message);
            }
            catch (Exception ex)
            {
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> VoidReceiptAsync(FeeReceiptVoidDto dto, int updatedById)
        {
            if (string.IsNullOrWhiteSpace(dto.VoidReason))
                return ApiResponse.Fail("A reason is required to void a fee receipt.");

            var receiptId = dto.GetResolvedReceiptId();
            var receiptGuid = dto.GetResolvedReceiptGuid();

            if (!receiptId.HasValue && !receiptGuid.HasValue)
                return ApiResponse.Fail("Receipt identifier is required.");

            try
            {
                var (isSuccessful, message, resReceiptId) = await _db.VoidFeeReceiptDbAsync(
                    receiptId,
                    receiptGuid,
                    dto.VoidReason.Trim(),
                    updatedById
                );

                if (!isSuccessful)
                    return ApiResponse.Fail(message);

                return ApiResponse.Ok(new { receiptId = resReceiptId }, message);
            }
            catch (DbException ex)
            {
                return ApiResponse.Fail(ex.Message);
            }
            catch (Exception ex)
            {
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> GetListAsync(int page, int limit, string? search, int? locationId, byte? status, List<int>? allowedLocationIds)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 100);

            string? allowedCsv = allowedLocationIds != null && allowedLocationIds.Count > 0
                ? string.Join(",", allowedLocationIds)
                : null;

            var (rows, total) = await _db.GetFeeReceiptsListDbAsync(page, limit, search, locationId, status, allowedCsv);
            var items = rows.Select(MapToReceiptDto).ToList();

            return ApiResponse.Ok(new
            {
                data = items,
                totalCount = total,
                page,
                limit
            });
        }

        public async Task<ApiResponse> GetByIdAsync(int? id, Guid? guid)
        {
            if (!id.HasValue && !guid.HasValue)
                return ApiResponse.Fail("Receipt identifier is required.");

            var row = await _db.GetFeeReceiptByIdDbAsync(id, guid);
            if (row == null)
                return ApiResponse.Fail("Fee receipt not found.");

            return ApiResponse.Ok(MapToReceiptDto(row));
        }

        private static FeeReceiptUnpaidInvoiceDto MapToUnpaidInvoiceDto(IDictionary<string, object?> r)
        {
            return new FeeReceiptUnpaidInvoiceDto
            {
                InvoiceId = GetInt(r, "InvoiceId"),
                InvoiceIdGUID = GetGuid(r, "InvoiceIdGUID"),
                InvoiceNumber = GetString(r, "InvoiceNumber"),
                DisplayName = GetString(r, "DisplayName"),
                CustomerName = GetString(r, "CustomerName"),
                CustomerCode = GetStringOrNull(r, "CustomerCode"),
                CustomerEmail = GetString(r, "CustomerEmail"),
                CustomerPhone = GetStringOrNull(r, "CustomerPhone"),
                CustomerCompany = GetStringOrNull(r, "CustomerCompany"),
                CustomerIdGUID = GetGuidOrNull(r, "CustomerIdGUID"),
                IssuedOn = GetDateTime(r, "IssuedOn"),
                DueOn = GetDateTime(r, "DueOn"),
                RoomRentExclTax = GetDecimal(r, "RoomRentExclTax"),
                ServiceCharges = GetDecimal(r, "ServiceCharges"),
                TaxOnServiceCharges = GetDecimal(r, "TaxOnServiceCharges"),
                WHTAmount = GetDecimal(r, "WHTAmount"),
                WHTRate = GetDecimal(r, "WHTRate"),
                SecurityDepositAmount = GetDecimal(r, "SecurityDepositAmount"),
                GrandTotal = GetDecimal(r, "GrandTotal"),
                NetReceivableAmount = GetDecimal(r, "NetReceivableAmount"),
                InvoiceTypeId = GetInt(r, "InvoiceTypeId"),
                CurrencyCode = GetString(r, "CurrencyCode", "PKR"),
                BookingId = GetIntOrNull(r, "BookingId"),
                UserId = GetInt(r, "UserId"),
                LocationId = GetIntOrNull(r, "LocationId"),
                LocationName = GetStringOrNull(r, "LocationName"),
                CompanyId = GetIntOrNull(r, "CompanyId"),
                BranchId = GetIntOrNull(r, "BranchId")
            };
        }

        private static FeeReceiptAccountDto MapToAccountDto(IDictionary<string, object?> r)
        {
            return new FeeReceiptAccountDto
            {
                AccountId = GetInt(r, "AccountId"),
                Description = GetString(r, "Description"),
                ShortDesc = GetString(r, "ShortDesc"),
                AccountNature = GetString(r, "AccountNature"),
                CompanyId = GetIntOrNull(r, "CompanyId"),
                IsCashInHand = GetBool(r, "IsCashInHand")
            };
        }

        private static FeeReceiptDto MapToReceiptDto(IDictionary<string, object?> r)
        {
            return new FeeReceiptDto
            {
                Id = GetInt(r, "Id"),
                IdGUID = GetGuid(r, "IdGUID"),
                Code = GetString(r, "Code"),
                CustomerIdGUID = GetGuidOrNull(r, "CustomerIdGUID"),
                InvoiceIdGUID = GetGuid(r, "InvoiceIdGUID"),
                InvoiceTypeId = GetInt(r, "InvoiceTypeId"),
                TotalAmount = GetDecimal(r, "TotalAmount"),
                RoomRentExclTax = GetDecimal(r, "RoomRentExclTax"),
                ServiceCharges = GetDecimal(r, "ServiceCharges"),
                TaxOnServiceCharges = GetDecimal(r, "TaxOnServiceCharges"),
                WHTAmount = GetDecimal(r, "WHTAmount"),
                SecurityDepositAmount = GetDecimal(r, "SecurityDepositAmount"),
                CashInHandAccountId = GetInt(r, "CashInHandAccountId"),
                CashInHandAccountName = GetStringOrNull(r, "CashInHandAccountName"),
                PaymentType = GetString(r, "PaymentType", "Cash"),
                BranchId = GetIntOrNull(r, "BranchId"),
                CompanyId = GetIntOrNull(r, "CompanyId"),
                FeeStatus = GetByteOrNull(r, "FeeStatus"),
                Status = GetByte(r, "Status", 1),
                StatusLabel = GetString(r, "StatusLabel", "Active"),
                ReceiptDate = GetDateTime(r, "ReceiptDate"),
                Remarks = GetStringOrNull(r, "Remarks"),
                VoidReason = GetStringOrNull(r, "VoidReason"),
                CreatedOn = GetDateTime(r, "CreatedOn"),
                UpdatedOn = GetDateTimeOrNull(r, "UpdatedOn"),
                CreatedById = GetIntOrNull(r, "CreatedById"),
                CreatedByName = GetStringOrNull(r, "CreatedByName"),
                UpdatedById = GetIntOrNull(r, "UpdatedById"),
                UpdatedByName = GetStringOrNull(r, "UpdatedByName"),
                InvoiceId = GetIntOrNull(r, "InvoiceId"),
                InvoiceNumber = GetStringOrNull(r, "InvoiceNumber"),
                InvoiceIssuedOn = GetDateTimeOrNull(r, "InvoiceIssuedOn"),
                InvoiceDueOn = GetDateTimeOrNull(r, "InvoiceDueOn"),
                InvoiceGrandTotal = GetDecimalOrNull(r, "InvoiceGrandTotal"),
                CustomerName = GetStringOrNull(r, "CustomerName"),
                CustomerCode = GetStringOrNull(r, "CustomerCode"),
                CustomerEmail = GetStringOrNull(r, "CustomerEmail"),
                CustomerPhone = GetStringOrNull(r, "CustomerPhone"),
                CustomerCompany = GetStringOrNull(r, "CustomerCompany"),
                CustomerAddress = GetStringOrNull(r, "CustomerAddress"),
                LocationId = GetIntOrNull(r, "LocationId"),
                LocationName = GetStringOrNull(r, "LocationName"),
                LocationCity = GetStringOrNull(r, "LocationCity")
            };
        }

        private static int GetInt(IDictionary<string, object?> d, string key, int fallback = 0) =>
            d.TryGetValue(key, out var v) && v != null && int.TryParse(v.ToString(), out var i) ? i : fallback;

        private static int? GetIntOrNull(IDictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v != null && int.TryParse(v.ToString(), out var i) ? i : null;

        private static decimal GetDecimal(IDictionary<string, object?> d, string key, decimal fallback = 0m) =>
            d.TryGetValue(key, out var v) && v != null && decimal.TryParse(v.ToString(), out var dec) ? dec : fallback;

        private static decimal? GetDecimalOrNull(IDictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v != null && decimal.TryParse(v.ToString(), out var dec) ? dec : null;

        private static string GetString(IDictionary<string, object?> d, string key, string fallback = "") =>
            d.TryGetValue(key, out var v) && v != null ? v.ToString()! : fallback;

        private static string? GetStringOrNull(IDictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v != null ? v.ToString() : null;

        private static Guid GetGuid(IDictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v != null && Guid.TryParse(v.ToString(), out var g) ? g : Guid.Empty;

        private static Guid? GetGuidOrNull(IDictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v != null && Guid.TryParse(v.ToString(), out var g) ? g : null;

        private static DateTime GetDateTime(IDictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v != null && DateTime.TryParse(v.ToString(), out var dt) ? dt : default;

        private static DateTime? GetDateTimeOrNull(IDictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v != null && DateTime.TryParse(v.ToString(), out var dt) ? dt : null;

        private static byte GetByte(IDictionary<string, object?> d, string key, byte fallback = 0) =>
            d.TryGetValue(key, out var v) && v != null && byte.TryParse(v.ToString(), out var b) ? b : fallback;

        private static byte? GetByteOrNull(IDictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v != null && byte.TryParse(v.ToString(), out var b) ? b : null;

        private static bool GetBool(IDictionary<string, object?> d, string key, bool fallback = false) =>
            d.TryGetValue(key, out var v) && v != null ? Convert.ToBoolean(v) : fallback;
    }
}
