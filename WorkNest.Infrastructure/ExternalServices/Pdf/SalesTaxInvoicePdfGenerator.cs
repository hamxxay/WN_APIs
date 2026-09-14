using System;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkNest.Application.DTOs.Payment;

namespace WorkNest.Infrastructure.ExternalServices.Pdf
{
    public static class SalesTaxInvoicePdfGenerator
    {
        public static byte[] GeneratePdf(CustomerSTInvoiceDto data)
        {
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
                        ComposeBody(col, data);
                    });
                });
            }).GeneratePdf();
        }

        private static void ComposeBody(ColumnDescriptor col, CustomerSTInvoiceDto data)
        {
            // Title Block
            col.Item().Row(r =>
            {
                r.RelativeItem().Column(c =>
                {
                    c.Item().Text("SALES TAX INVOICE").FontSize(16).Bold().FontColor("#000000");
                });
            });

            // Tariff Information Banner
            col.Item().PaddingTop(6).Border(1).BorderColor("#000000").Background("#f5f5f5").Padding(6).Row(r =>
            {
                r.RelativeItem().Text(tx =>
                {
                    tx.Span("HS  Description:").Bold().FontColor("#000000");
                    tx.Span(data.TariffLabel ?? "Business Support Services").FontColor("#000000");
                    tx.Span("  |  HS Code: ").Bold().FontColor("#000000");
                    tx.Span(data.TariffHeading ?? "9805.9200").FontColor("#000000");
                });
            });
    
            // Meta & Customer Details
            col.Item().PaddingTop(10).Row(r =>
            {
                r.RelativeItem().Column(c =>
                {
                    string customerName = !string.IsNullOrWhiteSpace(data.CustomerName) ? data.CustomerName : "Valued Customer";
                    c.Item().Text($"Customer Name: {customerName}").FontSize(10).FontColor("#000000");
                    if (!string.IsNullOrWhiteSpace(data.CustomerAddress))
                        c.Item().Text($"Address: {data.CustomerAddress}").FontSize(10).FontColor("#000000");
                    if (!string.IsNullOrWhiteSpace(data.SntnNtnNic))
                        c.Item().Text($"NTN / CNIC: {data.SntnNtnNic}").FontColor("#000000");
                });

                r.ConstantItem(240).Column(c =>
                {
                    c.Item().Row(row => { row.ConstantItem(120).Text("ST Invoice Number:").FontColor("#000000"); row.RelativeItem().Text(data.STInvoiceNumber).Bold().FontColor("#000000"); });
                    c.Item().Row(row => { row.ConstantItem(120).Text("Parent Invoice No:").FontColor("#000000"); row.RelativeItem().Text(data.ParentInvoiceNumber).FontColor("#000000"); });
                    c.Item().Row(row => { row.ConstantItem(120).Text("Customer Code:").FontColor("#000000"); row.RelativeItem().Text(data.CustomerCode).FontColor("#000000"); });
                    c.Item().Row(row => { row.ConstantItem(120).Text("Invoice Date:").FontColor("#000000"); row.RelativeItem().Text($"{data.IssuedOn:dd MMM yyyy}").FontColor("#000000"); });
                    c.Item().Row(row => { row.ConstantItem(120).Text("Due Date:").FontColor("#000000"); row.RelativeItem().Text($"{data.DueOn:dd MMM yyyy}").FontColor("#000000"); });
                });
            });

            // Breakdown Table
            col.Item().PaddingTop(15).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.ConstantColumn(90);
                    columns.ConstantColumn(70);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(90);
                });

                table.Header(h =>
                {
                    h.Cell().Background("#000000").Padding(5).Text("Description").Bold().FontColor("#ffffff");
                    h.Cell().Background("#000000").Padding(5).AlignRight().Text("Exclusive Amt").Bold().FontColor("#ffffff");
                    h.Cell().Background("#000000").Padding(5).AlignRight().Text("Tax Rate").Bold().FontColor("#ffffff");
                    h.Cell().Background("#000000").Padding(5).AlignRight().Text("Tax Amount").Bold().FontColor("#ffffff");
                    h.Cell().Background("#000000").Padding(5).AlignRight().Text("Line Total").Bold().FontColor("#ffffff");
                });

                foreach (var item in data.LineItems)
                {
                    table.Cell().BorderBottom(1).BorderColor("#cccccc").Padding(5).Text(item.Description).FontColor("#000000");
                    table.Cell().BorderBottom(1).BorderColor("#cccccc").Padding(5).AlignRight().Text(FormatAmount(item.ExclusiveAmount)).FontColor("#000000");
                    table.Cell().BorderBottom(1).BorderColor("#cccccc").Padding(5).AlignRight().Text($"{item.TaxPercentage:0.##}%").FontColor("#000000");
                    table.Cell().BorderBottom(1).BorderColor("#cccccc").Padding(5).AlignRight().Text(FormatAmount(item.TaxAmount)).FontColor("#000000");
                    table.Cell().BorderBottom(1).BorderColor("#cccccc").Padding(5).AlignRight().Text(FormatAmount(item.LineTotal)).FontColor("#000000");
                }
            });

            // Totals Summary Footer
            col.Item().PaddingTop(10).AlignRight().Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.RelativeItem().AlignRight().Text("SubTotal (exc. Tax):").FontColor("#000000");
                    r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, data.SubTotal)).FontColor("#000000");
                });
                c.Item().Row(r =>
                {
                    r.RelativeItem().AlignRight().Text("Sales Tax Total:").FontColor("#000000");
                    r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, data.TaxTotal)).FontColor("#000000");
                });
                c.Item().PaddingTop(4).Row(r =>
                {
                    r.RelativeItem().AlignRight().Text("Grand Total (inc. Tax):").Bold().FontSize(10).FontColor("#000000");
                    r.ConstantItem(140).AlignRight().Text(FormatCurrency(data.CurrencyCode, data.GrandTotal)).Bold().FontSize(11).FontColor("#000000");
                });
            });

            // Terms & Conditions Block
            decimal depositTotal = data.SecurityDepositAmount ?? 0;
            col.Item().PaddingTop(12).Border(1).BorderColor("#cccccc").Background("#fdfdfd").Padding(8).Column(tc =>
            {
                tc.Item().Text("Terms & Conditions").Bold().FontSize(9).FontColor("#000000");
                tc.Spacing(2);
                int itemNum = 1;
                tc.Item().Text($"{itemNum++}. Please deposit this payment into the following bank account: ").FontSize(7.5f).FontColor("#000000");

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

                tc.Item().Text($"And send the receipt to +923201809696").FontSize(7.5f).FontColor("#000000");
                tc.Item().Text($"{itemNum++}. Your access will be closed if dues are not paid within 5 days of the due date.").FontSize(7.5f).FontColor("#000000");
                tc.Item().Text($"{itemNum++}. Payment is due on or before the due date specified on this invoice.").FontSize(7.5f).FontColor("#000000");
                if (depositTotal > 0 || data.SecurityDepositAmount > 0)
                {
                    tc.Item().Text($"{itemNum++}. Security deposit is fully refundable upon termination of the agreement, subject to lease terms.").FontSize(7.5f).FontColor("#000000");
                }
                tc.Item().Text($"{itemNum++}. Booking confirmation is subject to space availability at the time of payment.").FontSize(7.5f).FontColor("#000000");
                tc.Item().Text($"{itemNum++}. WorkNest reserves the right to modify pricing and terms with prior notice.").FontSize(7.5f).FontColor("#000000");
            });
        }

        #region Common Header & Footer (Shared Visual Design)
        private static string GetLogoFilePath(string? configuredPath = null)
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

        private static void ComposeHeader(IContainer container, CustomerSTInvoiceDto data)
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

        private static void ComposeFooter(IContainer container, CustomerSTInvoiceDto data)
        {
            container.Column(c =>
            {
                c.Item().LineHorizontal(1).LineColor("#000000");
                c.Item().PaddingTop(4).Row(row =>
                {
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

        private static string FormatCurrency(string currencyCode, decimal amount)
        {
            return $"{currencyCode} {amount:#,##0.00}";
        }

        private static string FormatAmount(decimal amount)
        {
            return $"{amount:#,##0.00}";
        }
        #endregion
    }
}
