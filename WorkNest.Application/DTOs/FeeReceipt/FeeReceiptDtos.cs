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
        /// <summary>Unpaid | Overdue (Challan Expire invoices can also get a receipt).</summary>
        public string StatusLabel { get; set; } = "Unpaid";
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
