using System;

namespace WorkNest.Application.DTOs.Invoice
{
    public class UpdateInvoiceIssueDateRequest
    {
        public DateTime? NewIssueDate { get; set; }
        public string? Reason { get; set; }
    }

    public class UpdateInvoiceIssueDateResult
    {
        public int InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime IssuedOn { get; set; }
        public DateTime DueOn { get; set; }
        public byte StatusId { get; set; }
        public DateTime? UpdatedOn { get; set; }
    }
}

