using System;
using System.Collections.Generic;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkNest.Application.DTOs.Transfer;

namespace WorkNest.Infrastructure.ExternalServices.Pdf
{
    public static class TransferAddendumPdfGenerator
    {
        static TransferAddendumPdfGenerator()
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

        public static byte[] Generate(
            ActiveBookingSummaryDto oldBooking,
            TargetSpaceSummaryDto newSpace,
            TransferSettlementResult settlement,
            int newBookingId,
            string notes)
        {
            var logoPath = GetLogoFilePath();

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(36);
                    page.DefaultTextStyle(x => x.FontSize(9.5f).FontFamily(Fonts.Arial).FontColor("#1f2937"));

                    // Header
                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(titleCol =>
                            {
                                titleCol.Item().Text("WORKNEST CO-WORKING").FontSize(16).Bold().FontColor("#0d9488");
                                titleCol.Item().Text("ADDENDUM TO LEASE AGREEMENT — SPACE TRANSFER").FontSize(12).Bold().FontColor("#111827");
                                titleCol.Item().Text($"Transfer Reference: TRF-{oldBooking.BookingId}-TO-{newBookingId} | Effective: {settlement.EffectiveDate:dd MMM yyyy}").FontSize(8.5f).FontColor("#6b7280");
                            });

                            if (!string.IsNullOrEmpty(logoPath))
                            {
                                row.ConstantItem(120).AlignRight().Height(40).Image(logoPath);
                            }
                        });

                        col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor("#0d9488");
                    });

                    // Body
                    page.Content().PaddingTop(12).Column(col =>
                    {
                        // Preamble
                        col.Item().Text(text =>
                        {
                            text.Span("This Addendum to Lease Agreement (\"Addendum\") is made effective as of ");
                            text.Span(settlement.EffectiveDate.ToString("dd MMMM yyyy")).Bold();
                            text.Span(", by and between WorkNest and ");
                            text.Span(oldBooking.CustomerName ?? "the Customer").Bold();
                            text.Span($" (Customer Code: {oldBooking.CustomerCode ?? "N/A"}), to formally record a change of workspace within the same location.");
                        });

                        // 1. Booking Information Table
                        col.Item().PaddingTop(12).Text("1. REVISED LEASE PARTICULARS").FontSize(10.5f).Bold().FontColor("#0f766e");
                        col.Item().PaddingTop(4).Border(1).BorderColor("#e5e7eb").Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(1.2f);
                                cols.RelativeColumn(1.8f);
                                cols.RelativeColumn(1.2f);
                                cols.RelativeColumn(1.8f);
                            });

                            // Row 1
                            table.Cell().Background("#f3f4f6").Padding(5).Text("Customer Name").Bold();
                            table.Cell().Padding(5).Text(oldBooking.CustomerName ?? "N/A");
                            table.Cell().Background("#f3f4f6").Padding(5).Text("Location / Branch").Bold();
                            table.Cell().Padding(5).Text(oldBooking.LocationName ?? "WorkNest Facility");

                            // Row 2
                            table.Cell().Background("#f3f4f6").Padding(5).Text("Previous Booking #").Bold();
                            table.Cell().Padding(5).Text($"#{oldBooking.BookingId}");
                            table.Cell().Background("#f3f4f6").Padding(5).Text("New Booking #").Bold();
                            table.Cell().Padding(5).Text($"#{newBookingId}").Bold().FontColor("#0d9488");

                            // Row 3
                            table.Cell().Background("#f3f4f6").Padding(5).Text("Previous Space").Bold();
                            table.Cell().Padding(5).Text($"{oldBooking.SpaceName ?? "Space"} ({oldBooking.SpaceCode ?? ""})");
                            table.Cell().Background("#f3f4f6").Padding(5).Text("New Space").Bold();
                            table.Cell().Padding(5).Text($"{newSpace.SpaceName ?? "Space"} ({newSpace.SpaceCode ?? ""})").Bold().FontColor("#0d9488");

                            // Row 4
                            table.Cell().Background("#f3f4f6").Padding(5).Text("Effective Date").Bold();
                            table.Cell().Padding(5).Text(settlement.EffectiveDate.ToString("dd MMM yyyy"));
                            table.Cell().Background("#f3f4f6").Padding(5).Text("Lease Expiry Date").Bold();
                            table.Cell().Padding(5).Text(oldBooking.EndOn.ToString("dd MMM yyyy"));

                            // Row 5
                            table.Cell().Background("#f3f4f6").Padding(5).Text("Previous Monthly Rent").Bold();
                            table.Cell().Padding(5).Text($"PKR {oldBooking.MonthlyRent:N0}");
                            table.Cell().Background("#f3f4f6").Padding(5).Text("New Monthly Rent").Bold();
                            table.Cell().Padding(5).Text($"PKR {newSpace.MonthlyRate:N0}").Bold().FontColor("#0d9488");
                        });

                        // 2. Security Deposit Shift
                        col.Item().PaddingTop(12).Text("2. SECURITY DEPOSIT TRANSFER").FontSize(10.5f).Bold().FontColor("#0f766e");
                        col.Item().PaddingTop(4).Border(1).BorderColor("#e5e7eb").Background("#f9fafb").Padding(8).Text(text =>
                        {
                            text.Span("The refundable Security Deposit of ");
                            text.Span($"PKR {oldBooking.SecurityDeposit:N0}").Bold();
                            text.Span(" previously held under Booking #");
                            text.Span(oldBooking.BookingId.ToString()).Bold();
                            text.Span(" has been transferred in full to the new Booking #");
                            text.Span(newBookingId.ToString()).Bold();
                            text.Span(". No additional security deposit is required and no refund is deducted at this time.");
                        });

                        // 3. Prorated Settlement Calculation
                        col.Item().PaddingTop(12).Text("3. PRORATED SETTLEMENT FOR CURRENT BILLING PERIOD").FontSize(10.5f).Bold().FontColor("#0f766e");
                        col.Item().PaddingTop(4).Border(1).BorderColor("#e5e7eb").Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(3);
                                cols.RelativeColumn(1.5f);
                            });

                            table.Cell().Background("#f3f4f6").Padding(5).Text("Billing Cycle Period").Bold();
                            table.Cell().Padding(5).AlignRight().Text($"{settlement.PeriodStart:dd MMM yyyy} to {settlement.PeriodEnd:dd MMM yyyy}");

                            table.Cell().Background("#f3f4f6").Padding(5).Text("Days Elapsed in Current Space (Paid at Old Rate)").Bold();
                            table.Cell().Padding(5).AlignRight().Text($"{settlement.DaysUsed} day(s)");

                            table.Cell().Background("#f3f4f6").Padding(5).Text("Remaining Days in Cycle (Subject to Proration)").Bold();
                            table.Cell().Padding(5).AlignRight().Text($"{settlement.RemainingDays} day(s)");

                            table.Cell().Background("#f3f4f6").Padding(5).Text("Unused Credit of Previous Space (Old Rate / 30 x Remaining Days)").Bold();
                            table.Cell().Padding(5).AlignRight().Text($"PKR {settlement.CreditAtA:N2}");

                            table.Cell().Background("#f3f4f6").Padding(5).Text("Repriced Charge for New Space (New Rate / 30 x Remaining Days)").Bold();
                            table.Cell().Padding(5).AlignRight().Text($"PKR {settlement.RepricedAtB:N2}");

                            table.Cell().Background("#f3f4f6").Padding(5).Text("Net Adjustment Difference (New Charge - Old Credit)").Bold();
                            table.Cell().Padding(5).AlignRight().Text($"PKR {settlement.Adjustment:N2}").Bold();

                            if (settlement.TransferFee > 0)
                            {
                                table.Cell().Background("#f3f4f6").Padding(5).Text("Transfer Administration Fee").Bold();
                                table.Cell().Padding(5).AlignRight().Text($"PKR {settlement.TransferFee:N2}");
                            }

                            table.Cell().Background("#0d9488").Padding(6).Text("Settlement Outcome").Bold().FontColor("#ffffff");
                            var outcomeText = settlement.Outcome switch
                            {
                                "Billed" => $"Invoiced: PKR {settlement.TotalDue:N2}",
                                "CreditStored" => $"Credited to Account: PKR {Math.Abs(settlement.Adjustment):N2}",
                                _ => "No Adjustment Due"
                            };
                            table.Cell().Background("#0d9488").Padding(6).AlignRight().Text(outcomeText).Bold().FontColor("#ffffff");
                        });

                        // 4. Notes & Operational Terms
                        col.Item().PaddingTop(12).Text("4. OPERATIONAL NOTES & COVENANTS").FontSize(10.5f).Bold().FontColor("#0f766e");
                        col.Item().PaddingTop(4).Border(1).BorderColor("#e5e7eb").Padding(8).Column(termCol =>
                        {
                            if (!string.IsNullOrWhiteSpace(notes))
                            {
                                termCol.Item().Text($"Transfer Note: {notes}").Italic();
                                termCol.Item().PaddingTop(4);
                            }

                            termCol.Item().Text("• All other terms, rules, and conditions set forth in the master Lease Agreement remain in full force and effect.");
                            termCol.Item().Text("• Machine and entrance biometric access permissions have been updated to reflect the new assigned space.");
                            termCol.Item().Text("• Subsequent billing cycles will be billed at the new monthly rental rate of PKR " + newSpace.MonthlyRate.ToString("N0") + ".");
                        });

                        // 5. Sign-off Blocks
                        col.Item().PaddingTop(24).Row(row =>
                        {
                            row.RelativeItem().Column(sigCol =>
                            {
                                sigCol.Item().Text("FOR WORKNEST:").Bold().FontColor("#374151");
                                sigCol.Item().PaddingTop(30).LineHorizontal(1).LineColor("#9ca3af");
                                sigCol.Item().PaddingTop(2).Text("Authorized Representative");
                                sigCol.Item().Text("WorkNest Co-Working");
                            });

                            row.ConstantItem(40);

                            row.RelativeItem().Column(sigCol =>
                            {
                                sigCol.Item().Text("ACCEPTED AND CONFIRMED BY:").Bold().FontColor("#374151");
                                sigCol.Item().PaddingTop(30).LineHorizontal(1).LineColor("#9ca3af");
                                sigCol.Item().PaddingTop(2).Text(oldBooking.CustomerName ?? "Customer");
                                sigCol.Item().Text($"CNIC / ID: {oldBooking.CustomerCode ?? ""}");
                            });
                        });
                    });

                    // Footer
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("WorkNest Coworking — Space Transfer Addendum | Generated on ");
                        x.Span(DateTime.Now.ToString("dd MMM yyyy HH:mm"));
                    });
                });
            });

            return document.GeneratePdf();
        }
    }
}

