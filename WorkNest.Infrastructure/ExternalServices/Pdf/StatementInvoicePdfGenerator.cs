using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WorkNest.Infrastructure.ExternalServices.Pdf
{
    public class StatementInvoicePdfDto
    {
        public string AccountName { get; set; } = "Acme Global Solutions Ltd.";
        public string AttnName { get; set; } = "John Doe";
        public string BillingAddress { get; set; } = "Suite 401, 4th Floor, Commercial Tower, Sector F-8, Islamabad";
        public string AccountNumber { get; set; } = "ACC-987654";
        public string InvoiceNumber { get; set; } = "INV-2026-0042";
        public DateTime StatementDate { get; set; } = DateTime.Now;
        public DateTime InvoiceDate { get; set; } = DateTime.Now;
        public DateTime DueDate { get; set; } = DateTime.Now.AddDays(15);
        public DateTime? BillingPeriodStart { get; set; }
        
        public DateTime? BillingPeriodEnd { get; set; }
        public string SntnNtnNic { get; set; } = "NTN-4029182-7";
        public string CenterName { get; set; } = "WorkNest - I-8 Executive Center";

        public decimal PreviousOutstandingBalance { get; set; } = 150000.00m;
        public decimal PaymentReceived { get; set; } = 150000.00m;
        public decimal CurrentInvoiceTotal { get; set; } = 287500.00m;
        public decimal TotalOutstandingDue => PreviousOutstandingBalance - PaymentReceived + CurrentInvoiceTotal;

        public decimal VatRate { get; set; } = 0.16m;
        public decimal SupportChargeRate { get; set; } = 0.10m;
        public decimal? SupportChargeAmount { get; set; }
        public decimal? AppliedChargePercentage { get; set; }
        public decimal? AppliedTaxPercentage { get; set; }
        public decimal SecurityDepositAmount { get; set; }
        public string CurrencyCode { get; set; } = "PKR";

        public List<StatementInvoiceLineItemDto> LineItems { get; set; } = new();

        // Bank Details
        public string BankName { get; set; } = "Meezan Bank Limited";
        public string BankAddress { get; set; } = "I-8 Markaz Branch, Islamabad, Pakistan";
        public string BranchCode { get; set; } = "0201";
        public string BankAccountName { get; set; } = "WorkNest Coworking Spaces (Pvt) Ltd";
        public string BankAccountNumber { get; set; } = "01029384756102";
        public string SwiftBic { get; set; } = "MEEZPKKA";
        public string Iban { get; set; } = "PK36MEEZ000201010293847561";

        // Vendor Details
        public string VendorLegalName { get; set; } = "WorkNest Coworking Spaces (Pvt) Ltd";
        public string VendorAddress { get; set; } = "3rd Floor EOBI Building-II, I-8 Markaz, Islamabad, Pakistan";
        public string VendorPhone { get; set; } = "+92 309 9771774 / +92 308 0256000";
        public string VendorFax { get; set; } = "+92 51 8439201";
        public string VendorNtn { get; set; } = "7492018-3";
        public string VendorLogoPath { get; set; } = @"F:\WorkNest_FE\public\images\Logo.png";
        public string OnlinePaymentUrl { get; set; } = string.Empty;
    }

    public class StatementInvoiceLineItemDto
    {
        public string Description { get; set; } = string.Empty;
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public decimal PriceExclVat { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalInclVat => PriceExclVat + VatAmount;
        public string Category { get; set; } = "Recurring"; // "Recurring", "One-off", "IT Services"
        public bool IsDeposit { get; set; }
    }

    public class StatementInvoicePdfGenerator
    {
        static StatementInvoicePdfGenerator()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }



        public static byte[] GeneratePdf(StatementInvoicePdfDto data)
        {
            if (data == null) data = GetSampleData();
            if (data.LineItems == null || data.LineItems.Count == 0)
            {
                data.LineItems = GetDefaultLineItems(data.VatRate);
            }
            if (data.SecurityDepositAmount > 0 && !data.LineItems.Any(i => IsDepositItem(i)))
            {
                data.LineItems.Add(new StatementInvoiceLineItemDto
                {
                    Description = "Security Deposit (Refundable)",
                    PriceExclVat = data.SecurityDepositAmount,
                    VatAmount = 0m,
                    Category = "Security Deposit",
                    IsDeposit = true
                });
            }

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    page.Header().Element(c => ComposeHeader(c, data));
                    page.Footer().Element(c => ComposeFooter(c, data));

                    page.Content().Column(col =>
                    {
                        // PAGE 1 — Statement of Account
                        // ComposePage1(col, data);

                        // col.Item().PageBreak();

                        // PAGE 2 — Invoice
                        ComposePage2(col, data);

                        // col.Item().PageBreak();

                        // PAGE 3 — Your Invoice Details
                        // ComposePage3(col, data);

                        // col.Item().PageBreak();

                        // PAGE 4 — Methods of Payment + Understanding Your Invoice (Part 1)
                        // ComposePage4(col, data);

                        // col.Item().PageBreak();

                        // PAGE 5 — Understanding Your Invoice (Part 2)
                        // ComposePage5(col, data);
                    });
                });
            }).GeneratePdf();
        }

        // #region Page 1 — Statement of Account
        // private static void ComposePage1(ColumnDescriptor col, StatementInvoicePdfDto data)
        // {
        //     col.Item().Row(r =>
        //     {
        //         r.RelativeItem().Column(c =>
        //         {
        //             c.Item().Text("STATEMENT OF ACCOUNT").FontSize(16).Bold().FontColor("#1a1a2e");
        //             c.Item().Text(data.CenterName).FontSize(10).FontColor("#555555").Bold();
        //         });
        //     });

        //     col.Item().PaddingTop(10).Row(r =>
        //     {
        //         r.RelativeItem().Column(c =>
        //         {
        //             c.Item().Text("ACCOUNT DETAILS").FontSize(8).Bold().FontColor("#888888");
        //             c.Item().Text(data.AccountName).Bold().FontSize(10).FontColor("#1a1a2e");
        //             if (!string.IsNullOrWhiteSpace(data.AttnName))
        //                 c.Item().Text($"Attn: {data.AttnName}").FontColor("#444444");
        //             c.Item().Text(data.BillingAddress).FontColor("#555555");
        //         });

        //         r.ConstantItem(220).Column(c =>
        //         {
        //             c.Item().Row(row => { row.RelativeItem().Text("Customer Code:").FontColor("#666666"); row.AutoItem().Text(data.AccountNumber).Bold(); });
        //             c.Item().Row(row => { row.RelativeItem().Text("Invoice Number:").FontColor("#666666"); row.AutoItem().Text(data.InvoiceNumber).Bold(); });
        //             c.Item().Row(row => { row.RelativeItem().Text("Statement Date:").FontColor("#666666"); row.AutoItem().Text($"{data.StatementDate:dd MMM yyyy}"); });
        //             c.Item().Row(row => { row.RelativeItem().Text("Due Date:").FontColor("#666666"); row.AutoItem().Text($"{data.DueDate:dd MMM yyyy}").Bold().FontColor("#d9534f"); });
        //         });
        //     });

        //     col.Item().PaddingTop(12).Border(1).BorderColor("#e0e0e0").Background("#f8f9fa").Padding(8).Column(c =>
        //     {
        //         c.Item().Text("USEFUL INFORMATION").Bold().FontSize(9).FontColor("#1a1a2e");
        //         c.Item().Text("Need help with your account or invoice? Visit our online customer portal or contact community support.").FontSize(8).FontColor("#555555");
        //         c.Item().Text($"Support Phone: {data.VendorPhone} | Email: support@worknestpk.com").FontSize(8).FontColor("#1a1a2e");
        //     });

        //     col.Item().PaddingTop(15).Table(table =>
        //     {
        //         table.ColumnsDefinition(columns =>
        //         {
        //             columns.ConstantColumn(100);
        //             columns.RelativeColumn();
        //             columns.ConstantColumn(140);
        //         });

        //         table.Header(h =>
        //         {
        //             h.Cell().Background("#1a1a2e").Padding(6).Text("Date").Bold().FontColor("#ffffff");
        //             h.Cell().Background("#1a1a2e").Padding(6).Text("Description").Bold().FontColor("#ffffff");
        //             h.Cell().Background("#1a1a2e").Padding(6).AlignRight().Text("Amount").Bold().FontColor("#ffffff");
        //         });

        //         table.Cell().BorderBottom(1).BorderColor("#e0e0e0").Padding(6).Text($"{data.StatementDate.AddMonths(-1):dd MMM yyyy}");
        //         table.Cell().BorderBottom(1).BorderColor("#e0e0e0").Padding(6).Text("Prior Outstanding Balance");
        //         table.Cell().BorderBottom(1).BorderColor("#e0e0e0").Padding(6).AlignRight().Text(FormatCurrency(data.CurrencyCode, data.PreviousOutstandingBalance));

        //         table.Cell().BorderBottom(1).BorderColor("#e0e0e0").Padding(6).Text($"{data.StatementDate:dd MMM yyyy}");
        //         table.Cell().BorderBottom(1).BorderColor("#e0e0e0").Padding(6).Text("Payments Received");
        //         table.Cell().BorderBottom(1).BorderColor("#e0e0e0").Padding(6).AlignRight().Text(FormatCurrency(data.CurrencyCode, -data.PaymentReceived));

        //         table.Cell().BorderBottom(1).BorderColor("#e0e0e0").Padding(6).Text($"{data.InvoiceDate:dd MMM yyyy}");
        //         table.Cell().BorderBottom(1).BorderColor("#e0e0e0").Padding(6).Text($"This Invoice (#{data.InvoiceNumber})");
        //         table.Cell().BorderBottom(1).BorderColor("#e0e0e0").Padding(6).AlignRight().Text(FormatCurrency(data.CurrencyCode, data.CurrentInvoiceTotal));
        //     });

        //     col.Item().PaddingTop(10).AlignRight().Row(r =>
        //     {
        //         r.RelativeItem().AlignRight().Text("Total outstanding balance due:").Bold().FontSize(11).FontColor("#1a1a2e");
        //         r.ConstantItem(150).AlignRight().Text(FormatCurrency(data.CurrencyCode, data.TotalOutstandingDue)).Bold().FontSize(12).FontColor("#1a1a2e");
        //     });
        // }
        // #endregion

        #region Page 2 — Invoice
        private static void ComposePage2(ColumnDescriptor col, StatementInvoicePdfDto data)
        {
            col.Item().Row(r =>
            {
                r.RelativeItem().Column(c =>
                {
                    c.Item().Text("INVOICE").FontSize(16).Bold().FontColor("#1a1a2e");
                    // c.Item().Text(data.CenterName).FontSize(10).FontColor("#555555").Bold();
                });
            });

            col.Item().PaddingTop(10).Row(r =>
            {
                r.RelativeItem().Column(c =>
                {
                    c.Item().Text("BILL TO").FontSize(8).Bold().FontColor("#888888");
                    string companyName = "-";
                    if (!string.IsNullOrWhiteSpace(data.AccountName) &&
                        !string.Equals(data.AccountName.Trim(), data.AttnName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(data.AccountName.Trim(), "Valued Customer", StringComparison.OrdinalIgnoreCase))
                    {
                        companyName = data.AccountName.Trim();
                    }
                    c.Item().Text($"Company Name: {companyName}").Bold().FontSize(10).FontColor("#1a1a2e");
                    if (!string.IsNullOrWhiteSpace(data.AttnName))
                        c.Item().Text($"Attn: {data.AttnName}").FontColor("#444444");
                    c.Item().Text(data.BillingAddress).FontColor("#555555");
                });

                r.ConstantItem(220).Column(c =>
                {
                    c.Item().Row(row => { row.ConstantItem(100).Text("Customer Code:").FontColor("#666666"); row.RelativeItem().Text(data.AccountNumber).Bold(); });
                    c.Item().Row(row => { row.ConstantItem(100).Text("Invoice Number:").FontColor("#666666"); row.RelativeItem().Text(data.InvoiceNumber).Bold(); });
                    c.Item().Row(row => { row.ConstantItem(100).Text("Invoice Date:").FontColor("#666666"); row.RelativeItem().Text($"{data.InvoiceDate:dd MMM yyyy}"); });
                    c.Item().Row(row => { row.ConstantItem(100).Text("Due Date:").FontColor("#666666"); row.RelativeItem().Text($"{data.DueDate:dd MMM yyyy}").Bold().FontColor("#d9534f"); });
                    // c.Item().Row(row => { row.ConstantItem(100).Text("SNTN / NTN / NIC:").FontColor("#666666"); row.RelativeItem().Text(data.SntnNtnNic); });
                });
            });

            col.Item().PaddingTop(15).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.ConstantColumn(85);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(85);
                });

                table.Header(h =>
                {
                    h.Cell().Background("#1a1a2e").Padding(5).Text("Description of Charges").Bold().FontColor("#ffffff");
                    h.Cell().Background("#1a1a2e").Padding(5).AlignRight().Text("Price").Bold().FontColor("#ffffff");
                    h.Cell().Background("#1a1a2e").Padding(5).AlignRight().Text("Tax Amount").Bold().FontColor("#ffffff");
                    h.Cell().Background("#1a1a2e").Padding(5).AlignRight().Text("Total").Bold().FontColor("#ffffff");
                });

                foreach (var item in data.LineItems)
                {
                    table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(5).Text(item.Description);
                    table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(5).AlignRight().Text(FormatAmount(item.PriceExclVat));
                    table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(5).AlignRight().Text(FormatAmount(item.VatAmount));
                    table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(5).AlignRight().Text(FormatAmount(item.TotalInclVat));
                }
            });

            decimal totalExclVat = data.LineItems.Sum(i => i.PriceExclVat);
            decimal depositTotal = data.LineItems.Where(i => IsDepositItem(i)).Sum(i => i.PriceExclVat);
            decimal rentTotal = totalExclVat - depositTotal;
            decimal totalVat = data.LineItems.Sum(i => i.VatAmount);
            decimal grandTotal = data.LineItems.Sum(i => i.TotalInclVat);

            decimal chargePercentage = data.AppliedChargePercentage ?? (data.SupportChargeRate > 0 ? data.SupportChargeRate * 100m : 10m);
            decimal supportCharges = data.SupportChargeAmount ?? (rentTotal > 0 ? Math.Round(rentTotal * (chargePercentage / 100m), 2) : 0m);

            decimal taxPercentage;
            if (data.AppliedTaxPercentage.HasValue && data.AppliedTaxPercentage.Value > 0)
            {
                taxPercentage = data.AppliedTaxPercentage.Value;
            }
            else if (supportCharges > 0 && totalVat > 0)
            {
                taxPercentage = Math.Round((totalVat / supportCharges) * 100m, 0);
            }
            else
            {
                taxPercentage = data.VatRate > 0 ? (data.VatRate * 100m) : 16m;
            }

            col.Item().PaddingTop(10).AlignRight().Column(c =>
            {
                if (depositTotal > 0)
                {
                    c.Item().Row(r => { r.RelativeItem().AlignRight().Text("Total Rent (exc. Tax):"); r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, rentTotal)); });
                    c.Item().Row(r => { r.RelativeItem().AlignRight().Text("Security Deposit (Refundable):"); r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, depositTotal)).FontColor("#d97706"); });
                }
                else
                {
                    c.Item().Row(r => { r.RelativeItem().AlignRight().Text("Total (exc. Tax):"); r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, totalExclVat)); });
                }

                c.Item().Row(r => { r.RelativeItem().AlignRight().Text($"Support Charges ({chargePercentage:G29}%):"); r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, supportCharges)); });
                c.Item().Row(r => { r.RelativeItem().AlignRight().Text($"PST ({taxPercentage:G29}% on Support Charges):"); r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, totalVat)); });
                c.Item().PaddingTop(4).Row(r =>
                {
                    r.RelativeItem().AlignRight().Text($"{data.InvoiceDate:MMMM yyyy} invoice total (inc. Tax):").Bold().FontSize(10).FontColor("#1a1a2e");
                    r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, grandTotal)).Bold().FontSize(11).FontColor("#1a1a2e");
                });
            });

            // col.Item().PaddingTop(15).Text("See next page for an itemized breakdown of charges.").Italic().FontSize(8.5f).FontColor("#666666");
        }

        private static bool IsDepositItem(StatementInvoiceLineItemDto item)
        {
            return item.IsDeposit || item.Description.Contains("Deposit", StringComparison.OrdinalIgnoreCase);
        }
        #endregion

        // #region Page 3 — Your Invoice Details
        // private static void ComposePage3(ColumnDescriptor col, StatementInvoicePdfDto data)
        // {
        //     col.Item().Row(r =>
        //     {
        //         r.RelativeItem().Column(c =>
        //         {
        //             c.Item().Text("YOUR INVOICE DETAILS").FontSize(16).Bold().FontColor("#1a1a2e");
        //             c.Item().Text(data.CenterName).FontSize(10).FontColor("#555555").Bold();
        //         });
        //     });

        //     col.Item().PaddingTop(10).Row(r =>
        //     {
        //         r.RelativeItem().Column(c =>
        //         {
        //             c.Item().Text("ACCOUNT DETAILS").FontSize(8).Bold().FontColor("#888888");
        //             c.Item().Text(data.AccountName).Bold().FontSize(10).FontColor("#1a1a2e");
        //             if (!string.IsNullOrWhiteSpace(data.AttnName))
        //                 c.Item().Text($"Attn: {data.AttnName}").FontColor("#444444");
        //             c.Item().Text(data.BillingAddress).FontColor("#555555");
        //         });

        //         r.ConstantItem(220).Column(c =>
        //         {
        //             c.Item().Row(row => { row.RelativeItem().Text("Account Number:").FontColor("#666666"); row.AutoItem().Text(data.AccountNumber).Bold(); });
        //             c.Item().Row(row => { row.RelativeItem().Text("Invoice Number:").FontColor("#666666"); row.AutoItem().Text(data.InvoiceNumber).Bold(); });
        //             c.Item().Row(row => { row.RelativeItem().Text("Statement Date:").FontColor("#666666"); row.AutoItem().Text($"{data.StatementDate:dd MMM yyyy}"); });
        //             c.Item().Row(row => { row.RelativeItem().Text("Due Date:").FontColor("#666666"); row.AutoItem().Text($"{data.DueDate:dd MMM yyyy}").Bold().FontColor("#d9534f"); });
        //         });
        //     });

        //     col.Item().PaddingTop(15).Text("RECURRING CHARGES").Bold().FontSize(10).FontColor("#1a1a2e");

        //     col.Item().PaddingTop(5).Table(table =>
        //     {
        //         table.ColumnsDefinition(columns =>
        //         {
        //             columns.RelativeColumn(3);
        //             columns.ConstantColumn(70);
        //             columns.ConstantColumn(70);
        //             columns.ConstantColumn(75);
        //             columns.ConstantColumn(65);
        //             columns.ConstantColumn(80);
        //         });

        //         table.Header(h =>
        //         {
        //             h.Cell().Background("#1a1a2e").Padding(5).Text("Item Description").Bold().FontColor("#ffffff");
        //             h.Cell().Background("#1a1a2e").Padding(5).Text("From Date").Bold().FontColor("#ffffff");
        //             h.Cell().Background("#1a1a2e").Padding(5).Text("To Date").Bold().FontColor("#ffffff");
        //             h.Cell().Background("#1a1a2e").Padding(5).AlignRight().Text("Price").Bold().FontColor("#ffffff");
        //             h.Cell().Background("#1a1a2e").Padding(5).AlignRight().Text("Tax Amount").Bold().FontColor("#ffffff");
        //             h.Cell().Background("#1a1a2e").Padding(5).AlignRight().Text("Total (inc. Tax)").Bold().FontColor("#ffffff");
        //         });

        //         foreach (var item in data.LineItems)
        //         {
        //             table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(5).Text(item.Description);
        //             table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(5).Text(item.FromDate.HasValue ? $"{item.FromDate:dd/MM/yyyy}" : "-");
        //             table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(5).Text(item.ToDate.HasValue ? $"{item.ToDate:dd/MM/yyyy}" : "-");
        //             table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(5).AlignRight().Text(FormatAmount(item.PriceExclVat));
        //             table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(5).AlignRight().Text(FormatAmount(item.VatAmount));
        //             table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(5).AlignRight().Text(FormatAmount(item.TotalInclVat));
        //         }
        //     });

        //     decimal totalExclVat = data.LineItems.Sum(i => i.PriceExclVat);
        //     decimal totalVat = data.LineItems.Sum(i => i.VatAmount);
        //     decimal grandTotal = data.LineItems.Sum(i => i.TotalInclVat);

        //     col.Item().PaddingTop(10).AlignRight().Column(c =>
        //     {
        //         c.Item().Row(r => { r.RelativeItem().AlignRight().Text("Subtotal:"); r.ConstantItem(120).AlignRight().Text(FormatCurrency(data.CurrencyCode, totalExclVat)); });
        //         c.Item().Row(r => { r.RelativeItem().AlignRight().Text("Tax:"); r.ConstantItem(120).AlignRight().Text(FormatCurrency(data.CurrencyCode, totalVat)); });
        //         c.Item().PaddingTop(4).Row(r =>
        //         {
        //             r.RelativeItem().AlignRight().Text("Total Charges:").Bold().FontSize(11).FontColor("#1a1a2e");
        //             r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, grandTotal)).Bold().FontSize(12).FontColor("#1a1a2e");
        //         });
        //     });
        // }
        // #endregion

        // #region Page 4 — Methods of Payment + Understanding Your Invoice (Part 1)
        // private static void ComposePage4(ColumnDescriptor col, StatementInvoicePdfDto data)
        // {
        //     col.Item().Text("METHODS OF PAYMENT").FontSize(16).Bold().FontColor("#1a1a2e");
        //
        //     if (!string.IsNullOrWhiteSpace(data.OnlinePaymentUrl))
        //     {
        //         col.Item().PaddingTop(6).Text(t =>
        //         {
        //             t.Span("You can manage and update your online payment preferences anytime at: ");
        //             t.Span(data.OnlinePaymentUrl).Underline().FontColor("#0275d8");
        //         });
        //     }
        //
        //     col.Item().PaddingTop(10).Border(1).BorderColor("#cccccc").Background("#f9f9f9").Padding(10).Column(c =>
        //     {
        //         c.Item().Text("You may pay by Bank Transfer to:").Bold().FontSize(10).FontColor("#1a1a2e");
        //         c.Item().PaddingTop(4).Table(table =>
        //         {
        //             table.ColumnsDefinition(cols =>
        //             {
        //                 cols.RelativeColumn();
        //                 cols.RelativeColumn();
        //             });
        //
        //             table.Cell().Padding(2).Text($"Bank Name: {data.BankName}");
        //             table.Cell().Padding(2).Text($"Branch Code: {data.BranchCode}");
        //             table.Cell().Padding(2).Text($"Account Name: {data.BankAccountName}");
        //             table.Cell().Padding(2).Text($"Account Number: {data.BankAccountNumber}");
        //             table.Cell().Padding(2).Text($"SWIFT / BIC: {data.SwiftBic}");
        //             table.Cell().Padding(2).Text($"IBAN: {data.Iban}");
        //         });
        //         c.Item().PaddingTop(4).Text($"Bank Address: {data.BankAddress}").FontSize(8.5f).FontColor("#555555");
        //     });
        //
        //     col.Item().PaddingTop(8).Text(t =>
        //     {
        //         t.Span("Please provide your Invoice Number ").Bold();
        //         t.Span($"<{data.InvoiceNumber}>").Bold().FontColor("#1a1a2e");
        //         t.Span(" as a payee reference on all payments made.").Bold();
        //     });
        //
        //     col.Item().PaddingTop(20).Text("UNDERSTANDING YOUR INVOICE").FontSize(14).Bold().FontColor("#1a1a2e");
        //     col.Item().Text("INVOICE EXPLANATIONS").Bold().FontSize(9.5f).FontColor("#555555");
        //
        //     var invoiceExplanations = new List<(string Term, string Def)>
        //     {
        //         ("Account adjustments/refunds", "Adjustments, promotional discounts, or refunds credited to your account."),
        //         ("Account balance", "The accumulated outstanding balance across all active billing periods."),
        //         ("Credits", "Prepayments or manual credit notes applied to reduce the gross invoice amount."),
        //         ("Due date", "The calendar date by which full payment must clear in our bank account."),
        //         ("Invoice", "The legal tax document detailing services, office space, and applicable taxes."),
        //         ("Late payment fees", "Standard penalty fees applied when balance is unpaid past the due date."),
        //         ("One-off charges incurred", "Single-occurrence usage charges (e.g. print, meeting room top-ups)."),
        //         ("Payments received", "Total funds received and processed since the previous statement date."),
        //         ("Recurring charges", "Fixed contract agreement charges billed regularly on a monthly cycle."),
        //         ("Total payment due", "Net calculated total required to settle the current statement in full.")
        //     };
        //
        //     col.Item().PaddingTop(5).Table(table =>
        //     {
        //         table.ColumnsDefinition(columns =>
        //         {
        //             columns.ConstantColumn(160);
        //             columns.RelativeColumn();
        //         });
        //
        //         table.Header(h =>
        //         {
        //             h.Cell().Background("#1a1a2e").Padding(4).Text("Term").Bold().FontColor("#ffffff");
        //             h.Cell().Background("#1a1a2e").Padding(4).Text("Definition").Bold().FontColor("#ffffff");
        //         });
        //
        //         foreach (var exp in invoiceExplanations)
        //         {
        //             table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(4).Text(exp.Term).Bold().FontSize(8.5f);
        //             table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(4).Text(exp.Def).FontSize(8.5f).FontColor("#444444");
        //         }
        //     });
        //
        //     col.Item().PaddingTop(10).Text("RECURRING CHARGES GLOSSARY (PART 1)").Bold().FontSize(9.5f).FontColor("#555555");
        //     col.Item().PaddingTop(3).Table(table =>
        //     {
        //         table.ColumnsDefinition(columns =>
        //         {
        //             columns.ConstantColumn(160);
        //             columns.RelativeColumn();
        //         });
        //
        //         table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(4).Text("IT Services").Bold().FontSize(8.5f);
        //         table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(4).Text("High-speed enterprise internet, dedicated VLAN, Wi-Fi access, and digital infrastructure support.").FontSize(8.5f).FontColor("#444444");
        //     });
        // }
        // #endregion
        //
        // #region Page 5 — Understanding Your Invoice (Part 2)
        // private static void ComposePage5(ColumnDescriptor col, StatementInvoicePdfDto data)
        // {
        //     col.Item().Text("UNDERSTANDING YOUR INVOICE (CONTINUED)").FontSize(14).Bold().FontColor("#1a1a2e");
        //     col.Item().Text("RECURRING CHARGES GLOSSARY (PART 2)").Bold().FontSize(9.5f).FontColor("#555555");
        //
        //     var recurringGlossary = new List<(string Term, string Def)>
        //     {
        //         ("Kitchen Amenities", "Access to shared kitchen facilities, premium tea/coffee provisions, filtered water, and daily maintenance."),
        //         ("Office / Workstation", "Monthly license fee for primary dedicated private office room or assigned workstation desks."),
        //         ("Business Lounge Access", "Access to global and local coworking lounges during standard operational hours."),
        //         ("Meeting Room Allowance", "Monthly inclusive credit allowance for booking state-of-the-art conference rooms."),
        //         ("Mail & Package Handling", "Professional receipt, logging, notification, and secure storage of incoming mail/parcels."),
        //         ("Reception & Front Desk", "Professional greeting for guest visitors, administrative assistance, and phone answering services.")
        //     };
        //
        //     col.Item().PaddingTop(8).Table(table =>
        //     {
        //         table.ColumnsDefinition(columns =>
        //         {
        //             columns.ConstantColumn(160);
        //             columns.RelativeColumn();
        //         });
        //
        //         table.Header(h =>
        //         {
        //             h.Cell().Background("#1a1a2e").Padding(5).Text("Charge Category").Bold().FontColor("#ffffff");
        //             h.Cell().Background("#1a1a2e").Padding(5).Text("Description & Inclusions").Bold().FontColor("#ffffff");
        //         });
        //
        //         foreach (var item in recurringGlossary)
        //         {
        //             table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(5).Text(item.Term).Bold().FontSize(8.5f);
        //             table.Cell().BorderBottom(1).BorderColor("#f0f0f0").Padding(5).Text(item.Def).FontSize(8.5f).FontColor("#444444");
        //         }
        //     });
        //
        //     col.Item().PaddingTop(25).Border(1).BorderColor("#d0d0d0").Background("#fafafa").Padding(12).Column(c =>
        //     {
        //         c.Item().Text("CUSTOMER NOTICE & COMPLIANCE").Bold().FontSize(9.5f).FontColor("#1a1a2e");
        //         c.Item().PaddingTop(4).Text("Please note that failure to settle total outstanding balances by the specified due date may result in suspension of facility access services, meeting room booking privileges, and standard late payment finance interest charges.").FontSize(8.5f).FontColor("#555555");
        //         c.Item().PaddingTop(4).Text("For invoice inquiries or billing disputes, please contact billing@worknestpk.com within 7 business days of statement date.").FontSize(8.5f).FontColor("#555555");
        //     });
        // }
        // #endregion

        #region Common Header
        private static string GetLogoFilePath(string? configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
                return configuredPath;

            var searchPaths = new[]
            {
                @"F:\WorkNest_FE\public\images\Logo.png",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "public", "images", "Logo.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "images", "Logo.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "WorkNest_FE", "public", "images", "Logo.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "public", "images", "Logo.png")
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path)) return path;
            }

            return string.Empty;
        }

        private static void ComposeHeader(IContainer container, StatementInvoicePdfDto data)
        {
            string logoPath = GetLogoFilePath(data.VendorLogoPath);

            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Column(c =>
                    {
                        if (!string.IsNullOrWhiteSpace(logoPath))
                        {
                            c.Item().Row(r =>
                            {
                                r.AutoItem().Height(35).Image(logoPath).FitHeight();
                                r.AutoItem().AlignMiddle().PaddingLeft(2).Text("orkNest").FontSize(22).Bold().FontColor("#1a1a2e");
                            });
                        }
                        else
                        {
                            c.Item().Text(data.VendorLegalName ?? "WorkNest").FontSize(20).Bold().FontColor("#1a1a2e");
                        }
                    });

                    row.ConstantItem(260).Column(c =>
                    {
                        c.Item().AlignRight().Text(data.VendorLegalName).Bold().FontSize(9).FontColor("#1a1a2e");
                        c.Item().AlignRight().Text(data.VendorAddress).FontSize(8).FontColor("#555555");
                        c.Item().AlignRight().Text($"Phone: {data.VendorPhone}").FontSize(8).FontColor("#555555");
                        if (!string.IsNullOrWhiteSpace(data.VendorNtn))
                        {
                            c.Item().AlignRight().Text($"NTN: {data.VendorNtn}").FontSize(8).FontColor("#555555");
                        }
                    });
                });

                col.Item().PaddingTop(6).PaddingBottom(10).LineHorizontal(1.5f).LineColor("#1a1a2e");
            });
        }
        #endregion

        #region Common Footer
        private static void ComposeFooter(IContainer container, StatementInvoicePdfDto data)
        {
            container.Column(c =>
            {
                c.Item().LineHorizontal(1).LineColor("#cccccc");
                c.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text($"{data.VendorLegalName} | NTN: {data.VendorNtn} | Phone: {data.VendorPhone}").FontSize(7.5f).FontColor("#666666");
                        col.Item().Text(data.VendorAddress).FontSize(7.5f).FontColor("#888888");
                    });

                    row.ConstantItem(100).AlignRight().Text(x =>
                    {
                        x.Span("Page ").FontSize(8).FontColor("#666666");
                        x.CurrentPageNumber().FontSize(8).FontColor("#666666");
                        x.Span(" of ").FontSize(8).FontColor("#666666");
                        x.TotalPages().FontSize(8).FontColor("#666666");
                    });
                });
            });
        }
        #endregion

        #region Helpers & Default Data
        private static string FormatCurrency(string currencyCode, decimal amount)
        {
            return $"{currencyCode} {amount:#,##0.00}";
        }

        private static string FormatAmount(decimal amount)
        {
            return $"{amount:#,##0.00}";
        }

        public static StatementInvoicePdfDto GetSampleData()
        {
            var dto = new StatementInvoicePdfDto();
            dto.LineItems = GetDefaultLineItems(dto.VatRate);
            return dto;
        }

        private static List<StatementInvoiceLineItemDto> GetDefaultLineItems(decimal vatRate)
        {
            var startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            decimal officeBase = 200000.00m;
            decimal itBase = 30000.00m;
            decimal kitchenBase = 20000.00m;
            decimal supportRate = 0.10m;

            return new List<StatementInvoiceLineItemDto>
            {
                new StatementInvoiceLineItemDto
                {
                    Description = "Private Office 401 License Fee",
                    FromDate = startDate,
                    ToDate = endDate,
                    PriceExclVat = officeBase,
                    VatAmount = Math.Round(officeBase * supportRate * vatRate, 2),
                    Category = "Recurring"
                },
                new StatementInvoiceLineItemDto
                {
                    Description = "Dedicated High-Speed IT Services (100Mbps)",
                    FromDate = startDate,
                    ToDate = endDate,
                    PriceExclVat = itBase,
                    VatAmount = Math.Round(itBase * supportRate * vatRate, 2),
                    Category = "IT Services"
                },
                new StatementInvoiceLineItemDto
                {
                    Description = "Kitchen & Beverage Amenities Package",
                    FromDate = startDate,
                    ToDate = endDate,
                    PriceExclVat = kitchenBase,
                    VatAmount = Math.Round(kitchenBase * supportRate * vatRate, 2),
                    Category = "Recurring"
                }
            };
        }
        #endregion
    }
}
