using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.FeeReceipt;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class FeeReceiptService : IFeeReceiptService
    {
        private const int MaxTextLength = 500;     // Remarks / VoidReason columns are NVARCHAR(500)
        private const int MaxSearchLength = 60;    // escaped search must fit the procedures' NVARCHAR(200)
        private const int MinDepositorNameLength = 2;
        private const int MaxDepositorNameLength = 150;   // DepositorName column is NVARCHAR(150)
        private const string DepositorCnicFormatMessage = "Depositor CNIC must be in the format 12345-1234567-1.";

        // ASCII digits only ([0-9], not \d, which also matches other scripts' digits) - same as CK_WN_FeeReceipts_DepositorCNIC.
        private static readonly Regex CnicDigitsOnly = new(@"^[0-9]{13}$", RegexOptions.Compiled);
        private static readonly Regex CnicFormatted = new(@"^[0-9]{5}-[0-9]{7}-[0-9]$", RegexOptions.Compiled);

        // Payment types a fee receipt can record (cash or bank accounts from WN_FeeReceipts_GetAccounts).
        // The web form currently sends only "Cash"; values are stored in this canonical spelling.
        private static readonly string[] AllowedPaymentTypes = { "Cash", "Bank Transfer", "Cheque", "Card" };

        private readonly IDbRepository _db;
        private readonly IBusinessClock _clock;

        public FeeReceiptService(IDbRepository db, IBusinessClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<ApiResponse> GetUnpaidInvoicesAsync(string? search, int? locationId, List<int>? allowedLocationIds, int top = 50)
        {
            var rows = await _db.GetFeeReceiptUnpaidInvoicesDbAsync(CleanSearch(search), locationId, ToCsv(allowedLocationIds), top);
            var items = rows.Select(MapToUnpaidInvoiceDto).ToList();
            return ApiResponse.Ok(items);
        }

        public async Task<ApiResponse> GetAccountsAsync(int? companyId)
        {
            var rows = await _db.GetFeeReceiptAccountsDbAsync(companyId is > 0 ? companyId : null);
            var items = rows.Select(MapToAccountDto).ToList();
            return ApiResponse.Ok(items);
        }

        public async Task<FeeReceiptResult> CreateReceiptAsync(FeeReceiptCreateDto dto, int createdById, List<int>? allowedLocationIds, bool isSuperAdmin)
        {
            var invoiceGuid = dto.GetResolvedInvoiceIdGuid();
            var accountId = dto.GetResolvedAccountId();

            if (invoiceGuid == Guid.Empty)
                return BadRequest("A valid invoice must be selected.");

            if (accountId <= 0)
                return BadRequest("A valid Cash / Bank account must be selected.");

            if (dto.ReceiptDate == default)
                return BadRequest("A valid receipt date is required.");

            if (dto.ReceiptDate.Date > _clock.Today)
                return BadRequest("Receipt date cannot be in the future.");

            var paymentType = string.IsNullOrWhiteSpace(dto.PaymentType)
                ? "Cash"
                : AllowedPaymentTypes.FirstOrDefault(t => string.Equals(t, dto.PaymentType.Trim(), StringComparison.OrdinalIgnoreCase));
            if (paymentType == null)
                return BadRequest($"Payment type must be one of: {string.Join(", ", AllowedPaymentTypes)}.");

            var remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();
            if (remarks != null && remarks.Length > MaxTextLength)
                return BadRequest($"Remarks cannot be longer than {MaxTextLength} characters.");

            var depositorName = dto.DepositorName?.Trim();
            if (string.IsNullOrEmpty(depositorName))
                return BadRequest("Depositor name is required.");
            if (depositorName.Length < MinDepositorNameLength || depositorName.Length > MaxDepositorNameLength)
                return BadRequest($"Depositor name must be between {MinDepositorNameLength} and {MaxDepositorNameLength} characters.");

            var depositorCnic = dto.DepositorCnic?.Trim();
            if (string.IsNullOrEmpty(depositorCnic))
                return BadRequest("Depositor CNIC is required.");
            if (CnicDigitsOnly.IsMatch(depositorCnic))
                depositorCnic = $"{depositorCnic[..5]}-{depositorCnic.Substring(5, 7)}-{depositorCnic[12..]}";
            if (!CnicFormatted.IsMatch(depositorCnic))
                return BadRequest(DepositorCnicFormatMessage);

            string? allowedCsv = isSuperAdmin ? null : ToCsv(allowedLocationIds);

            try
            {
                var row = await _db.InsertFeeReceiptDbAsync(
                    invoiceGuid,
                    accountId,
                    paymentType,
                    dto.ReceiptDate.Date,
                    remarks,
                    depositorName,
                    depositorCnic,
                    createdById,
                    allowedCsv);

                if (row == null)
                    throw new InvalidOperationException("WN_FeeReceipts_Insert returned no receipt row.");

                return FeeReceiptResult.Ok(ApiResponse.Ok(MapToReceiptDto(row), "Fee receipt created successfully."));
            }
            catch (FeeReceiptRuleException ex)
            {
                // Business rule raised by the procedure (safe to show). Anything else goes to ExceptionMiddleware.
                return FeeReceiptResult.Fail(ex.Outcome, ex.Message);
            }
        }

        public async Task<FeeReceiptResult> VoidReceiptAsync(FeeReceiptVoidDto dto, int updatedById, List<int>? allowedLocationIds)
        {
            var reason = dto.VoidReason?.Trim();
            if (string.IsNullOrEmpty(reason))
                return BadRequest("A reason is required to void a fee receipt.");

            if (reason.Length > MaxTextLength)
                return BadRequest($"Void reason cannot be longer than {MaxTextLength} characters.");

            var receiptId = dto.GetResolvedReceiptId();
            var receiptGuid = dto.GetResolvedReceiptGuid();

            if (receiptId is not > 0)
                receiptId = null;
            if (receiptGuid == Guid.Empty)
                receiptGuid = null;

            if (!receiptId.HasValue && !receiptGuid.HasValue)
                return BadRequest("Receipt identifier is required.");

            try
            {
                var result = await _db.VoidFeeReceiptDbAsync(
                    receiptId,
                    receiptId.HasValue ? null : receiptGuid,   // Id wins when both are sent
                    reason,
                    updatedById,
                    ToCsv(allowedLocationIds));

                if (!result.IsSuccessful)
                    return BadRequest(result.Message);

                return FeeReceiptResult.Ok(ApiResponse.Ok(new
                {
                    receiptId = result.ReceiptId,
                    invoiceRestored = result.InvoiceRestored,
                    accessCardsNeedReview = result.AccessCardsNeedReview
                }, result.Message));
            }
            catch (FeeReceiptRuleException ex)
            {
                return FeeReceiptResult.Fail(ex.Outcome, ex.Message);
            }
        }

        public async Task<ApiResponse> GetListAsync(int page, int limit, string? search, int? locationId, byte? status, List<int>? allowedLocationIds)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 100);

            var (rows, total) = await _db.GetFeeReceiptsListDbAsync(page, limit, CleanSearch(search), locationId, status, ToCsv(allowedLocationIds));
            var items = rows.Select(MapToReceiptDto).ToList();

            return ApiResponse.Ok(new
            {
                data = items,
                totalCount = total,
                page,
                limit
            });
        }

        public async Task<FeeReceiptResult> GetByIdAsync(int? id, Guid? guid, List<int>? allowedLocationIds)
        {
            if (!id.HasValue && !guid.HasValue)
                return BadRequest("Receipt identifier is required.");

            // Receipts outside the caller's locations are not returned, so they read as not found.
            var row = await _db.GetFeeReceiptByIdDbAsync(id, id.HasValue ? null : guid, ToCsv(allowedLocationIds));
            if (row == null)
                return FeeReceiptResult.Fail(FeeReceiptOutcome.NotFound, "Fee receipt not found.");

            return FeeReceiptResult.Ok(ApiResponse.Ok(MapToReceiptDto(row)));
        }

        private static FeeReceiptResult BadRequest(string message) => FeeReceiptResult.Fail(FeeReceiptOutcome.BadRequest, message);

        /// <summary>NULL = every location; otherwise the caller's ids (the controller sends -1 for "none", which matches nothing).</summary>
        private static string? ToCsv(List<int>? allowedLocationIds) =>
            allowedLocationIds == null ? null : string.Join(",", allowedLocationIds);

        /// <summary>Trims, caps the length and escapes LIKE wildcards ([ % _) so the text is matched literally.</summary>
        private static string? CleanSearch(string? search)
        {
            if (string.IsNullOrWhiteSpace(search)) return null;
            var s = search.Trim();
            if (s.Length > MaxSearchLength) s = s.Substring(0, MaxSearchLength);
            return s.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
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
                BranchId = GetIntOrNull(r, "BranchId"),
                StatusId = GetInt(r, "StatusId"),
                StatusLabel = GetString(r, "StatusLabel", "Unpaid")
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
                DepositorName = GetStringOrNull(r, "DepositorName"),
                DepositorCnic = GetStringOrNull(r, "DepositorCNIC"),
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

        // Row values come typed from SqlDataReader (DBNull already turned into null), so convert directly:
        // no string round-trips, and DateTime values keep their Kind (UTC audit columns serialise with "Z").
        private static object? Value(IDictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v is not null and not DBNull ? v : null;

        private static int GetInt(IDictionary<string, object?> d, string key, int fallback = 0) =>
            Value(d, key) is { } v ? Convert.ToInt32(v) : fallback;

        private static int? GetIntOrNull(IDictionary<string, object?> d, string key) =>
            Value(d, key) is { } v ? Convert.ToInt32(v) : null;

        private static decimal GetDecimal(IDictionary<string, object?> d, string key, decimal fallback = 0m) =>
            Value(d, key) is { } v ? Convert.ToDecimal(v) : fallback;

        private static decimal? GetDecimalOrNull(IDictionary<string, object?> d, string key) =>
            Value(d, key) is { } v ? Convert.ToDecimal(v) : null;

        private static string GetString(IDictionary<string, object?> d, string key, string fallback = "") =>
            Value(d, key)?.ToString() ?? fallback;

        private static string? GetStringOrNull(IDictionary<string, object?> d, string key) =>
            Value(d, key)?.ToString();

        private static Guid GetGuid(IDictionary<string, object?> d, string key) =>
            GetGuidOrNull(d, key) ?? Guid.Empty;

        private static Guid? GetGuidOrNull(IDictionary<string, object?> d, string key) =>
            Value(d, key) switch
            {
                Guid g => g,
                { } v when Guid.TryParse(v.ToString(), out var g) => g,
                _ => null
            };

        private static DateTime GetDateTime(IDictionary<string, object?> d, string key) =>
            GetDateTimeOrNull(d, key) ?? default;

        private static DateTime? GetDateTimeOrNull(IDictionary<string, object?> d, string key) =>
            Value(d, key) switch
            {
                DateTime dt => dt,
                DateTimeOffset dto => dto.UtcDateTime,
                { } v => Convert.ToDateTime(v),
                _ => null
            };

        private static byte GetByte(IDictionary<string, object?> d, string key, byte fallback = 0) =>
            Value(d, key) is { } v ? Convert.ToByte(v) : fallback;

        private static byte? GetByteOrNull(IDictionary<string, object?> d, string key) =>
            Value(d, key) is { } v ? Convert.ToByte(v) : null;

        private static bool GetBool(IDictionary<string, object?> d, string key, bool fallback = false) =>
            Value(d, key) is { } v ? Convert.ToBoolean(v) : fallback;
    }
}
