using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;
using System.Linq;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.DTOs.Quotation;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.Pdf
{
    public class PdfService : IPdfService
    {
        static PdfService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public byte[] GenerateQuotationPdf(QuotationResponse q)
        {
            if (q.Fields == null || q.Fields.Count == 0)
            {
                ChallanFieldBuilder.BuildForQuotation(q);
            }

            string spaceType = q.SpaceType;
            if (string.IsNullOrWhiteSpace(spaceType)) spaceType = "MeetingRoom";

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Header().Element(ComposeHeader);

                    page.Content().Column(col =>
                    {
                        col.Spacing(12);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("QUOTATION").FontSize(18).Bold().FontColor("#1a1a2e");
                                c.Item().Text($"# {q.QuotationNumber}").FontSize(11).FontColor("#555555");
                            });
                            row.ConstantItem(160).Column(c =>
                            {
                                c.Item().AlignRight().Text($"Date: {q.QuotationDate:dd MMM yyyy}").FontColor("#555555");
                                c.Item().AlignRight().Text($"Valid Until: {q.ValidUntil:dd MMM yyyy}").FontColor("#555555");
                            });
                        });

                        col.Item().LineHorizontal(1).LineColor("#e0e0e0");

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Quotation To").FontSize(9).Bold().FontColor("#888888");
                                if (!string.IsNullOrWhiteSpace(q.CustomerCompany))
                                {
                                    c.Item().Text($"Bill to: {q.CustomerCompany}").FontColor("#1a1a2e").Bold();
                                }
                                c.Item().Text($"Attn: {q.CustomerName ?? "-"}").FontColor("#444444");
                                c.Item().Text($"Email: {q.CustomerEmail ?? "-"}").FontColor("#555555");
                            });
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Quotation For").FontSize(9).Bold().FontColor("#888888");
                                c.Item().Text(q.SpaceName ?? q.SpaceCode ?? "-").Bold();
                                c.Item().Text(q.SpaceTypeName ?? "-").FontColor("#555555");
                                c.Item().Text(q.LocationName ?? "-").FontColor("#555555");
                            });
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("PERIOD").FontSize(9).Bold().FontColor("#888888");
                                if (spaceType == "MeetingRoom")
                                {
                                    c.Item().Text($"From: {q.StartDateTime:dd MMM yyyy hh:mm tt}");
                                    c.Item().Text($"To:   {q.EndDateTime:dd MMM yyyy hh:mm tt}");
                                }
                                else
                                {
                                    c.Item().Text($"From: {q.StartDateTime:dd MMM yyyy}");
                                    c.Item().Text($"To:   {q.EndDateTime:dd MMM yyyy}");
                                }
                            });
                        });

                        col.Item().LineHorizontal(1).LineColor("#e0e0e0");

                        int contractMonths = q.Contract?.NumberOfMonths > 0 ? q.Contract.NumberOfMonths : (int)Math.Max(1, Math.Round((q.EndDateTime - q.StartDateTime).TotalDays / 30));
                        int billingMonths = q.BillingPeriodMonths > 0 ? q.BillingPeriodMonths : 3;
                        decimal totalContract = q.TotalContractAmount > 0 ? q.TotalContractAmount : q.TotalAmount;
                        decimal monthlyRent = q.MonthlyRent > 0 ? q.MonthlyRent : (contractMonths > 0 ? totalContract / contractMonths : totalContract);
                        decimal firstCycleRent = q.CurrentCycleAmount > 0 ? q.CurrentCycleAmount : (monthlyRent * billingMonths);
                        
                        decimal secDeposit = q.SecurityDeposit;
                        if (secDeposit <= 0 && q.Details != null && q.Details.Count > 0)
                        {
                            var secLine = q.Details.FirstOrDefault(d => string.Equals(d.FeeType, "SecurityDeposit", StringComparison.OrdinalIgnoreCase) || (d.Description != null && d.Description.Contains("Security Deposit", StringComparison.OrdinalIgnoreCase)));
                            if (secLine != null && secLine.Amount > 0) secDeposit = secLine.Amount;
                        }
                        if (secDeposit <= 0 && q.Contract != null && q.Contract.SecurityDeposit > 0)
                        {
                            secDeposit = q.Contract.SecurityDeposit;
                        }
                        if (secDeposit <= 0 && spaceType == "PrivateRoom")
                        {
                            secDeposit = monthlyRent;
                        }

                        decimal discountAmount = q.DiscountAmount;
                        if (q.DiscountPercentage > 0 && spaceType != "MeetingRoom")
                        {
                            discountAmount = Math.Round(firstCycleRent * (q.DiscountPercentage / 100.0m), 2);
                        }
                        else if (string.Equals(q.DiscountType, "Percentage", StringComparison.OrdinalIgnoreCase) && q.DiscountValue > 0 && spaceType != "MeetingRoom")
                        {
                            discountAmount = Math.Round(firstCycleRent * (q.DiscountValue / 100.0m), 2);
                        }

                        decimal taxPct = q.AppliedTaxPercentage > 0 ? q.AppliedTaxPercentage : 16.00m;
                        decimal taxOnAdvanceRent = q.TaxAmountOnAdvanceRent > 0 ? q.TaxAmountOnAdvanceRent : Math.Round(Math.Round(firstCycleRent * 0.10m, 2) * (taxPct / 100.0m), 2);
                        decimal initialPayable = q.TotalPayable > 0 ? q.TotalPayable : Math.Max(0, firstCycleRent + (spaceType == "PrivateRoom" ? secDeposit : 0) + taxOnAdvanceRent - discountAmount);

                        // Dynamic Summary Ribbon
                        col.Item().Background("#f8fafc").Border(1).BorderColor("#e2e8f0").Padding(8).Row(row =>
                        {
                            if (spaceType == "MeetingRoom")
                            {
                                var timeSpan = q.EndDateTime - q.StartDateTime;
                                double hours = Math.Max(1, Math.Ceiling(timeSpan.TotalHours));
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("BOOKING RENT").FontSize(7.5f).Bold().FontColor("#64748b");
                                    c.Item().Text($"PKR {q.SubtotalAmount:N2}").Bold().FontSize(9.5f).FontColor("#0f172a");
                                    c.Item().Text("Base Booking Rent").FontSize(7f).FontColor("#64748b");
                                });
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("DURATION").FontSize(7.5f).Bold().FontColor("#64748b");
                                    c.Item().Text($"{hours} Hour(s)").Bold().FontSize(9.5f).FontColor("#2563eb");
                                    c.Item().Text("Booking Length").FontSize(7f).FontColor("#64748b");
                                });
                                row.RelativeItem().Background("#eff6ff").Padding(4).Column(c =>
                                {
                                    c.Item().Text("TOTAL PAYABLE").FontSize(7.5f).Bold().FontColor("#1e40af");
                                    c.Item().Text($"PKR {initialPayable:N2}").Bold().FontSize(9.5f).FontColor("#1e40af");
                                    c.Item().Text("Rent + Tax").FontSize(7f).FontColor("#1e40af");
                                });
                            }
                            else if (spaceType == "SharedSpace")
                            {
                                // row.RelativeItem().Column(c =>
                                // {
                                //     c.Item().Text("TOTAL CONTRACT").FontSize(7.5f).Bold().FontColor("#64748b");
                                //     c.Item().Text($"PKR {totalContract:N2}").Bold().FontSize(9.5f).FontColor("#0f172a");
                                //     c.Item().Text($"{contractMonths} Month(s) Total").FontSize(7f).FontColor("#64748b");
                                // });
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("MONTHLY RENT").FontSize(7.5f).Bold().FontColor("#64748b");
                                    c.Item().Text($"PKR {monthlyRent:N2}").Bold().FontSize(9.5f).FontColor("#2563eb");
                                    c.Item().Text("per month").FontSize(7f).FontColor("#64748b");
                                });
                                // row.RelativeItem().Column(c =>
                                // {
                                //     c.Item().Text("1ST CYCLE RENT").FontSize(7.5f).Bold().FontColor("#1e40af");
                                //     c.Item().Text($"PKR {firstCycleRent:N2}").Bold().FontSize(9.5f).FontColor("#1d4ed8");
                                //     c.Item().Text($"First {billingMonths} Months").FontSize(7f).FontColor("#1e40af");
                                // });
                                // row.RelativeItem().Background("#eff6ff").Padding(4).Column(c =>
                                // {
                                //     c.Item().Text("1ST CYCLE PAYABLE").FontSize(7.5f).Bold().FontColor("#1e40af");
                                //     c.Item().Text($"PKR {initialPayable:N2}").Bold().FontSize(9.5f).FontColor("#1e40af");
                                //     c.Item().Text("Rent + Tax").FontSize(7f).FontColor("#1e40af");
                                // });
                            }
                            else // PrivateRoom
                            {
                                // row.RelativeItem().Column(c =>
                                // {
                                //     c.Item().Text("TOTAL CONTRACT").FontSize(7.5f).Bold().FontColor("#64748b");
                                //     c.Item().Text($"PKR {totalContract:N2}").Bold().FontSize(9.5f).FontColor("#0f172a");
                                //     c.Item().Text($"{contractMonths} Month(s) Total").FontSize(7f).FontColor("#64748b");
                                // });
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("MONTHLY RENT").FontSize(7.5f).Bold().FontColor("#64748b");
                                    c.Item().Text($"PKR {monthlyRent:N2}").Bold().FontSize(9.5f).FontColor("#2563eb");
                                    c.Item().Text("per month").FontSize(7f).FontColor("#64748b");
                                });
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("SECURITY DEPOSIT").FontSize(7.5f).Bold().FontColor("#64748b");
                                    c.Item().Text($"PKR {secDeposit:N2}").Bold().FontSize(9.5f).FontColor("#d97706");
                                    c.Item().Text("Refundable").FontSize(7f).FontColor("#64748b");
                                });
                                // row.RelativeItem().Background("#eff6ff").Padding(4).Column(c =>
                                // {
                                //     c.Item().Text("1ST CYCLE PAYABLE").FontSize(7.5f).Bold().FontColor("#1e40af");
                                //     c.Item().Text($"PKR {initialPayable:N2}").Bold().FontSize(9.5f).FontColor("#1e40af");
                                //     c.Item().Text("Rent + Deposit + Tax").FontSize(7f).FontColor("#1e40af");
                                // });
                            }
                        });

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(4);
                                cols.RelativeColumn(1);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                var durationHeader = spaceType == "MeetingRoom" ? "Duration" : "No. of Months";
                                foreach (var h in new[] { "Description", durationHeader, "Unit Price", "Amount" })
                                    header.Cell().Background("#1a1a2e").Padding(6)
                                        .Text(h).FontColor(Colors.White).Bold().FontSize(9);
                            });

                            bool alt = false;
                            if (q.Details != null && q.Details.Count > 0)
                            {
                                foreach (var d in q.Details)
                                {
                                    var bg = alt ? "#f9f9f9" : "#ffffff";
                                    table.Cell().Background(bg).Padding(6).Text(d.Description);

                                    string qtyLabel = spaceType == "MeetingRoom"
                                        ? $"{d.Quantity:N0} Hour(s)"
                                        : $"{d.Quantity:N0} Month(s)";

                                    table.Cell().Background(bg).Padding(6).AlignRight().Text(qtyLabel);
                                    table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {d.UnitPrice:N2}");
                                    table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {d.Amount:N2}");
                                    alt = !alt;
                                }
                            }
                            else
                            {
                                var bg = "#ffffff";
                                table.Cell().Background(bg).Padding(6).Text("Room Rent");
                                if (spaceType == "MeetingRoom")
                                {
                                    var timeSpan = q.EndDateTime - q.StartDateTime;
                                    double hours = Math.Max(1, Math.Ceiling(timeSpan.TotalHours));
                                    table.Cell().Background(bg).Padding(6).AlignRight().Text($"{hours} Hour(s)");
                                }
                                else
                                {
                                    table.Cell().Background(bg).Padding(6).AlignRight().Text($"{billingMonths} Month(s)");
                                }
                                table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {monthlyRent:N2}");
                                table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {firstCycleRent:N2}");
                            }
                        });

                        col.Item().AlignRight().Column(c =>
                        {
                            c.Spacing(3);
                            if (spaceType != "MeetingRoom")
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text($"Advance Room Rent ({billingMonths} Mos):");
                                    r.ConstantItem(120).AlignRight().Text($"PKR {firstCycleRent:N2}").Bold();
                                });
                                if (spaceType == "PrivateRoom" && secDeposit > 0)
                                {
                                    c.Item().Row(r =>
                                    {
                                        r.ConstantItem(220).AlignRight().Text("Security Deposit (Refundable):");
                                        r.ConstantItem(120).AlignRight().Text($"PKR {secDeposit:N2}").Bold().FontColor("#d97706");
                                    });
                                }
                            }
                            else
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text("Booking Rent Amount:");
                                    r.ConstantItem(120).AlignRight().Text($"PKR {q.SubtotalAmount:N2}").Bold();
                                });
                            }

                            if (q.TaxAmount > 0)
                            {
                                // c.Item().Row(r =>
                                // {
                                //     r.ConstantItem(220).AlignRight().Text($"Provincial Sales Tax ({taxPct:G29}%):").FontColor("#15803d");
                                //     r.ConstantItem(120).AlignRight().Text($"PKR {q.TaxAmount:N2}").FontColor("#15803d");
                                // });
                            }
                            if (q.DiscountAmount > 0)
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text("Discount:").FontColor("#e74c3c");
                                    r.ConstantItem(120).AlignRight().Text($"- PKR {q.DiscountAmount:N2}").FontColor("#e74c3c");
                                });
                            }
                            c.Item().LineHorizontal(1).LineColor("#1a1a2e");
                            c.Item().Row(r =>
                            {
                                r.ConstantItem(220).AlignRight().Text("TOTAL INITIAL AMOUNT PAYABLE:").Bold().FontSize(11);
                                r.ConstantItem(120).AlignRight().Text($"PKR {q.TotalPayable:N2}").Bold().FontSize(11).FontColor("#1d4ed8");
                            });
                        });

                        if (!string.IsNullOrWhiteSpace(q.Remarks))
                        {
                            col.Item().Background("#f5f5f5").Padding(10).Column(c =>
                            {
                                c.Item().Text("Remarks").Bold().FontSize(9).FontColor("#888888");
                                c.Item().Text(q.Remarks);
                            });
                        }

                        col.Item().PaddingTop(8).Border(1).BorderColor("#dee2e6").Background("#f8f9fa").Padding(10).Column(tc =>
                        {
                            tc.Item().Text("Terms & Conditions").Bold().FontSize(9).FontColor("#495057");
                            tc.Spacing(2);
                            tc.Item().Text("1. The price is exclusive of all tax.").FontSize(8).FontColor("#6c757d");
                            tc.Item().Text("2. Worknest will charge Provincial sales tax on support service.").FontSize(8).FontColor("#6c757d");
                            tc.Item().Text("3. This quotation is valid until the date specified above. Prices are subject to change after expiry.").FontSize(8).FontColor("#6c757d");
                            if (spaceType == "PrivateRoom")
                            {
                                tc.Item().Text("4. Security deposit is fully refundable upon termination of the agreement, subject to lease terms.").FontSize(8).FontColor("#6c757d");
                                tc.Item().Text("5. Booking confirmation is subject to space availability at the time of payment.").FontSize(8).FontColor("#6c757d");
                                tc.Item().Text("6. WorkNest reserves the right to modify pricing and terms with prior notice.").FontSize(8).FontColor("#6c757d");
                            }
                            else
                            {
                                tc.Item().Text("4. Booking confirmation is subject to space availability at the time of payment.").FontSize(8).FontColor("#6c757d");
                                tc.Item().Text("5. WorkNest reserves the right to modify pricing and terms with prior notice.").FontSize(8).FontColor("#6c757d");
                            }
                        });
                    });

                    page.Footer().Element(ComposeFooter);
                });
            }).GeneratePdf();
        }

        public byte[] GenerateBookingConfirmationPdf(ChallanResponseDto c)
        {
            if (c.Fields == null || c.Fields.Count == 0)
            {
                ChallanFieldBuilder.BuildForChallan(c);
            }

            string spaceType = c.SpaceType;
            if (string.IsNullOrWhiteSpace(spaceType)) spaceType = c.IsMeetingRoom ? "MeetingRoom" : "SharedSpace";

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Header().Element(ComposeHeader);

                    page.Content().Column(col =>
                    {
                        col.Spacing(12);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(inner =>
                            {
                                inner.Item().Text("BOOKING CONFIRMATION").FontSize(20).Bold().FontColor("#1a1a2e");
                                inner.Item().Text(c.ChallanNumber).FontSize(11).FontColor("#555555");
                            });
                            row.ConstantItem(160).Column(inner =>
                            {
                                inner.Item().AlignRight().Text($"Issued: {c.IssuedOn:dd MMM yyyy}").FontColor("#555555");
                                inner.Item().AlignRight().Text($"Valid Until: {c.ValidUntil:dd MMM yyyy}").FontColor("#555555");
                                // inner.Item().AlignRight().Text($"Status: {c.BookingStatusLabel ?? c.BookingStatusCode ?? "-"}").Bold();
                            });
                        });

                        col.Item().LineHorizontal(1).LineColor("#e0e0e0");

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(inner =>
                            {
                                inner.Item().Text("CUSTOMER").FontSize(9).Bold().FontColor("#888888");
                                inner.Item().Text("Company: " + (c.CustomerCompany ?? "-"));
                                inner.Item().Text("attn: " + (c.CustomerName ?? "-")).Bold();
                                inner.Item().Text("Email: " + (c.CustomerEmail ?? "-")).FontColor("#555555");
                            });
                            row.RelativeItem().Column(inner =>
                            {
                                inner.Item().Text("SPACE").FontSize(9).Bold().FontColor("#888888");
                                inner.Item().Text(c.SpaceName ?? c.SpaceCode ?? "-").Bold();
                                inner.Item().Text(c.SpaceTypeName ?? "-").FontColor("#555555");
                                inner.Item().Text(c.LocationName ?? "-").FontColor("#555555");
                                inner.Item().Text(c.BranchName ?? "-").FontColor("#555555");
                            });
                            row.RelativeItem().Column(inner =>
                            {
                                inner.Item().Text("BOOKING DETAILS").FontSize(9).Bold().FontColor("#888888");
                                if (spaceType == "MeetingRoom")
                                {
                                    inner.Item().Text($"From: {c.StartOn:dd MMM yyyy hh:mm tt}");
                                    inner.Item().Text($"To:   {c.EndOn:dd MMM yyyy hh:mm tt}");
                                    if (!string.IsNullOrWhiteSpace(c.TimeSlot))
                                    {
                                        inner.Item().Text($"Slot: {c.TimeSlot}").FontColor("#555555");
                                    }
                                }
                                else
                                {
                                    inner.Item().Text($"From: {c.StartOn:dd MMM yyyy}");
                                    inner.Item().Text($"To:   {c.EndOn:dd MMM yyyy}");
                                    inner.Item().Text($"Billing: {c.BillingPeriodLabel ?? c.BillingPeriodCode ?? "-"}").FontColor("#555555");
                                    // inner.Item().Text($"Contract Total: PKR {c.TotalContractAmount:N2}").FontColor("#15803d").Bold();
                                    if (c.NextBillDueDate.HasValue)
                                    {
                                        inner.Item().Text($"Next Bill Due: {c.NextBillDueDate:dd MMM yyyy}").FontColor("#0284c7");
                                    }
                                    // inner.Item().Text($"Balance Left: PKR {c.BalanceLeft:N2}").FontColor("#b45309");
                                }
                            });
                        });

                        col.Item().LineHorizontal(1).LineColor("#e0e0e0");

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(3);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                foreach (var h in new[] { "Description", "Office Number", "Unit Price", "Add Ons", "Discount", "Total" })
                                    header.Cell().Background("#1a1a2e").Padding(6)
                                        .Text(h).FontColor(Colors.White).Bold().FontSize(9);
                            });

                            bool alt = false;
                            var displayLines = spaceType == "MeetingRoom"
                                ? c.Details.Where(d => !d.ChargeTypeCode.Contains("TAX", StringComparison.OrdinalIgnoreCase) && !d.ChargeTypeLabel.Contains("Tax", StringComparison.OrdinalIgnoreCase) && !d.ChargeTypeCode.Contains("SERVICE", StringComparison.OrdinalIgnoreCase)).ToList()
                                : c.Details;

                            if (displayLines.Count == 0)
                            {
                                displayLines = new List<ChallanLineDto>
                                {
                                    new ChallanLineDto
                                    {
                                        Description = "Meeting Room Rent",
                                        Quantity = 1,
                                        UnitPrice = c.SubtotalAmount,
                                        LineTotal = c.SubtotalAmount
                                    }
                                };
                            }

                            foreach (var line in displayLines)
                            {
                                var bg = alt ? "#f9f9f9" : "#ffffff";
                                var desc = string.IsNullOrWhiteSpace(line.Description) ? line.ChargeTypeLabel : line.Description;
                                string officeNo = c.SpaceCode ?? c.SpaceNumber ?? c.SpaceName ?? "-";
                                table.Cell().Background(bg).Padding(6).Text(desc);
                                table.Cell().Background(bg).Padding(6).AlignRight().Text(officeNo);
                                table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {line.UnitPrice:N2}");
                                table.Cell().Background(bg).Padding(6).AlignRight().Text("-");
                                table.Cell().Background(bg).Padding(6).AlignRight().Text(line.DiscountAmount > 0 ? $"PKR {line.DiscountAmount:N2}" : "-");
                                table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {line.LineTotal:N2}");
                                alt = !alt;
                            }
                        });

                        col.Item().AlignRight().Column(inner =>
                        {
                            inner.Spacing(3);
                            if (spaceType != "MeetingRoom")
                            {
                                inner.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text($"Advance Rent ({c.BillingPeriodLabel ?? c.BillingPeriodCode ?? "Cycle"}):");
                                    r.ConstantItem(120).AlignRight().Text($"PKR {c.CurrentCycleAmount:N2}");
                                });
                                if (spaceType == "PrivateRoom" && c.SecurityDeposit > 0)
                                {
                                    inner.Item().Row(r =>
                                    {
                                        r.ConstantItem(220).AlignRight().Text("Security Deposit (Refundable):");
                                        r.ConstantItem(120).AlignRight().Text($"PKR {c.SecurityDeposit:N2}").FontColor("#d97706");
                                    });
                                }
                            }
                            else
                            {
                                inner.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text("Booking Rent Amount:");
                                    r.ConstantItem(120).AlignRight().Text($"PKR {c.SubtotalAmount:N2}").Bold();
                                });
                            }

                            if (c.TaxAmount > 0)
                            {
                                inner.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text($"Provincial Sales Tax (PST):").FontColor("#15803d");
                                    r.ConstantItem(120).AlignRight().Text($"PKR {c.TaxAmount:N2}").FontColor("#15803d");
                                });
                            }
                            if (c.DiscountAmount > 0)
                            {
                                inner.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text($"Discount:").FontColor("#e74c3c");
                                    r.ConstantItem(120).AlignRight().Text($"- PKR {c.DiscountAmount:N2}").FontColor("#e74c3c");
                                });
                            }
                            inner.Item().LineHorizontal(1).LineColor("#1a1a2e");
                            inner.Item().Row(r =>
                            {
                                r.ConstantItem(220).AlignRight().Text("TOTAL INITIAL AMOUNT PAYABLE:").Bold().FontSize(11);
                                r.ConstantItem(120).AlignRight().Text($"PKR {c.TotalPayable:N2}").Bold().FontSize(11).FontColor("#1d4ed8");
                            });
                        });
                        if (!string.IsNullOrWhiteSpace(c.ChallanNotes))
                        {
                            col.Item().Background("#f5f5f5").Padding(10).Column(inner =>
                            {
                                inner.Item().Text("Notes").Bold().FontSize(9).FontColor("#888888");
                                inner.Item().Text(c.ChallanNotes);
                            });
                        }

                        col.Item().PaddingTop(8).Border(1).BorderColor("#dee2e6").Background("#f8f9fa").Padding(10).Column(tc =>
                        {
                            tc.Item().Text("Terms & Conditions").Bold().FontSize(9).FontColor("#495057");
                            tc.Spacing(2);
                            tc.Item().Text("1. The price includes 10% support services and Worknest will charge Provincial sales tax on this service.").FontSize(8).FontColor("#6c757d");
                            tc.Item().Text("2. Payment must be made before the lexpiry date to confirm the booking.").FontSize(8).FontColor("#6c757d");
                            if (spaceType == "PrivateRoom")
                            {
                                tc.Item().Text("3. Security deposit is fully refundable upon termination of the agreement, subject to lease terms.").FontSize(8).FontColor("#6c757d");
                            }
                            tc.Item().Text("4. Cancellation policy applies as per the signed agreement.").FontSize(8).FontColor("#6c757d");
                            tc.Item().Text("5. WorkNest reserves the right to modify pricing and terms with prior notice.").FontSize(8).FontColor("#6c757d");
                        });
                    });

                    page.Footer().Element(ComposeFooter);
                });
            }).GeneratePdf();
        }

        public byte[] GenerateAdvanceInvoicePdf(WorkNest.Application.DTOs.Payment.AdvanceInvoicePdfDto inv)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Header().Element(ComposeHeader);

                    page.Content().Column(col =>
                    {
                        col.Spacing(12);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("ADVANCE PAYMENT INVOICE").FontSize(20).Bold().FontColor("#1a1a2e");
                                c.Item().Text($"Invoice # {inv.InvoiceNumber}").FontSize(11).FontColor("#555555");
                            });
                            row.ConstantItem(160).Column(c =>
                            {
                                c.Item().AlignRight().Text($"Issued: {inv.IssuedOn:dd MMM yyyy}").FontColor("#555555");
                                c.Item().AlignRight().Text($"Advance Months: {inv.AdvanceRentMonths}").FontColor("#555555");
                                if (inv.SecurityDepositMonths > 0)
                                    c.Item().AlignRight().Text($"Security Deposit Months: {inv.SecurityDepositMonths}").FontColor("#555555");
                            });
                        });

                        col.Item().LineHorizontal(1).LineColor("#e0e0e0");

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("CUSTOMER").FontSize(9).Bold().FontColor("#888888");
                                c.Item().Text(inv.CustomerName ?? "-").Bold();
                                c.Item().Text(inv.CustomerEmail ?? "-").FontColor("#555555");
                                if (!string.IsNullOrWhiteSpace(inv.CustomerCode))
                                    c.Item().Text($"Code: {inv.CustomerCode}").FontColor("#555555");
                            });
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("SPACE").FontSize(9).Bold().FontColor("#888888");
                                c.Item().Text(inv.SpaceName ?? "-").Bold();
                                c.Item().Text(inv.SpaceTypeName ?? "-").FontColor("#555555");
                                c.Item().Text(inv.LocationName ?? "-").FontColor("#555555");
                            });
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("BOOKING PERIOD").FontSize(9).Bold().FontColor("#888888");
                                c.Item().Text($"From: {inv.StartOn:dd MMM yyyy}");
                                c.Item().Text($"To:   {inv.EndOn:dd MMM yyyy}");
                            });
                        });

                        col.Item().LineHorizontal(1).LineColor("#e0e0e0");

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(4);
                                cols.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                foreach (var h in new[] { "Description", "Amount (PKR)" })
                                    header.Cell().Background("#1a1a2e").Padding(6)
                                        .Text(h).FontColor(Colors.White).Bold().FontSize(9);
                            });

                            bool alt = false;
                            if (inv.MonthsBreakdown != null && inv.MonthsBreakdown.Count > 0)
                            {
                                foreach (var m in inv.MonthsBreakdown)
                                {
                                    var bg = alt ? "#f9f9f9" : "#ffffff";
                                    table.Cell().Background(bg).Padding(6).Text($"Advance Rent “ {m.MonthName}");
                                    table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {m.Amount:N2}");
                                    alt = !alt;
                                }
                            }
                            else
                            {
                                var bg = "#ffffff";
                                table.Cell().Background(bg).Padding(6).Text($"Advance Rent ({inv.AdvanceRentMonths} Month(s))");
                                table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {inv.AdvanceRentTotal:N2}");
                                alt = true;
                            }

                            if (inv.SecurityDepositTotal > 0)
                            {
                                var bg = alt ? "#f9f9f9" : "#ffffff";
                                table.Cell().Background(bg).Padding(6).Text($"Security Deposit ({inv.SecurityDepositMonths} Month(s))");
                                table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {inv.SecurityDepositTotal:N2}");
                                alt = !alt;
                            }
                        });

                        col.Item().AlignRight().Column(c =>
                        {
                            c.Spacing(3);
                            c.Item().Row(r =>
                            {
                                r.ConstantItem(160).AlignRight().Text("Advance Rent:");
                                r.ConstantItem(120).AlignRight().Text($"PKR {inv.AdvanceRentTotal:N2}");
                            });
                            if (inv.SecurityDepositTotal > 0)
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(160).AlignRight().Text("Security Deposit:");
                                    r.ConstantItem(120).AlignRight().Text($"PKR {inv.SecurityDepositTotal:N2}");
                                });
                            }
                            if (inv.DiscountAmount > 0)
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(160).AlignRight().Text("Discount:").FontColor("#e74c3c");
                                    r.ConstantItem(120).AlignRight().Text($"- PKR {inv.DiscountAmount:N2}").FontColor("#e74c3c");
                                });
                            }
                            c.Item().LineHorizontal(1).LineColor("#1a1a2e");
                            c.Item().Row(r =>
                            {
                                r.ConstantItem(160).AlignRight().Text("Total Payable:").Bold().FontSize(12);
                                r.ConstantItem(120).AlignRight().Text($"PKR {inv.TotalPayable:N2}").Bold().FontSize(12).FontColor("#1a1a2e");
                            });
                        });

                        if (!string.IsNullOrWhiteSpace(inv.Notes))
                        {
                            col.Item().Background("#f5f5f5").Padding(10).Column(c =>
                            {
                                c.Item().Text("Notes").Bold().FontSize(9).FontColor("#888888");
                                c.Item().Text(inv.Notes);
                            });
                        }

                        col.Item().PaddingTop(8).Border(1).BorderColor("#dee2e6").Background("#f8f9fa").Padding(10).Column(tc =>
                        {
                            tc.Item().Text("Terms & Conditions").Bold().FontSize(9).FontColor("#495057");
                            tc.Spacing(2);
                            tc.Item().Text("1. The price includes 10% support services and Worknest will charge Provincial sales tax on this service.").FontSize(8).FontColor("#6c757d");
                            tc.Item().Text("2. Security deposit is fully refundable upon termination, subject to lease terms.").FontSize(8).FontColor("#6c757d");
                            tc.Item().Text("3. WorkNest reserves the right to modify pricing and terms with prior notice.").FontSize(8).FontColor("#6c757d");
                        });
                    });

                    page.Footer().Element(ComposeFooter);
                });
            }).GeneratePdf();
        }

        private static readonly string LogoPath = @"F:\WorkNest_FE\public\images\Logo.png";

        private static void ComposeHeader(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Column(c =>
                    {
                        if (File.Exists(LogoPath))
                        {
                            c.Item().Row(r =>
                            {
                                r.AutoItem().Height(40).Image(LogoPath).FitHeight();
                                r.AutoItem().AlignMiddle().PaddingLeft(2).Text("orkNest").FontSize(25).Bold().FontColor("#1a1a2e");
                            });
                        }
                        else
                        {
                            c.Item().Text("WorkNest").FontSize(22).Bold().FontColor("#1a1a2e");
                        }
                        // c.Item().Text("Coworking Space Management").FontSize(8.5f).FontColor("#64748b");
                    });

                    row.ConstantItem(240).Column(c =>
                    {
                        c.Item().AlignRight().Text("3rd Floor EOBI Building-II, I-8 Markaz").FontSize(8.5f).FontColor("#475569");
                        c.Item().AlignRight().Text("Islamabad, Pakistan").FontSize(8.5f).FontColor("#475569");
                        c.Item().AlignRight().Text("Phone: +92 309 9771774 / +92 308 0256000").FontSize(8.5f).FontColor("#475569");
                        c.Item().AlignRight().Text("Email: sales@worknestpk.com").FontSize(8.5f).FontColor("#475569");
                    });
                });
                col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor("#1a1a2e");
            });
        }

        private static void ComposeFooter(IContainer container)
        {
            container.Column(c =>
            {
                c.Item().LineHorizontal(1).LineColor("#e0e0e0");
                c.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text($"Generated on {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC").FontSize(8).FontColor("#aaaaaa");
                    row.RelativeItem().AlignRight().Text(x =>
                    {
                        x.Span("Page ").FontSize(8).FontColor("#aaaaaa");
                        x.CurrentPageNumber().FontSize(8).FontColor("#aaaaaa");
                        x.Span(" of ").FontSize(8).FontColor("#aaaaaa");
                        x.TotalPages().FontSize(8).FontColor("#aaaaaa");
                    });
                });
            });
        }
    }
}
