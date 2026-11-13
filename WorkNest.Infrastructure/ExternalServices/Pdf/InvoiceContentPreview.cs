using WorkNest.Application.DTOs.Payment;
using WorkNest.Application.InvoiceContent;

namespace WorkNest.Infrastructure.ExternalServices.Pdf
{
    /// <summary>
    /// The preview of the invoice-content settings page: the CURRENT invoice renderer (invoice plus its sales-tax invoice page) with fixed
    /// representative invoice data and the content being edited. Nothing is saved and nothing is sent; the amounts are sample amounts.
    /// </summary>
    public static class InvoiceContentPreview
    {
        public const string SampleOneBillNumber = "1013401034000000";

        public static StatementInvoicePdfDto SampleInvoice(int locationId, InvoiceContentFields content, bool withOneBillNumber = true, bool withDeposit = true, string? locationAddress = null)
        {
            var issued = new DateTime(2026, 11, 1);
            var due = new DateTime(2026, 11, 10);
            var periodEnd = new DateTime(2026, 11, 30);
            const decimal rent = 100000m, support = 10000m, pst = 1600m, deposit = 200000m;
            string? oneBill = withOneBillNumber ? SampleOneBillNumber : null;
            decimal dep = withDeposit ? deposit : 0m;

            var dto = new StatementInvoicePdfDto
            {
                AccountName = "Sample Customer Pvt Ltd (preview)",
                AttnName = "Sample Contact",
                BillingAddress = "Office 12, Sample Plaza, Islamabad",
                AccountNumber = "SAMPLE-0001",
                InvoiceNumber = "INV-SAMPLE-0001",
                InvoiceDate = issued, StatementDate = issued, DueDate = due,
                BillingPeriodStart = issued, BillingPeriodEnd = periodEnd,
                SntnNtnNic = "0000000-0",
                CenterName = "Sample",
                LocationId = locationId,
                VendorLegalName = "WorkNest Coworking Spaces (Pvt) Ltd",
                VendorAddress = InvoiceVendorAddress.Choose(locationAddress, null),
                VendorPhone = "+92 328 0256000",
                VendorFax = "", VendorNtn = "7492018-3", VendorLogoPath = "",
                CurrencyCode = "PKR",
                OneBillNumber = oneBill,
                InvoiceTypeId = 1,
                WithholdingTaxRate = 15m,
                SecurityDepositAmount = dep,
                SubTotal = rent + support + dep, TaxTotal = pst, CurrentInvoiceTotal = rent + support + pst + dep,
                SupportChargeAmount = support, AppliedTaxPercentage = 16m, AppliedChargePercentage = 10m,
                Content = content,
            };
            dto.LineItems.Add(new StatementInvoiceLineItemDto { Description = "Private Office 401 License Fee", FromDate = issued, ToDate = periodEnd, PriceExclVat = rent + support, VatAmount = pst, Category = "Recurring" });
            if (withDeposit)
                dto.LineItems.Add(new StatementInvoiceLineItemDto { Description = "Security Deposit (Refundable)", PriceExclVat = dep, Category = "Security Deposit", IsDeposit = true });
            dto.SalesTaxInvoice = new CustomerSTInvoiceDto
            {
                STInvoiceNumber = "ST-INV-SAMPLE-0001", ParentInvoiceNumber = dto.InvoiceNumber,
                IssuedOn = issued, DueOn = due, CurrencyCode = "PKR", CenterName = "Sample", LocationId = locationId, OneBillNumber = oneBill,
                CustomerName = dto.AccountName, CustomerAddress = dto.BillingAddress, CustomerCode = dto.AccountNumber, SntnNtnNic = dto.SntnNtnNic,
                VendorLogoPath = "", SecurityDepositAmount = withDeposit ? dep : null,
                SubTotal = support, TaxTotal = pst, GrandTotal = support + pst,
                LineItems = new List<CustomerSTInvoiceLineDto> { new() { Description = "Service Charges", ExclusiveAmount = support, TaxPercentage = 16m, TaxAmount = pst } },
                Content = content,
            };
            dto.SalesTaxInvoice.VendorAddress = dto.VendorAddress;
            return dto;
        }

        public static byte[] Render(int locationId, InvoiceContentFields content, bool withOneBillNumber = true, bool withDeposit = true, string? locationAddress = null)
            => StatementInvoicePdfGenerator.GeneratePdf(SampleInvoice(locationId, content, withOneBillNumber, withDeposit, locationAddress));
    }
}
