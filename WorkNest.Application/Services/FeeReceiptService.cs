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
        // FR_01 part payments. The database checks the amount against the invoice's balance inside the same transaction
        // that saves the receipt (the invoice row is locked), so two people paying the same invoice cannot overpay it.
        public const string FullMode = "Full";
        public const string PartialMode = "Partial";
        private const decimal MaxAmount = 999_999_999_999.99m;   // DECIMAL(18,2)

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

        public async Task<ApiResponse> GetUnpaidInvoicesAsync(string? search, int? locationId, List<int>? allowedLocationIds, int top = 200)
        {
            var today = _clock.Today;
            var rows = await _db.GetFeeReceiptUnpaidInvoicesDbAsync(CleanSearch(search), locationId, ToCsv(allowedLocationIds), top);
            var items = rows.Select(r => MapToUnpaidInvoiceDto(r, today)).ToList();
            return ApiResponse.Ok(items);
        }

        public async Task<ApiResponse> GetAccountsAsync(int? companyId)
        {
            var rows = await _db.GetFeeReceiptAccountsDbAsync(companyId is > 0 ? companyId : null);
            var items = rows.Select(MapToAccountDto).ToList();
            return ApiResponse.Ok(items);
        }

        /// <summary>FR_05: the "Receipt For" options (Room Rent, Security Deposit, Other Charges), from dbo.WN_ChargeTypes.</summary>
        public async Task<ApiResponse> GetChargeTypesAsync()
        {
            var rows = await _db.GetFeeReceiptChargeTypesDbAsync();
            return ApiResponse.Ok(rows.Select(MapToChargeTypeDto).ToList());
        }

        /// <summary>FR_05: the heads of one invoice with what is due, paid and unpaid.</summary>
        public async Task<FeeReceiptResult> GetInvoiceHeadsAsync(Guid invoiceGuid, List<int>? allowedLocationIds)
        {
            if (invoiceGuid == Guid.Empty)
                return BadRequest("A valid invoice must be selected.");
            try
            {
                var rows = await _db.GetFeeReceiptInvoiceHeadsDbAsync(invoiceGuid, ToCsv(allowedLocationIds));
                return FeeReceiptResult.Ok(ApiResponse.Ok(rows.Select(MapToInvoiceHeadDto).ToList()));
            }
            catch (FeeReceiptRuleException ex)
            {
                return FeeReceiptResult.Fail(ex.Outcome, ex.Message);
            }
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

            var depositorName = string.IsNullOrWhiteSpace(dto.DepositorName) ? null : dto.DepositorName.Trim();
            if (depositorName != null && (depositorName.Length < MinDepositorNameLength || depositorName.Length > MaxDepositorNameLength))
                return BadRequest($"Depositor name must be between {MinDepositorNameLength} and {MaxDepositorNameLength} characters.");

            string? depositorCnic = null;
            if (!string.IsNullOrWhiteSpace(dto.DepositorCnic))
            {
                depositorCnic = dto.DepositorCnic.Trim();
                if (CnicDigitsOnly.IsMatch(depositorCnic))
                    depositorCnic = $"{depositorCnic[..5]}-{depositorCnic.Substring(5, 7)}-{depositorCnic[12..]}";
                if (!CnicFormatted.IsMatch(depositorCnic))
                    return BadRequest(DepositorCnicFormatMessage);
            }

            var (payment, paymentError) = ResolvePayment(dto.PaymentMode, dto.Amount, dto.ChargeTypeId, dto.OtherDescription);
            if (paymentError != null || payment == null)
                return BadRequest(paymentError ?? "Invalid payment.");

            // Withholding-tax invoices must be paid in full in one receipt (Full mode); the database checks it too.
            if (payment.ChargeTypeId.HasValue && await _db.GetFeeReceiptInvoiceBalanceDbAsync(invoiceGuid) is { InvoiceTypeId: 6 })
                return BadRequest("Withholding-tax invoices can only be paid in full, in one receipt.");

            string? allowedCsv = isSuperAdmin ? null : ToCsv(allowedLocationIds);

            try
            {
                var row = await _db.InsertFeeReceiptDbAsync(
                    invoiceGuid,
                    accountId,
                    paymentType,
                    dto.ReceiptDate,
                    remarks,
                    depositorName,
                    depositorCnic,
                    createdById,
                    allowedCsv,
                    payment.Amount,
                    payment.ChargeTypeId,
                    payment.OtherDescription);

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
                    accessCardsNeedReview = result.AccessCardsNeedReview,
                    voidedCount = result.VoidedCount
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

        /// <summary>WN_Invoices.InvoiceTypeId of a custom invoice (CustomInvoiceService.InvoiceTypeId).</summary>
        public const int CustomInvoiceTypeId = CustomInvoiceService.InvoiceTypeId;

        /// <summary>400 for a custom invoice receipt that is not the whole balance.</summary>
        public static string CustomInvoiceFullPaymentMessage(decimal balance) =>
            $"A custom invoice must be paid in full: PKR {Math.Round(balance, 2).ToString("N2", System.Globalization.CultureInfo.InvariantCulture)}.";

        public const int MaxOtherDescriptionLength = 200;   // WN_FeeReceipts.OtherDescription NVARCHAR(200)

        /// <summary>What a receipt request asks for, checked (see <see cref="ResolvePayment"/>).</summary>
        public sealed record ReceiptPayment(decimal? Amount, int? ChargeTypeId, string? OtherDescription);

        /// <summary>
        /// FR_05. Full: nothing is sent (the database takes the whole balance at the moment of saving, never a figure the
        /// browser had). Partial: one head of the invoice ("Receipt For", required); the database takes that head's whole
        /// unpaid amount, refuses a head already received, and needs a description for Other Charges. An amount, when sent,
        /// is only the figure the page showed: the database refuses the receipt if it is no longer the head's unpaid amount.
        /// Returns an error message instead when the request is wrong.
        /// </summary>
        public static (ReceiptPayment? Payment, string? Error) ResolvePayment(string? paymentMode, decimal? amount, int? chargeTypeId, string? otherDescription)
        {
            var mode = string.IsNullOrWhiteSpace(paymentMode) ? FullMode : paymentMode.Trim();
            if (string.Equals(mode, FullMode, StringComparison.OrdinalIgnoreCase))
                return (new ReceiptPayment(null, null, null), null);
            if (!string.Equals(mode, PartialMode, StringComparison.OrdinalIgnoreCase))
                return (null, "Payment mode must be Full or Partial.");
            if (chargeTypeId is not > 0 || chargeTypeId > byte.MaxValue)
                return (null, "Choose what this receipt is for (Receipt For): Room Rent, Security Deposit or Other Charges.");
            if (amount is { } a)
            {
                if (a <= 0)
                    return (null, "The amount received must be more than zero.");
                if (a > MaxAmount)
                    return (null, "The amount received is too large.");
                if (decimal.Round(a, 2) != a)
                    return (null, "The amount received can have at most 2 decimals.");
            }
            var description = string.IsNullOrWhiteSpace(otherDescription) ? null : otherDescription.Trim();
            if (description is { Length: > MaxOtherDescriptionLength })
                return (null, $"The description of the other charges can be at most {MaxOtherDescriptionLength} characters.");
            return (new ReceiptPayment(amount, chargeTypeId, description), null);
        }

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

        public static FeeReceiptUnpaidInvoiceDto MapToUnpaidInvoiceDto(IDictionary<string, object?> r) =>
            MapToUnpaidInvoiceDto(r, null);

        public static FeeReceiptUnpaidInvoiceDto MapToUnpaidInvoiceDto(IDictionary<string, object?> r, DateTime? today)
        {
            // InvoiceTypeId: 0 when the procedure does not return it (older database).
            var invoiceTypeId = GetInt(r, "InvoiceTypeId");
            var isCustom = invoiceTypeId == CustomInvoiceTypeId;
            var isWht = invoiceTypeId == 6 || GetDecimal(r, "WHTAmount") > 0;
            var allowPartial = (Value(r, "AllowPartial") != null ? GetBool(r, "AllowPartial") : !isWht) && !isWht;
            var dueOn = GetDateTime(r, "DueOn");
            var amountPaid = Math.Round(GetDecimal(r, "AmountPaid", GetDecimal(r, "PaidTotal")), 2);
            var statusLabel = GetString(r, "StatusLabel", "Unpaid");
            var paymentStatus = GetString(r, "PaymentStatus", amountPaid > 0 ? "Partial" : "Unpaid");
            if (today.HasValue && dueOn != default && dueOn.Date < today.Value.Date)
            {
                if (string.Equals(statusLabel, "Unpaid", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(statusLabel, "Partial", StringComparison.OrdinalIgnoreCase))
                {
                    statusLabel = "Overdue";
                }
                if (amountPaid <= 0 && string.Equals(paymentStatus, "Unpaid", StringComparison.OrdinalIgnoreCase))
                {
                    paymentStatus = "Overdue";
                }
            }
            return new FeeReceiptUnpaidInvoiceDto
            {
                InvoiceId = GetInt(r, "InvoiceId", GetInt(r, "Id")),
                InvoiceIdGUID = GetGuid(r, "InvoiceIdGUID") != Guid.Empty ? GetGuid(r, "InvoiceIdGUID") : GetGuid(r, "PublicId"),
                InvoiceNumber = GetString(r, "InvoiceNumber"),
                DisplayName = GetString(r, "DisplayName", GetString(r, "CustomerDisplayName")),
                CustomerName = GetString(r, "CustomerName", GetString(r, "CustomerDisplayName")),
                CustomerCode = GetStringOrNull(r, "CustomerCode"),
                CustomerEmail = GetString(r, "CustomerEmail"),
                CustomerPhone = GetStringOrNull(r, "CustomerPhone") ?? GetStringOrNull(r, "PhoneNumber"),
                CustomerCompany = GetStringOrNull(r, "CustomerCompany") ?? GetStringOrNull(r, "Company"),
                CustomerIdGUID = GetGuidOrNull(r, "CustomerIdGUID"),
                IssuedOn = GetDateTime(r, "IssuedOn"),
                DueOn = dueOn,
                RoomRentExclTax = GetDecimal(r, "RoomRentExclTax"),
                ServiceCharges = GetDecimal(r, "ServiceCharges"),
                TaxOnServiceCharges = GetDecimal(r, "TaxOnServiceCharges"),
                WHTAmount = GetDecimal(r, "WHTAmount"),
                WHTRate = GetDecimal(r, "WHTRate"),
                SecurityDepositAmount = GetDecimal(r, "SecurityDepositAmount"),
                GrandTotal = GetDecimal(r, "GrandTotal"),
                NetReceivableAmount = GetDecimal(r, "NetReceivableAmount"),
                InvoiceTypeId = invoiceTypeId,
                IsCustom = isCustom,
                CurrencyCode = GetString(r, "CurrencyCode", "PKR"),
                BookingId = GetIntOrNull(r, "BookingId"),
                UserId = GetInt(r, "UserId"),
                LocationId = GetIntOrNull(r, "LocationId"),
                LocationName = GetStringOrNull(r, "LocationName"),
                CompanyId = GetIntOrNull(r, "CompanyId"),
                BranchId = GetIntOrNull(r, "BranchId"),
                StatusId = GetInt(r, "StatusId"),
                StatusLabel = statusLabel,
                // FR_01 columns; before the database update: nothing paid (the list then has only unpaid invoices) and
                // no part payments.
                AmountPaid = amountPaid,
                BalanceAmount = Value(r, "BalanceAmount") is not null
                    ? GetDecimal(r, "BalanceAmount")
                    : Math.Max(0, Math.Round(GetDecimal(r, "NetReceivableAmount") - GetDecimal(r, "PaidTotal"), 2)),
                PaymentStatus = paymentStatus,
                // AllowPartial: standard and custom invoices allow part payments; WHT invoices must be paid in full.
                AllowPartial = allowPartial
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
            var firstName = GetStringOrNull(r, "CustomerFirstName") ?? GetStringOrNull(r, "FirstName");
            var lastName = GetStringOrNull(r, "CustomerLastName") ?? GetStringOrNull(r, "LastName");
            var fullName = $"{firstName} {lastName}".Trim();
            var customerCompany = GetStringOrNull(r, "CustomerCompany") ?? GetStringOrNull(r, "Company");
            var customerName = GetStringOrNull(r, "CustomerName")
                ?? (fullName.Length > 0 ? fullName : null)
                ?? GetStringOrNull(r, "CustomerDisplayName")
                ?? customerCompany;

            return new FeeReceiptDto
            {
                Id = GetInt(r, "Id"),
                IdGUID = GetGuid(r, "IdGUID"),
                Code = GetString(r, "Code"),
                CustomerIdGUID = GetGuidOrNull(r, "CustomerIdGUID"),
                InvoiceIdGUID = GetGuid(r, "InvoiceIdGUID") != Guid.Empty ? GetGuid(r, "InvoiceIdGUID") : GetGuid(r, "PublicId"),
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
                CustomerName = customerName,
                CustomerCode = GetStringOrNull(r, "CustomerCode"),
                CustomerEmail = GetStringOrNull(r, "CustomerEmail"),
                CustomerPhone = GetStringOrNull(r, "CustomerPhone") ?? GetStringOrNull(r, "PhoneNumber"),
                CustomerCompany = customerCompany,
                CustomerAddress = GetStringOrNull(r, "CustomerAddress"),
                LocationId = GetIntOrNull(r, "LocationId"),
                LocationName = GetStringOrNull(r, "LocationName"),
                LocationCity = GetStringOrNull(r, "LocationCity"),
                InvoiceTotal = GetDecimalOrNull(r, "InvoiceTotal"),
                PreviousPaid = GetDecimalOrNull(r, "PreviousPaid"),
                AmountPaid = Math.Round(GetDecimal(r, "AmountPaid", GetDecimal(r, "TotalAmount")), 2),
                BalanceAfter = GetDecimalOrNull(r, "BalanceAfter"),
                InvoiceBalanceNow = GetDecimalOrNull(r, "InvoiceBalanceNow"),
                InvoicePaymentStatus = GetStringOrNull(r, "InvoicePaymentStatus"),
                BillingMonth = GetDateTimeOrNull(r, "BillingMonth"),
                MonthNo = Math.Max(1, GetInt(r, "MonthNo", 1)),
                MonthCount = Math.Max(1, GetInt(r, "MonthCount", 1)),
                ChargeTypeId = GetIntOrNull(r, "ChargeTypeId"),
                ChargeTypeName = GetStringOrNull(r, "ChargeTypeName"),
                OtherDescription = GetStringOrNull(r, "OtherDescription")
            };
        }

        public static FeeReceiptChargeTypeDto MapToChargeTypeDto(IDictionary<string, object?> r) => new()
        {
            ChargeTypeId = GetInt(r, "ChargeTypeId"),
            Code = GetString(r, "Code"),
            DisplayName = GetString(r, "DisplayName")
        };

        public static FeeReceiptInvoiceHeadDto MapToInvoiceHeadDto(IDictionary<string, object?> r)
        {
            var unpaid = Math.Round(GetDecimal(r, "Unpaid"), 2);
            return new FeeReceiptInvoiceHeadDto
            {
                ChargeTypeId = GetInt(r, "ChargeTypeId"),
                Code = GetString(r, "Code"),
                DisplayName = GetString(r, "DisplayName"),
                Due = Math.Round(GetDecimal(r, "Due"), 2),
                Paid = Math.Round(GetDecimal(r, "Paid"), 2),
                Unpaid = unpaid,
                CanReceive = unpaid > 0 && !GetBool(r, "IsWhtInvoice")
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
