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

        public decimal SubTotal { get; set; }
        public decimal DiscountTotal { get; set; }
        public decimal TaxTotal { get; set; }

        public decimal VatRate { get; set; } = 0.16m;
        public decimal SupportChargeRate { get; set; } = 0.10m;
        public decimal? SupportChargeAmount { get; set; }
        public decimal? AppliedChargePercentage { get; set; }
        public decimal? AppliedTaxPercentage { get; set; }
        public decimal SecurityDepositAmount { get; set; }
        public decimal WithholdingTaxRate { get; set; } = 15.00m;
        public string CurrencyCode { get; set; } = "PKR";

        public string? SupportChargesInvoiceUrl { get; set; }

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
        public string VendorPhone { get; set; } = "+92 328 0256000 / +92 320 1809696";
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
        public decimal Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }
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
                    c.Item().Text("INVOICE").FontSize(16).Bold().FontColor("#000000");
                    // c.Item().Text(data.CenterName).FontSize(10).FontColor("#000000").Bold();
                });
            });

            col.Item().PaddingTop(10).Row(r =>
            {
                r.RelativeItem().Column(c =>
                {
                    // c.Item().Text("BILL TO").FontSize(8).Bold().FontColor("#000000");
                    string companyName = "-";
                    if (!string.IsNullOrWhiteSpace(data.AccountName) &&
                        !string.Equals(data.AccountName.Trim(), data.AttnName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(data.AccountName.Trim(), "Valued Customer", StringComparison.OrdinalIgnoreCase))
                    {
                        companyName = data.AccountName.Trim();
                    }
                    c.Item().Text($"Customer Name: {companyName}").FontSize(10).FontColor("#000000");
                    // if (!string.IsNullOrWhiteSpace(data.AttnName))
                    //     c.Item().Text($"Attn: {data.AttnName}").FontSize(10).FontColor("#000000");
                    c.Item().Text($"Address: {data.BillingAddress}").FontSize(10).FontColor("#000000");
                    c.Item().Text($"NTN / CNIC: {data.SntnNtnNic}").FontColor("#000000");

                });

                r.ConstantItem(240).Column(c =>
                {
                    c.Item().Row(row => { row.ConstantItem(110).Text("Customer Code:").FontColor("#000000"); row.RelativeItem().Text(data.AccountNumber).FontColor("#000000"); });
                    c.Item().Row(row => { row.ConstantItem(110).Text("Invoice Number:").FontColor("#000000"); row.RelativeItem().Text(data.InvoiceNumber).FontColor("#000000"); });
                    c.Item().Row(row => { row.ConstantItem(110).Text("Invoice Date:").FontColor("#000000"); row.RelativeItem().Text($"{data.InvoiceDate:dd MMM yyyy}").FontColor("#000000"); });
                    c.Item().Row(row => { row.ConstantItem(110).Text("Due Date:").FontColor("#000000"); row.RelativeItem().Text($"{data.DueDate:dd MMM yyyy}").FontColor("#000000"); });
                });
            });

            col.Item().PaddingTop(15).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.ConstantColumn(100);
                    columns.ConstantColumn(100);
                });

                table.Header(h =>
                {
                    h.Cell().Background("#000000").Padding(5).Text("Description").Bold().FontColor("#ffffff");
                    h.Cell().Background("#000000").Padding(5).AlignRight().Text("Unit Price").Bold().FontColor("#ffffff");
                    h.Cell().Background("#000000").Padding(5).AlignRight().Text("Total").Bold().FontColor("#ffffff");
                });

                foreach (var item in data.LineItems)
                {
                    decimal displayQty = item.Quantity > 0 ? item.Quantity : 1;
                    decimal displayUnitPrice = item.UnitPrice > 0 ? item.UnitPrice : (displayQty > 0 ? item.PriceExclVat / displayQty : item.PriceExclVat);

                    table.Cell().BorderBottom(1).BorderColor("#cccccc").Padding(5).Text(item.Description).FontColor("#000000");
                    table.Cell().BorderBottom(1).BorderColor("#cccccc").Padding(5).AlignRight().Text(FormatAmount(displayUnitPrice)).FontColor("#000000");
                    table.Cell().BorderBottom(1).BorderColor("#cccccc").Padding(5).AlignRight().Text(FormatAmount(item.PriceExclVat)).FontColor("#000000");
                }
            });

            decimal totalExclVat = data.LineItems.Sum(i => i.PriceExclVat);
            decimal depositTotal = data.LineItems.Where(i => IsDepositItem(i)).Sum(i => i.PriceExclVat);
            decimal rentTotal = totalExclVat - depositTotal;
            decimal totalLineVat = data.LineItems.Sum(i => i.VatAmount);
            decimal effectiveTaxTotal = data.TaxTotal > 0 ? data.TaxTotal : totalLineVat;

            decimal taxPercentage = data.AppliedTaxPercentage.HasValue && data.AppliedTaxPercentage.Value > 0
                ? data.AppliedTaxPercentage.Value
                : (data.VatRate > 0 ? (data.VatRate * 100m) : 16m);

            bool isExplicitlyNoTax = (data.AppliedTaxPercentage.HasValue && data.AppliedTaxPercentage.Value == 0m)
                                  && (data.AppliedChargePercentage.HasValue && data.AppliedChargePercentage.Value == 0m);

            bool hasTax = !isExplicitlyNoTax && (
                effectiveTaxTotal > 0
                || (data.AppliedTaxPercentage.HasValue && data.AppliedTaxPercentage.Value > 0)
                || (data.SupportChargeAmount.HasValue && data.SupportChargeAmount.Value > 0)
            );

            if (hasTax)
            {
                decimal supportCharges = 0m;
                decimal totalVat = 0m;

                if (data.SupportChargeAmount.HasValue && data.SupportChargeAmount.Value > 0)
                {
                    supportCharges = data.SupportChargeAmount.Value;
                    totalVat = effectiveTaxTotal > 0 ? effectiveTaxTotal : Math.Round(supportCharges * (taxPercentage / 100m), 2);
                }
                else if (effectiveTaxTotal > 0 && taxPercentage > 0)
                {
                    totalVat = effectiveTaxTotal;
                    supportCharges = Math.Round(totalVat / (taxPercentage / 100m), 2);
                }
                else if (rentTotal > 0 && (data.AppliedChargePercentage ?? 10m) > 0)
                {
                    supportCharges = Math.Round(rentTotal * ((data.AppliedChargePercentage ?? 10m) / 100m), 2);
                    totalVat = Math.Round(supportCharges * (taxPercentage / 100m), 2);
                }

                decimal grandTotal = data.CurrentInvoiceTotal > 0
                    ? data.CurrentInvoiceTotal
                    : (totalExclVat + totalVat);

                col.Item().PaddingTop(10).AlignRight().Column(c =>
                {
                    if (depositTotal > 0)
                    {
                        c.Item().Row(r => { r.RelativeItem().AlignRight().Text("Total Rent (exc. Tax):").FontColor("#000000"); r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, rentTotal)).FontColor("#000000"); });
                        c.Item().Row(r => { r.RelativeItem().AlignRight().Text("Security Deposit (Refundable):").FontColor("#000000"); r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, depositTotal)).FontColor("#000000"); });
                    }
                    else
                    { 
                        c.Item().Row(r => { r.RelativeItem().AlignRight().Text("Total (exc. Tax):").FontColor("#000000"); r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, totalExclVat)).FontColor("#000000"); });
                    }

                    c.Item().Row(r => { r.RelativeItem().AlignRight().Text("Support Services Portion (incl. in rent):").FontColor("#000000"); r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, supportCharges)).FontColor("#000000"); });
                    c.Item().Row(r => { r.RelativeItem().AlignRight().Text($"PST ({taxPercentage:G29}% on Support Services):").FontColor("#000000"); r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, totalVat)).FontColor("#000000"); });
                    c.Item().PaddingTop(4).Row(r =>
                    {
                        r.RelativeItem().AlignRight().Text($"{data.InvoiceDate:MMMM yyyy} invoice total (inc. Tax):").Bold().FontSize(10).FontColor("#000000");
                        r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, grandTotal)).Bold().FontSize(11).FontColor("#000000");
                    });

                    if (!string.IsNullOrWhiteSpace(data.SupportChargesInvoiceUrl))
                    {
                        c.Item().PaddingTop(6).AlignRight().Text(tx =>
                        {
                            tx.Hyperlink("View Sales Tax Invoice for Support Services", data.SupportChargesInvoiceUrl)
                              .Bold().FontSize(9).FontColor("#1d4ed8").Underline();
                        });
                    }
                });
            }
            else
            {
                decimal grandTotal = data.CurrentInvoiceTotal > 0
                    ? data.CurrentInvoiceTotal
                    : totalExclVat;

                col.Item().PaddingTop(10).AlignRight().Column(c =>
                {
                    if (depositTotal > 0)
                    {
                        c.Item().Row(r => { r.RelativeItem().AlignRight().Text("Total Rent:").FontColor("#000000"); r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, rentTotal)).FontColor("#000000"); });
                        c.Item().Row(r => { r.RelativeItem().AlignRight().Text("Security Deposit (Refundable):").FontColor("#000000"); r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, depositTotal)).FontColor("#000000"); });
                    }
                    else
                    {
                        c.Item().Row(r => { r.RelativeItem().AlignRight().Text("Sub Total:").FontColor("#000000"); r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, totalExclVat)).FontColor("#000000"); });
                    }

                    c.Item().PaddingTop(4).Row(r =>
                    {
                        r.RelativeItem().AlignRight().Text($"{data.InvoiceDate:MMMM yyyy} invoice total:").Bold().FontSize(10).FontColor("#000000");
                        r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, grandTotal)).Bold().FontSize(11).FontColor("#000000");
                    });
                });
            }

            col.Item().PaddingTop(12).Border(1).BorderColor("#cccccc").Background("#fdfdfd").Padding(8).Column(tc =>
            {
                tc.Item().Text("Terms & Conditions").Bold().FontSize(9).FontColor("#000000");
                tc.Spacing(2);
                int itemNum = 1;
                tc.Item().Text($"{itemNum++}. Please deposit this payment in the following bank ").FontSize(7.5f).FontColor("#000000");

                bool isF7 = data.CenterName != null && (data.CenterName.Contains("F-7", StringComparison.OrdinalIgnoreCase) || data.CenterName.Contains("F7", StringComparison.OrdinalIgnoreCase));

                if (isF7)
                {
                    tc.Item().Text($"Account Title: Work Nest Co-Working").FontSize(7.5f).FontColor("#000000");
                    tc.Item().Text($"Account: 6-2-10-20389-714-250794").FontSize(7.5f).FontColor("#000000");
                    tc.Item().Text($"IBAN: PK26MPBL0210587140250794").FontSize(7.5f).FontColor("#000000");
                }
                else
                {
                    tc.Item().Text($"Bank Name: Bank Of Punjab").FontSize(7.5f).FontColor("#000000");
                    tc.Item().Text($"Account Title: WORKNEST PRIVATE LIMITED").FontSize(7.5f).FontColor("#000000");
                    tc.Item().Text($"Account Number: 5310449521900010").FontSize(7.5f).FontColor("#000000");
                    tc.Item().Text($"IBAN: PK24BPUN5310449521900010 ").FontSize(7.5f).FontColor("#000000");
                    tc.Item().Text($"Swift code: BPUNPKKA ").FontSize(7.5f).FontColor("#000000");
                    tc.Item().Text($"Branch code: 0839 ").FontSize(7.5f).FontColor("#000000");
                }

                tc.Item().Text($"And send the receipt on +923201809696").FontSize(7.5f).FontColor("#000000");
                tc.Item().Text($"{itemNum++}. Your access will be suspended if dues are not paid by the due date ").FontSize(7.5f).FontColor("#000000");


                tc.Item().Text($"{itemNum++}. WorkNest will charge Provincial Sales Tax (PST) on support services.").FontSize(7.5f).FontColor("#000000");
                tc.Item().Text($"{itemNum++}. Payment is due on or before the due date specified on this invoice.").FontSize(7.5f).FontColor("#000000");
                tc.Item().Text($"{itemNum++}. WorkNest reserves the right to modify pricing and terms with prior notice.").FontSize(7.5f).FontColor("#000000");

                decimal invoiceTotal = data.CurrentInvoiceTotal > 0
                    ? data.CurrentInvoiceTotal
                    : (hasTax ? (totalExclVat + (effectiveTaxTotal > 0 ? effectiveTaxTotal : Math.Round((data.SupportChargeAmount ?? 0m) * (taxPercentage / 100m), 2))) : totalExclVat);
                decimal secDeposit = depositTotal > 0 ? depositTotal : data.SecurityDepositAmount;
                decimal taxableBase = Math.Max(0, invoiceTotal - secDeposit);
                decimal rawWht = data.WithholdingTaxRate > 0 ? data.WithholdingTaxRate : 15.00m;
                decimal withholdingTaxRate = rawWht > 1m ? (rawWht / 100.0m) : rawWht;
                decimal grossedUpRent = withholdingTaxRate < 1m ? taxableBase / (1 - withholdingTaxRate) : taxableBase;
                decimal grossedUpTotal = grossedUpRent + secDeposit;

                tc.Item().Text($"{itemNum++}. If tax is withheld, the customer shall pay PKR {grossedUpTotal:N0}").FontSize(7.5f).Bold().FontColor("#000000");
                if (secDeposit > 0)
                {
                    tc.Item().Text($"{itemNum++}. Withholding tax is not applicable on the Security Deposit.").FontSize(7.5f).Bold().FontColor("#000000");
                }
            });
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
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath) && !configuredPath.EndsWith("Logo.png", StringComparison.OrdinalIgnoreCase))
                return configuredPath;

            var searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "images", "Logo_black.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "Logo_black.png"),
                @"F:\WN_APIs\WorkNest.API\wwwroot\images\Logo_black.png",
                @"F:\WorkNest_FE\public\images\Logo_black.png",
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
                                r.AutoItem().AlignMiddle().PaddingLeft(2).Text("orkNest").FontSize(22).Bold().FontColor("#000000");
                            });
                        }
                        else
                        {
                            c.Item().Text(data.VendorLegalName ?? "WorkNest").FontSize(20).Bold().FontColor("#000000");
                        }
                    });

                    row.ConstantItem(260).Column(c =>
                    {
                        c.Item().AlignRight().Text(data.VendorLegalName).Bold().FontSize(9).FontColor("#000000");
                        c.Item().AlignRight().Text(data.VendorAddress).FontSize(8).FontColor("#000000");
                        c.Item().AlignRight().Text($"Phone: {data.VendorPhone}").FontSize(8).FontColor("#000000");
                        if (!string.IsNullOrWhiteSpace(data.VendorNtn))
                        {
                            c.Item().AlignRight().Text($"NTN: {data.VendorNtn}").FontSize(8).FontColor("#000000");
                        }
                    });
                });

                col.Item().PaddingTop(6).PaddingBottom(10).LineHorizontal(1.5f).LineColor("#000000");
            });
        }
        #endregion

        #region Common Footer
        private static void ComposeFooter(IContainer container, StatementInvoicePdfDto data)
        {
            container.Column(c =>
            {
                c.Item().LineHorizontal(1).LineColor("#000000");
                c.Item().PaddingTop(4).Row(row =>
                {
                    // row.RelativeItem().Column(col =>
                    // {
                    //     col.Item().Text($"{data.VendorLegalName} | NTN: {data.VendorNtn} | Phone: {data.VendorPhone}").FontSize(7.5f).FontColor("#000000");
                    //     col.Item().Text(data.VendorAddress).FontSize(7.5f).FontColor("#000000");
                    // });

                    row.ConstantItem(100).AlignRight().Text(x =>
                    {
                        x.Span("Page ").FontSize(8).FontColor("#000000");
                        x.CurrentPageNumber().FontSize(8).FontColor("#000000");
                        x.Span(" of ").FontSize(8).FontColor("#000000");
                        x.TotalPages().FontSize(8).FontColor("#000000");
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
