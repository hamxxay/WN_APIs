using System;
using WorkNest.Common.Responses;

namespace WorkNest.Application.DTOs.FeeReceipt
{
    public class FeeReceiptCreateDto
    {
        public Guid InvoiceIdGUID { get; set; }
        public int CashInHandAccountId { get; set; }
        public int? AccountId { get; set; }
        public string PaymentType { get; set; } = "Cash";
        public DateTime ReceiptDate { get; set; }
        public string? Remarks { get; set; }
        /// <summary>Person who paid at the counter. Optional (2-150 characters when provided).</summary>
        public string? DepositorName { get; set; }
        /// <summary>Depositor's CNIC: 12345-1234567-1, or 13 digits without dashes (the API adds them). Optional.</summary>
        public string? DepositorCnic { get; set; }
        /// <summary>
        /// "Full" (default): the invoice's whole remaining balance, worked out by the database at the moment of saving.
        /// "Partial": one head of the invoice (<see cref="ChargeTypeId"/>), its whole unpaid amount (FR_05). Not for
        /// withholding-tax invoices.
        /// </summary>
        public string? PaymentMode { get; set; }
        /// <summary>
        /// Partial only: the head's unpaid amount the page showed. Optional; when sent, the database refuses the receipt if
        /// the head's unpaid amount has changed since (the page was out of date). Ignored for Full.
        /// </summary>
        public decimal? Amount { get; set; }
        /// <summary>
        /// "Receipt For" (Partial, required): a WN_ChargeTypes receipt head from GET api/fee-receipts/charge-types
        /// (Room Rent, Security Deposit, Other Charges). Ignored for Full.
        /// </summary>
        public int? ChargeTypeId { get; set; }
        /// <summary>What the other charges were: required when <see cref="ChargeTypeId"/> is Other Charges, at most 200 characters.</summary>
        public string? OtherDescription { get; set; }

        public Guid GetResolvedInvoiceIdGuid() => InvoiceIdGUID;

        public int GetResolvedAccountId()
        {
            if (CashInHandAccountId > 0) return CashInHandAccountId;
            if (AccountId.HasValue && AccountId.Value > 0) return AccountId.Value;
            return 0;
        }
    }

    public class FeeReceiptVoidDto
    {
        public int? Id { get; set; }
        public int? ReceiptId { get; set; }
        public Guid? ReceiptGUID { get; set; }
        public string VoidReason { get; set; } = string.Empty;

        public int? GetResolvedReceiptId() => Id.HasValue && Id.Value > 0 ? Id : ReceiptId;
        public Guid? GetResolvedReceiptGuid() => ReceiptGUID;
    }

    public class FeeReceiptUnpaidInvoiceDto
    {
        public int InvoiceId { get; set; }
        public Guid InvoiceIdGUID { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerCode { get; set; }
        public string CustomerEmail { get; set; } = string.Empty;
        public string? CustomerPhone { get; set; }
        public string? CustomerCompany { get; set; }
        public Guid? CustomerIdGUID { get; set; }
        public DateTime IssuedOn { get; set; }
        public DateTime DueOn { get; set; }
        public decimal RoomRentExclTax { get; set; }
        public decimal ServiceCharges { get; set; }
        public decimal TaxOnServiceCharges { get; set; }
        public decimal WHTAmount { get; set; }
        public decimal WHTRate { get; set; }
        public decimal SecurityDepositAmount { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal NetReceivableAmount { get; set; }
        public int InvoiceTypeId { get; set; }
        public string CurrencyCode { get; set; } = "PKR";
        public int? BookingId { get; set; }
        public int UserId { get; set; }
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        public int? CompanyId { get; set; }
        public int? BranchId { get; set; }
        public int StatusId { get; set; }
        /// <summary>Unpaid | Partial | Overdue (Challan Expire invoices can also get a receipt).</summary>
        public string StatusLabel { get; set; } = "Unpaid";
        /// <summary>Paid so far (net of WHT), 2 decimals.</summary>
        public decimal AmountPaid { get; set; }
        /// <summary>Left to pay: <see cref="NetReceivableAmount"/> less <see cref="AmountPaid"/>, never below 0.</summary>
        public decimal BalanceAmount { get; set; }
        /// <summary>Unpaid | Partial | Paid.</summary>
        public string PaymentStatus { get; set; } = "Unpaid";
        /// <summary>
        /// False for withholding-tax invoices (paid in full only) and before the database update FR_01. Standard and custom invoices allow partial payments.

        /// </summary>
        public bool AllowPartial { get; set; }
        /// <summary>A custom invoice (InvoiceTypeId 7, script CI_01).</summary>
        public bool IsCustom { get; set; }
    }

    /// <summary>A "Receipt For" option (dbo.WN_FeeReceipts_GetChargeTypes, FR_05).</summary>
    public class FeeReceiptChargeTypeDto
    {
        public int ChargeTypeId { get; set; }
        /// <summary>WN_ChargeTypes.Code: Rent | SecurityDeposit | Other.</summary>
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    /// <summary>One head of an invoice (dbo.WN_FeeReceipts_GetInvoiceHeads, FR_05): what it asks, what is paid, what is left.</summary>
    public class FeeReceiptInvoiceHeadDto
    {
        public int ChargeTypeId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public decimal Due { get; set; }
        public decimal Paid { get; set; }
        public decimal Unpaid { get; set; }
        /// <summary>A receipt can be made for this head: something is unpaid and the invoice is not a withholding-tax invoice.</summary>
        public bool CanReceive { get; set; }
    }

    public class FeeReceiptAccountDto
    {
        public int AccountId { get; set; }
        public string Description { get; set; } = string.Empty;
        public string ShortDesc { get; set; } = string.Empty;
        public string AccountNature { get; set; } = string.Empty;
        public int? CompanyId { get; set; }
        public bool IsCashInHand { get; set; }
    }

    public class FeeReceiptDto
    {
        public int Id { get; set; }
        public Guid IdGUID { get; set; }
        public string Code { get; set; } = string.Empty;
        public Guid? CustomerIdGUID { get; set; }
        public Guid InvoiceIdGUID { get; set; }
        public int InvoiceTypeId { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal RoomRentExclTax { get; set; }
        public decimal ServiceCharges { get; set; }
        public decimal TaxOnServiceCharges { get; set; }
        public decimal WHTAmount { get; set; }
        public decimal SecurityDepositAmount { get; set; }
        public int CashInHandAccountId { get; set; }
        public string? CashInHandAccountName { get; set; }
        public string PaymentType { get; set; } = "Cash";
        public int? BranchId { get; set; }
        public int? CompanyId { get; set; }
        public byte? FeeStatus { get; set; }
        public byte Status { get; set; }
        public string StatusLabel { get; set; } = "Active";
        public DateTime ReceiptDate { get; set; }
        public string? Remarks { get; set; }
        public string? VoidReason { get; set; }
        /// <summary>NULL on receipts saved before the depositor fields were added.</summary>
        public string? DepositorName { get; set; }
        public string? DepositorCnic { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime? UpdatedOn { get; set; }
        public int? CreatedById { get; set; }
        public string? CreatedByName { get; set; }
        public int? UpdatedById { get; set; }
        public string? UpdatedByName { get; set; }
        public int? InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime? InvoiceIssuedOn { get; set; }
        public DateTime? InvoiceDueOn { get; set; }
        public decimal? InvoiceGrandTotal { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }
        public string? CustomerCompany { get; set; }
        public string? CustomerAddress { get; set; }
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        public string? LocationCity { get; set; }
        // FR_01 snapshot, taken when the receipt was saved (printed receipts show these, never recalculated values).
        /// <summary>What the invoice asked (net of WHT). Null only before the database update FR_01.</summary>
        public decimal? InvoiceTotal { get; set; }
        /// <summary>Paid on the invoice before this receipt.</summary>
        public decimal? PreviousPaid { get; set; }
        /// <summary>Received in this receipt (same as <see cref="TotalAmount"/>, 2 decimals).</summary>
        public decimal AmountPaid { get; set; }
        /// <summary>Left on the invoice after this receipt.</summary>
        public decimal? BalanceAfter { get; set; }
        /// <summary>The invoice's balance today (changes with later receipts; not for printing).</summary>
        public decimal? InvoiceBalanceNow { get; set; }
        public string? InvoicePaymentStatus { get; set; }
        // RC_02 monthly receipts: one receipt per calendar month of a multi-month invoice. Receipt 1 is saved by staff;
        // months 2..N are created at the start of each month. Older receipts (NULL columns) are month 1 of 1.
        /// <summary>1st of the month this receipt is for; null on receipts saved before RC_02.</summary>
        public DateTime? BillingMonth { get; set; }
        public int MonthNo { get; set; } = 1;
        public int MonthCount { get; set; } = 1;
        // FR_05 "Receipt For". Null before the database update FR_05.
        public int? ChargeTypeId { get; set; }
        /// <summary>Room Rent | Security Deposit | Other Charges (WN_ChargeTypes.DisplayName).</summary>
        public string? ChargeTypeName { get; set; }
        public string? OtherDescription { get; set; }
    }

    /// <summary>How a fee receipt call ended; the controller maps it to the HTTP status.</summary>
    public enum FeeReceiptOutcome
    {
        Ok,
        BadRequest,
        NotFound,
        Forbidden
    }

    public sealed class FeeReceiptResult
    {
        public FeeReceiptOutcome Outcome { get; init; }
        public ApiResponse Response { get; init; } = ApiResponse.Ok();

        public static FeeReceiptResult Ok(ApiResponse response) => new() { Outcome = FeeReceiptOutcome.Ok, Response = response };
        public static FeeReceiptResult Fail(FeeReceiptOutcome outcome, string message) => new() { Outcome = outcome, Response = ApiResponse.Fail(message) };
    }

    /// <summary>
    /// A business rule raised by a WN_FeeReceipts_* procedure (RAISERROR, error number 50000).
    /// State: 1 = rule broken (400), 2 = not found (404), 3 = outside the caller's locations (403).
    /// Thrown by the repository so the message can be shown; any other database error is not caught.
    /// </summary>
    public sealed class FeeReceiptRuleException : Exception
    {
        public byte State { get; }

        public FeeReceiptRuleException(string message, byte state, Exception inner) : base(message, inner)
        {
            State = state;
        }

        public FeeReceiptOutcome Outcome => State switch
        {
            2 => FeeReceiptOutcome.NotFound,
            3 => FeeReceiptOutcome.Forbidden,
            _ => FeeReceiptOutcome.BadRequest
        };
    }
}
