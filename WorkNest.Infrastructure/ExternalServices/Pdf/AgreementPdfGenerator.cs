using System;
using System.Collections.Generic;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkNest.Application.DTOs.Agreement;

namespace WorkNest.Infrastructure.ExternalServices.Pdf
{
    public static class AgreementPdfGenerator
    {
        static AgreementPdfGenerator()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        private static string GetLogoFilePath()
        {
            var searchPaths = new List<string>
            {
                @"F:\WorkNest_FE\public\images\Logo_black.png",
                @"F:\WN_APIs\WorkNest.API\wwwroot\images\Logo_black.png",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "images", "Logo_black.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "Logo_black.png"),
                @"F:\WorkNest_FE\public\images\Logo.png",
                @"F:\WorkNest_FE\src\assets\Logo.png",
                @"F:\WorkNest_FE\public\logo.png",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "Logo.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "Logo.png")
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path)) return path;
            }

            return string.Empty;
        }

        public static string SanitizeCustomerName(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "The Customer";
            var tokens = rawName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Count >= 2)
            {
                if (string.Equals(tokens[tokens.Count - 1], tokens[tokens.Count - 2], StringComparison.OrdinalIgnoreCase))
                {
                    tokens.RemoveAt(tokens.Count - 1);
                }
            }
            return string.Join(" ", tokens);
        }

        /// <summary>
        /// Generates Page 1 (Dynamic Terms, Customer/Entity Details, Financial Schedule, Signatures)
        /// using QuestPDF Span-based composition.
        /// </summary>
        public static byte[] Generate(SendAgreementRequest req, string quotationNumber)
        {
            string logoPath = GetLogoFilePath();

            string startDateStr = req.ContractStartDate.HasValue ? req.ContractStartDate.Value.ToString("dd MMMM yyyy") : DateTime.Now.ToString("dd MMMM yyyy");
            string endDateStr = req.ContractEndDate.HasValue ? req.ContractEndDate.Value.ToString("dd MMMM yyyy") : DateTime.Now.AddYears(1).ToString("dd MMMM yyyy");
            string agreementDateStr = DateTime.Now.ToString("dd MMMM yyyy");
            string billingPeriodStr = !string.IsNullOrWhiteSpace(req.BillingFrequency) ? req.BillingFrequency : "Monthly";
            string opHoursStr = !string.IsNullOrWhiteSpace(req.OperatingHours) ? req.OperatingHours : "24/7 Access (Monday to Sunday)";
            int refundDays = req.RefundDays > 0 ? req.RefundDays : 30;

            string customerDisplayName = SanitizeCustomerName(req.FullName);
            string idOrRegNumber = !string.IsNullOrWhiteSpace(req.Cnic) ? req.Cnic : (!string.IsNullOrWhiteSpace(req.SecpRegistrationNo) ? req.SecpRegistrationNo : (!string.IsNullOrWhiteSpace(req.Ntn) ? req.Ntn : "N/A"));
            string address = !string.IsNullOrWhiteSpace(req.Address) ? req.Address : "Islamabad, Pakistan";
            string companyName = !string.IsNullOrWhiteSpace(req.CompanyName) ? req.CompanyName : customerDisplayName;

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(32);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial").FontColor("#1e293b").LineHeight(1.3f));

                    // Header
                    page.Header().Column(col =>
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
                                        r.AutoItem().AlignMiddle().PaddingLeft(2).Text("orkNest").FontSize(22).Bold().FontColor("#0f172a");
                                    });
                                }
                                else
                                {
                                    c.Item().Text("WorkNest (Pvt) Ltd").FontSize(18).Bold().FontColor("#0f172a");
                                }
                            });

                            row.ConstantItem(260).Column(c =>
                            {
                                c.Item().AlignRight().Text("WorkNest (Pvt) Ltd").Bold().FontSize(9).FontColor("#0f172a");
                                c.Item().AlignRight().Text("3rd Floor EOBI Building-II, I-8 Markaz, Islamabad").FontSize(8).FontColor("#475569");
                                c.Item().AlignRight().Text("Phone: +92 309 9771774 / +92 308 0256000").FontSize(8).FontColor("#475569");
                                c.Item().AlignRight().Text("NTN: 7492018-3").FontSize(8).FontColor("#475569");
                            });
                        });

                        col.Item().PaddingTop(6).PaddingBottom(10).LineHorizontal(1.5f).LineColor("#0f172a");
                    });

                    // Content: Dynamic Schedule & Preamble
                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        // Document Title & Ref
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("AGREEMENT FOR COWORKING SPACE ").Bold().FontSize(13).FontColor("#0f172a");
                                c.Item().Text($"Ref / Quotation: {quotationNumber}").FontSize(8.5f).FontColor("#64748b");
                            });
                            r.ConstantItem(150).AlignRight().Column(c =>
                            {
                                c.Item().Text($"Execution Date: {agreementDateStr}").Bold().FontSize(8.5f).FontColor("#0f172a");
                            });
                        });

                        // Preamble using Span-based composition
                        col.Item().Background("#f8fafc").Border(1).BorderColor("#e2e8f0").Padding(10).Column(pc =>
                        {
                            pc.Spacing(6);
                            pc.Item().Text(text =>
                            {
                                text.Span("This Coworking Space Use Agreement (the ").FontColor("#334155");
                                text.Span("\"Agreement\"").Bold().FontColor("#0f172a");
                                text.Span(") is entered into on ");
                                text.Span($"{agreementDateStr}").Bold().FontColor("#0284c7");
                                text.Span(", by and between:");
                            });

                            pc.Item().PaddingLeft(8).Text(text =>
                            {
                                text.Span("1. Provider: ").Bold().FontColor("#0f172a");
                                text.Span("Work Nest (Pvt) Ltd").Bold().FontColor("#0f172a");
                                text.Span(", having its principal place of business at 3rd Floor EOBI Plaza I-8 Markaz Islamabad (hereinafter referred to as the ");
                                text.Span("\"Provider\"").Bold().FontColor("#0f172a");
                                text.Span("); AND\n");

                                text.Span("2. Customer: ").Bold().FontColor("#0f172a");
                                if (string.Equals(req.EntityType, "Company", StringComparison.OrdinalIgnoreCase))
                                {
                                    text.Span($"{companyName}").Bold().FontColor("#0284c7");
                                    text.Span(" (Registration/CNIC: ");
                                    text.Span($"{idOrRegNumber}").Bold().FontColor("#0f172a");
                                    if (!string.IsNullOrWhiteSpace(req.Ntn))
                                    {
                                        text.Span($", NTN: {req.Ntn}").Bold().FontColor("#0f172a");
                                    }
                                    text.Span($"), located at ");
                                    text.Span($"{address}").Bold().FontColor("#0f172a");
                                    text.Span(" (hereinafter referred to as the ");
                                    text.Span("\"Customer\"").Bold().FontColor("#0f172a");
                                    text.Span(").");
                                }
                                else
                                {
                                    text.Span($"{customerDisplayName}").Bold().FontColor("#0284c7");
                                    text.Span(" (CNIC: ");
                                    text.Span($"{idOrRegNumber}").Bold().FontColor("#0f172a");
                                    if (!string.IsNullOrWhiteSpace(req.Ntn))
                                    {
                                        text.Span($", NTN: {req.Ntn}").Bold().FontColor("#0f172a");
                                    }
                                    text.Span($"), residing at ");
                                    text.Span($"{address}").Bold().FontColor("#0f172a");
                                    text.Span(" (hereinafter referred to as the ");
                                    text.Span("\"Customer\"").Bold().FontColor("#0f172a");
                                    text.Span(").");
                                }
                            });
                        });

                        // Dynamic Schedule Table
                        col.Item().Text("KEY COMMERCIAL & OCCUPANCY TERMS (SCHEDULE A)").Bold().FontSize(9.5f).FontColor("#0f172a");

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(3);
                                cols.RelativeColumn(4);
                                cols.RelativeColumn(3);
                                cols.RelativeColumn(4);
                            });

                            void AddRow(string label1, string val1, string label2 = "", string val2 = "", bool highlight1 = false, bool highlight2 = false)
                            {
                                table.Cell().Background("#f1f5f9").Padding(5).Text(label1).Bold().FontSize(8.5f).FontColor("#475569");
                                var cell1 = table.Cell().Padding(5);
                                if (highlight1) cell1.Text(val1).Bold().FontSize(8.5f).FontColor("#0284c7");
                                else cell1.Text(val1).FontSize(8.5f).FontColor("#0f172a");

                                table.Cell().Background(string.IsNullOrWhiteSpace(label2) ? "#ffffff" : "#f1f5f9").Padding(5).Text(label2).Bold().FontSize(8.5f).FontColor("#475569");
                                var cell2 = table.Cell().Padding(5);
                                if (highlight2) cell2.Text(val2).Bold().FontSize(8.5f).FontColor("#d97706");
                                else cell2.Text(val2).FontSize(8.5f).FontColor("#0f172a");
                            }

                            AddRow("Customer Entity Type", req.EntityType, "Agreement Status", "Standard Issued", false, false);
                            AddRow("Commencement Date", startDateStr, "Expiration Date", endDateStr, true, false);
                            AddRow("Recurring Fee Amount", $"PKR {req.FeeAmount:N2}", "Billing Frequency", billingPeriodStr, true, false);
                            AddRow("Security Deposit (Refundable)", $"PKR {req.SecurityDeposit:N2}");
                            AddRow("Designated Operating Hours", opHoursStr, "Contact Number", req.PhoneNumber ?? "-", false, false);
                        });

                        // Core Acknowledgement Note
                        col.Item().Background("#eff6ff").Border(1).BorderColor("#bfdbfe").Padding(8).Column(ac =>
                        {
                            ac.Item().Text("OPERATIONAL AND STATUTORY COMPLIANCE ACKNOWLEDGEMENT").Bold().FontSize(8.5f).FontColor("#1e40af");
                            ac.Item().Text("By signing below, the Customer expressly agrees to all general terms, house rules, AML/KYC policies, and statutory compliance clauses set forth in the attached Standard Terms & Conditions (Pages 2+) which form an integral and legally binding part of this Agreement.").FontSize(8f).FontColor("#1e3a8a");
                        });

                        // Signatures Block on Page 1
                        col.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.ConstantColumn(16);
                                columns.RelativeColumn();
                            });

                            table.Cell().Column(1).Row(1).Border(1).BorderColor("#0f172a").Padding(8).Column(sc =>
                            {
                                sc.Item().Text("FOR WORK NEST (PVT) LTD (PROVIDER)").Bold().FontSize(8.5f).FontColor("#0f172a");
                                sc.Item().PaddingTop(18).Text("Authorized Signatory: ____________________________").FontSize(8f);
                                sc.Item().PaddingTop(5).Text("Signature: _____________________________________").FontSize(8f);
                                sc.Item().PaddingTop(5).Text($"Date: {agreementDateStr}").FontSize(8f);
                            });

                            table.Cell().Column(2).Row(1);

                            table.Cell().Column(3).Row(1).Border(1).BorderColor("#0f172a").Padding(8).Column(sc =>
                            {
                                sc.Item().Text($"FOR CUSTOMER ({req.EntityType.ToUpperInvariant()})").Bold().FontSize(8.5f).FontColor("#0f172a");
                                string namePlaceholder = !string.IsNullOrWhiteSpace(req.CompanyName) ? req.CompanyName : customerDisplayName;
                                sc.Item().PaddingTop(18).Text($"Name / Representative: {namePlaceholder}").FontSize(8f);
                                sc.Item().PaddingTop(5).Text("Signature: _____________________________________").FontSize(8f);
                                sc.Item().PaddingTop(5).Text($"Date: {agreementDateStr}").FontSize(8f);
                            });
                        });
                    });

                    // Footer for Page 1
                    page.Footer().Column(fc =>
                    {
                        fc.Item().LineHorizontal(0.5f).LineColor("#cbd5e1");
                        fc.Item().PaddingTop(4).Row(r =>
                        {
                            r.RelativeItem().Text($"WorkNest Coworking Agreement • Ref #{quotationNumber}").FontSize(7.5f).FontColor("#94a3b8");
                            r.RelativeItem().AlignRight().Text("Page 1 of Agreement").FontSize(7.5f).FontColor("#94a3b8");
                        });
                    });
                });
            });

            return doc.GeneratePdf();
        }
    }
}
