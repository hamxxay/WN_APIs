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
                            row.RelativeItem(1.5f).Column(c =>
                            {
                                c.Item().Text("Customer Details").FontSize(9).Bold().FontColor("#000000");
                                c.Item().Text($"Attn: {q.CustomerName ?? "-"}").FontColor("#000000");
                                if (!string.IsNullOrWhiteSpace(q.CustomerCompany))
                                {
                                    c.Item().Text($"Customer Name: {q.CustomerCompany}").FontColor("#000000");
                                }
                                if (!string.IsNullOrWhiteSpace(q.CustomerAddress))
                                {
                                    c.Item().Text($"Address: {q.CustomerAddress}").FontColor("#555555");
                                }
                                // c.Item().Text($"Email: {q.CustomerEmail ?? "-"}").FontColor("#555555");
                            });

                            row.RelativeItem(1.0f).Column(c =>
                            {
                                c.Item().Text("Quotation For").FontSize(9).Bold().FontColor("#000000");
                                c.Item().Text(q.SpaceName ?? q.SpaceCode ?? "-");
                                c.Item().Text(q.SpaceTypeName ?? "-").FontColor("#555555");

                                string cityName = !string.IsNullOrWhiteSpace(q.CityName) ? q.CityName : "Islamabad";
                                string locName = q.LocationName ?? "";
                                string locationDisplay = locName;
                                if (!string.IsNullOrWhiteSpace(cityName) && !locName.Contains(cityName, StringComparison.OrdinalIgnoreCase))
                                {
                                    locationDisplay = string.IsNullOrWhiteSpace(locName) ? cityName : $"{locName}, {cityName}";
                                }
                                if (string.IsNullOrWhiteSpace(locationDisplay)) locationDisplay = "-";
                                c.Item().Text(locationDisplay).FontColor("#555555");
                            });

                            row.RelativeItem(0.85f).Column(c =>
                            {
                                c.Item().Text("PERIOD").FontSize(9).Bold().FontColor("#000000");
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
                        var rentDetail = q.Details?.FirstOrDefault(d => string.Equals(d.FeeType, "RoomRent", StringComparison.OrdinalIgnoreCase) || (!string.Equals(d.FeeType, "SecurityDeposit", StringComparison.OrdinalIgnoreCase) && d.Description != null && !d.Description.Contains("Security Deposit", StringComparison.OrdinalIgnoreCase)));
                        decimal monthlyRent = rentDetail != null && rentDetail.UnitPrice > 0 
                            ? rentDetail.UnitPrice 
                            : (q.MonthlyRent > 0 ? q.MonthlyRent : (contractMonths > 0 ? totalContract / contractMonths : totalContract));
                        decimal firstCycleRent = monthlyRent * billingMonths;
                        
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
                        else if ((string.Equals(q.DiscountType, "Amount", StringComparison.OrdinalIgnoreCase) || string.Equals(q.DiscountType, "Fixed", StringComparison.OrdinalIgnoreCase)) && q.DiscountValue > 0)
                        {
                            discountAmount = Math.Min(q.DiscountValue * (spaceType == "MeetingRoom" ? 1 : billingMonths), firstCycleRent);
                        }
                        else if (discountAmount > 0 && spaceType != "MeetingRoom")
                        {
                            if (discountAmount <= monthlyRent && billingMonths > 1)
                            {
                                discountAmount = Math.Min(discountAmount * billingMonths, firstCycleRent);
                            }
                            else
                            {
                                discountAmount = Math.Min(discountAmount, firstCycleRent);
                            }
                        }

                        decimal supportCharge = q.SupportChargeAmount > 0 
                            ? q.SupportChargeAmount 
                            : (spaceType == "MeetingRoom" ? Math.Round(firstCycleRent * 0.10m, 2) : 2000.00m * (q.Capacity.HasValue && q.Capacity.Value > 0 ? q.Capacity.Value : 1) * billingMonths);
                        decimal taxPct = q.AppliedTaxPercentage > 0 ? q.AppliedTaxPercentage : 16.00m;
                        decimal taxOnAdvanceRent = q.TaxAmountOnAdvanceRent > 0 
                            ? q.TaxAmountOnAdvanceRent 
                            : (q.TaxAmount > 0 ? q.TaxAmount : Math.Round(supportCharge * (taxPct / 100.0m), 2));
                        decimal initialPayable = Math.Max(0, (spaceType == "MeetingRoom" ? q.SubtotalAmount : firstCycleRent) + secDeposit + taxOnAdvanceRent - discountAmount);

                        // Dynamic Summary Ribbon
                        // col.Item().Background("#f8fafc").Border(1).BorderColor("#e2e8f0").Padding(8).Row(row =>
                        // {
                        //     if (spaceType == "MeetingRoom")
                        //     {
                        //         var timeSpan = q.EndDateTime - q.StartDateTime;
                        //         double hours = Math.Max(1, Math.Ceiling(timeSpan.TotalHours));
                        //         row.RelativeItem().Column(c =>
                        //         {
                        //             c.Item().Text("BOOKING RENT").FontSize(7.5f).Bold().FontColor("#64748b");
                        //             c.Item().Text($"PKR {q.SubtotalAmount:N0}").Bold().FontSize(9.5f).FontColor("#0f172a");
                        //             c.Item().Text("Base Booking Rent").FontSize(7f).FontColor("#64748b");
                        //         });
                        //         row.RelativeItem().Column(c =>
                        //         {
                        //             c.Item().Text("DURATION").FontSize(7.5f).Bold().FontColor("#64748b");
                        //             c.Item().Text($"{hours} Hour(s)").Bold().FontSize(9.5f).FontColor("#2563eb");
                        //             c.Item().Text("Booking Length").FontSize(7f).FontColor("#64748b");
                        //         });
                        //         row.RelativeItem().Background("#eff6ff").Padding(4).Column(c =>
                        //         {
                        //             c.Item().Text("TOTAL PAYABLE").FontSize(7.5f).Bold().FontColor("#1e40af");
                        //             c.Item().Text($"PKR {initialPayable:N0}").Bold().FontSize(9.5f).FontColor("#1e40af");
                        //             c.Item().Text("Rent + Tax").FontSize(7f).FontColor("#1e40af");
                        //         });
                        //     }
                        //     else if (spaceType == "SharedSpace")
                        //     {
                        //         // row.RelativeItem().Column(c =>
                        //         // {
                        //         //     c.Item().Text("TOTAL CONTRACT").FontSize(7.5f).Bold().FontColor("#64748b");
                        //         //     c.Item().Text($"PKR {totalContract:N0}").Bold().FontSize(9.5f).FontColor("#0f172a");
                        //         //     c.Item().Text($"{contractMonths} Month(s) Total").FontSize(7f).FontColor("#64748b");
                        //         // });
                        //         row.RelativeItem().Column(c =>
                        //         {
                        //             c.Item().Text("MONTHLY RENT").FontSize(7.5f).Bold().FontColor("#64748b");
                        //             c.Item().Text($"PKR {monthlyRent:N0}").Bold().FontSize(9.5f).FontColor("#2563eb");
                        //             c.Item().Text("per month").FontSize(7f).FontColor("#64748b");
                        //         });
                        //         // row.RelativeItem().Column(c =>
                        //         // {
                        //         //     c.Item().Text("1ST CYCLE RENT").FontSize(7.5f).Bold().FontColor("#1e40af");
                        //         //     c.Item().Text($"PKR {firstCycleRent:N0}").Bold().FontSize(9.5f).FontColor("#1d4ed8");
                        //         //     c.Item().Text($"First {billingMonths} Months").FontSize(7f).FontColor("#1e40af");
                        //         // });
                        //         // row.RelativeItem().Background("#eff6ff").Padding(4).Column(c =>
                        //         // {
                        //         //     c.Item().Text("1ST CYCLE PAYABLE").FontSize(7.5f).Bold().FontColor("#1e40af");
                        //         //     c.Item().Text($"PKR {initialPayable:N0}").Bold().FontSize(9.5f).FontColor("#1e40af");
                        //         //     c.Item().Text("Rent + Tax").FontSize(7f).FontColor("#1e40af");
                        //         // });
                        //     }
                        //     else // PrivateRoom
                        //     {
                        //         // row.RelativeItem().Column(c =>
                        //         // {
                        //         //     c.Item().Text("TOTAL CONTRACT").FontSize(7.5f).Bold().FontColor("#64748b");
                        //         //     c.Item().Text($"PKR {totalContract:N0}").Bold().FontSize(9.5f).FontColor("#0f172a");
                        //         //     c.Item().Text($"{contractMonths} Month(s) Total").FontSize(7f).FontColor("#64748b");
                        //         // });
                        //         row.RelativeItem().Column(c =>
                        //         {
                        //             c.Item().Text("Private Office Charges").FontSize(7.5f).Bold().FontColor("#64748b");
                        //             c.Item().Text($"PKR {monthlyRent:N0}").Bold().FontSize(9.5f).FontColor("#2563eb");
                        //             c.Item().Text("per month").FontSize(7f).FontColor("#64748b");
                        //         });
                        //         row.RelativeItem().Column(c =>
                        //         {
                        //             c.Item().Text("SECURITY DEPOSIT").FontSize(7.5f).Bold().FontColor("#64748b");
                        //             c.Item().Text($"PKR {secDeposit:N0}").Bold().FontSize(9.5f).FontColor("#d97706");
                        //             c.Item().Text("Refundable").FontSize(7f).FontColor("#64748b");
                        //         });
                        //         // row.RelativeItem().Background("#eff6ff").Padding(4).Column(c =>
                        //         // {
                        //         //     c.Item().Text("1ST CYCLE PAYABLE").FontSize(7.5f).Bold().FontColor("#1e40af");
                        //         //     c.Item().Text($"PKR {initialPayable:N0}").Bold().FontSize(9.5f).FontColor("#1e40af");
                        //         //     c.Item().Text("Rent + Deposit + Tax").FontSize(7f).FontColor("#1e40af");
                        //         // });
                        //     }
                        // });

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(35);
                                cols.RelativeColumn(4);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                var durationHeader = spaceType == "MeetingRoom" ? "Duration" : "No. of Months";
                                foreach (var h in new[] { "S.No", "Description", durationHeader, "Unit Price", "Amount" })
                                {
                                    var cell = header.Cell().Border(1).BorderColor("#000000").Background(Colors.White).Padding(6);
                                    if (h == durationHeader || h == "Unit Price" || h == "Amount")
                                    {
                                        cell.AlignRight().Text(h).FontColor("#000000").Bold().FontSize(9);
                                    }
                                    else
                                    {
                                        cell.Text(h).FontColor("#000000").Bold().FontSize(9);
                                    }
                                }
                            });

                            bool alt = false;
                            int sno = 1;
                            if (q.Details != null && q.Details.Count > 0)
                            {
                                foreach (var d in q.Details)
                                {
                                    var bg = alt ? "#f9f9f9" : "#ffffff";
                                    table.Cell().Background(bg).Padding(6).Text(sno++.ToString());

                                    bool isSecurityDeposit = string.Equals(d.FeeType, "SecurityDeposit", StringComparison.OrdinalIgnoreCase) || 
                                                             (d.Description != null && d.Description.Contains("Security Deposit", StringComparison.OrdinalIgnoreCase));

                                    string desc = d.Description ?? "";
                                    if (!isSecurityDeposit)
                                    {
                                        if (!desc.Contains("(including support service charges)", StringComparison.OrdinalIgnoreCase))
                                        {
                                            string baseName = !string.IsNullOrWhiteSpace(q.SpaceTypeName) ? q.SpaceTypeName.Trim() : (!string.IsNullOrWhiteSpace(desc) ? desc.Trim() : "Facility");
                                            if (baseName.EndsWith(" Rent", StringComparison.OrdinalIgnoreCase))
                                            {
                                                baseName = baseName.Substring(0, baseName.Length - 5).Trim();
                                            }
                                            if (!baseName.Contains("facility", StringComparison.OrdinalIgnoreCase))
                                            {
                                                desc = $"{baseName} facility (including support service charges)";
                                            }
                                            else
                                            {
                                                desc = $"{baseName} (including support service charges)";
                                            }
                                        }
                                    }

                                    table.Cell().Background(bg).Padding(6).Text(desc);

                                    string qtyLabel;
                                    decimal unitPrice = d.UnitPrice > 0 ? d.UnitPrice : monthlyRent;
                                    decimal lineAmount = d.Amount;

                                    if (spaceType == "MeetingRoom")
                                    {
                                        qtyLabel = $"{d.Quantity:N0} Hour(s)";
                                    }
                                    else
                                    {
                                        if (isSecurityDeposit)
                                        {
                                            decimal qty = d.Quantity > 0 ? d.Quantity : (q.SecurityDepositMonths > 0 ? q.SecurityDepositMonths : 1);
                                            qtyLabel = $"{qty:G29}";
                                            unitPrice = d.UnitPrice > 0 ? d.UnitPrice : (qty > 0 ? d.Amount / qty : (monthlyRent > 0 ? monthlyRent : secDeposit));
                                            lineAmount = d.Amount > 0 ? d.Amount : secDeposit;
                                        }
                                        else
                                        {
                                            int months = billingMonths;
                                            qtyLabel = $"{months}";
                                            if (unitPrice <= 0) unitPrice = monthlyRent;
                                            lineAmount = Math.Round(unitPrice * months, 2);
                                        }
                                    }

                                    table.Cell().Background(bg).Padding(6).AlignRight().Text(qtyLabel);
                                    table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {unitPrice:N0}");
                                    table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {lineAmount:N0}");
                                    alt = !alt;
                                }
                            }
                            else
                            {
                                var bg = "#ffffff";
                                table.Cell().Background(bg).Padding(6).Text("1");

                                string fallbackName = !string.IsNullOrWhiteSpace(q.SpaceTypeName) ? q.SpaceTypeName : "Space";
                                if (fallbackName.EndsWith(" Rent", StringComparison.OrdinalIgnoreCase))
                                {
                                    fallbackName = fallbackName.Substring(0, fallbackName.Length - 5);
                                }
                                string fallbackDesc = fallbackName.Contains("facility", StringComparison.OrdinalIgnoreCase)
                                    ? $"{fallbackName} (including support service charges)"
                                    : $"{fallbackName} facility (including support service charges)";

                                table.Cell().Background(bg).Padding(6).Text(fallbackDesc);
                                if (spaceType == "MeetingRoom")
                                {
                                    var timeSpan = q.EndDateTime - q.StartDateTime;
                                    double hours = Math.Max(1, Math.Ceiling(timeSpan.TotalHours));
                                    table.Cell().Background(bg).Padding(6).AlignRight().Text($"{hours} Hour(s)");
                                }
                                else
                                {
                                    table.Cell().Background(bg).Padding(6).AlignRight().Text($"{billingMonths}");
                                }
                                table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {monthlyRent:N0}");
                                table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {firstCycleRent:N0}");
                            }
                        });

                        col.Item().AlignRight().Column(c =>
                        {
                            c.Spacing(3);
                            if (spaceType != "MeetingRoom")
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text($"Advance ({billingMonths} Mos):");
                                    r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {firstCycleRent:N0}").Bold();
                                });
                                if (secDeposit > 0)
                                {
                                    c.Item().Row(r =>
                                    {
                                        r.ConstantItem(220).AlignRight().Text("Security Deposit:");
                                        r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {secDeposit:N0}").Bold().FontColor("#000000");
                                    });
                                }
                            }
                            else
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text("Booking Rent Amount:");
                                    r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {q.SubtotalAmount:N0}").Bold();
                                });
                            }

                            if (taxOnAdvanceRent > 0)
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text($"PST on Support Services ({taxPct:G29}%):").FontColor("#000000");
                                    r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {taxOnAdvanceRent:N0}").FontColor("#000000");
                                });
                            }
                            if (discountAmount > 0)
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text("Discount:").FontColor("#000000");
                                    r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"- PKR {discountAmount:N0}").FontColor("#000000");
                                });
                            }
                            c.Item().LineHorizontal(1).LineColor("#000000");
                            c.Item().Row(r =>
                            {
                                r.ConstantItem(220).AlignRight().Text("TOTAL INITIAL AMOUNT PAYABLE:").Bold().FontSize(11);
                                r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {initialPayable:N0}").Bold().FontSize(11).FontColor("#000000");
                            });

                            if (q.SendWhtInvoice && q.WhtAmount > 0)
                            {
                                decimal rawWhtTotal = q.WhtRatePercent > 0 ? q.WhtRatePercent : (q.WithholdingTaxRate > 0 ? q.WithholdingTaxRate : 15.00m);
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text($"Less Withholding Tax ({rawWhtTotal:G29}%):").FontColor("#000000");
                                    r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"- PKR {q.WhtAmount:N0}").FontColor("#000000");
                                });
                                c.Item().LineHorizontal(1).LineColor("#000000");
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text("NET PAYABLE AMOUNT:").Bold().FontSize(11);
                                    r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {Math.Max(0, initialPayable - q.WhtAmount):N0}").Bold().FontSize(11).FontColor("#000000");
                                });
                            }
                        });

                        if (!string.IsNullOrWhiteSpace(q.Remarks))
                        {
                            col.Item().Background("#f5f5f5").Padding(10).Column(c =>
                            {
                                c.Item().Text("Remarks").Bold().FontSize(9).FontColor("#000000");
                                c.Item().Text(q.Remarks);
                            });
                        }

                        col.Item().PaddingTop(8).Border(1).BorderColor("#dee2e6").Background("#f8f9fa").Padding(10).Column(tc =>
                        {
                            tc.Item().Text("Terms & Conditions").Bold().FontSize(9).FontColor("#495057");
                            tc.Spacing(2);
                            int itemNum = 1;
                            tc.Item().Text($"{itemNum++}. The price is exclusive of all tax.").FontSize(8).FontColor("#495057");
                            tc.Item().Text($"{itemNum++}. The {spaceType} facility charges are inclusive of support service charges of PKR {supportCharge:N0}.").FontSize(8).FontColor("#495057");
                            tc.Item().Text($"{itemNum++}. WorkNest will charge Provincial sales tax on support service.").FontSize(8).FontColor("#495057");
                            tc.Item().Text($"{itemNum++}. This quotation is valid until the date specified above. Prices are subject to change after expiry.").FontSize(8).FontColor("#495057");
                            if (spaceType == "PrivateRoom" || secDeposit > 0)
                            {
                                tc.Item().Text($"{itemNum++}. Security deposit is fully refundable upon termination of the agreement, subject to terms of agreement.").FontSize(8).FontColor("#495057");
                            }
                            if (spaceType != "MeetingRoom")
                            {
                                tc.Item().Text($"{itemNum++}. {spaceType} facility charges will be paid in advance for {billingMonths} month(s).").FontSize(8).FontColor("#495057");
                            }
                            tc.Item().Text($"{itemNum++}. Booking confirmation is subject to space availability at the time of payment.").FontSize(8).FontColor("#495057");
                            tc.Item().Text($"{itemNum++}. WorkNest reserves the right to modify pricing and terms with prior notice.").FontSize(8).FontColor("#495057");

                            decimal? SecurityDeposit = secDeposit > 0 ? secDeposit : null;
                            decimal serviceChargeAmount = q.SupportChargeAmount > 0 
                                ? q.SupportChargeAmount 
                                : (spaceType == "MeetingRoom" 
                                    ? Math.Round(((q.SubtotalAmount - discountAmount) * ((q.AppliedChargePercentage > 0 ? q.AppliedChargePercentage : 10.00m) / 100.0m)), 2) 
                                    : (2000.00m * (q.Capacity.HasValue && q.Capacity.Value > 0 ? q.Capacity.Value : 1) * billingMonths));
                            decimal ServiceCharges = serviceChargeAmount;
                            decimal RoomRent = Math.Max(0, ((spaceType == "MeetingRoom" ? q.SubtotalAmount : firstCycleRent) - discountAmount) - ServiceCharges);
                            decimal SalesTax = taxOnAdvanceRent > 0 ? taxOnAdvanceRent : (q.TaxAmount > 0 ? q.TaxAmount : Math.Round(ServiceCharges * (taxPct / 100.0m), 2));
                            decimal rawWht = q.WhtRatePercent > 0 ? q.WhtRatePercent : (q.WithholdingTaxRate > 0 ? q.WithholdingTaxRate : 15.00m);   // percentage, not the WN_WHTaxRate Id
                            decimal WithholdingTaxRate = rawWht > 1m ? (rawWht / 100.0m) : rawWht;

                            decimal securityDeposit = SecurityDeposit ?? 0m;

                            if (q.SendWhtInvoice)
                            {
                                decimal whtAmt = q.WhtAmount > 0 ? q.WhtAmount : Math.Round((spaceType == "MeetingRoom" ? q.SubtotalAmount : firstCycleRent) * WithholdingTaxRate, 2);
                                decimal netPayable = Math.Max(0, initialPayable - whtAmt);
                                tc.Item().Text($"{itemNum++}. This quotation is grossed up for Withholding Tax (WHT) at {rawWht:G29}%. The customer shall withhold PKR {whtAmt:N0} and pay net PKR {netPayable:N0}.").FontSize(8).Bold().FontColor("#495057");
                                if (SecurityDeposit.HasValue && securityDeposit > 0)
                                {
                                    tc.Item().Text($"{itemNum++}. Withholding tax is not applicable on the Security Deposit.").FontSize(8).Bold().FontColor("#495057");
                                }
                            }
                            else
                            {
                                decimal roomRentNet = RoomRent + ServiceCharges;
                                decimal grossRentOnly = WithholdingTaxRate < 1m ? Math.Round(roomRentNet / (1m - WithholdingTaxRate), 2, MidpointRounding.AwayFromZero) : roomRentNet;
                                decimal grossedUpTotal = grossRentOnly + SalesTax + securityDeposit;

                                tc.Item().Text($"{itemNum++}. If tax is withheld, the customer shall pay PKR {grossedUpTotal:N0}").FontSize(8).Bold().FontColor("#495057");
                                if (SecurityDeposit.HasValue && securityDeposit > 0)
                                {
                                    tc.Item().Text($"{itemNum++}. Withholding tax is not applicable on the Security Deposit.").FontSize(8).Bold().FontColor("#495057");
                                }
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
                                inner.Item().Text("CUSTOMER").FontSize(9).Bold().FontColor("#000000");
                                inner.Item().Text("Attn: " + (c.CustomerName ?? "-")).FontColor("#000000");
                                inner.Item().Text("Company: " + (c.CustomerCompany ?? "-")).FontColor("#000000");
                                inner.Item().Text("Email: " + (c.CustomerEmail ?? "-")).FontColor("#555555");
                                inner.Item().Text("Address: " + (c.CustomerAddress ?? "-")).FontColor("#555555");
                            });
                            row.RelativeItem().Column(inner =>
                            {
                                inner.Item().Text("SPACE").FontSize(9).Bold().FontColor("#000000");
                                inner.Item().Text(c.SpaceName ?? c.SpaceCode ?? "-").Bold();
                                inner.Item().Text(c.SpaceTypeName ?? "-").FontColor("#555555");
                                string cityName = !string.IsNullOrWhiteSpace(c.CityName) ? c.CityName : "Islamabad";
                                string locName = c.LocationName ?? "";
                                string locationDisplay = locName;
                                if (!string.IsNullOrWhiteSpace(cityName) && !locName.Contains(cityName, StringComparison.OrdinalIgnoreCase))
                                {
                                    locationDisplay = string.IsNullOrWhiteSpace(locName) ? cityName : $"{locName}, {cityName}";
                                }
                                if (string.IsNullOrWhiteSpace(locationDisplay)) locationDisplay = "-";
                                inner.Item().Text(locationDisplay).FontColor("#555555");
                            });
                            row.RelativeItem().Column(inner =>
                            {
                                inner.Item().AlignRight().Text("BOOKING DETAILS").FontSize(9).Bold().FontColor("#000000");
                                if (spaceType == "MeetingRoom")
                                {
                                    inner.Item().AlignRight().Text($"From: {c.StartOn:dd MMM yyyy hh:mm tt}");
                                    inner.Item().AlignRight().Text($"To:   {c.EndOn:dd MMM yyyy hh:mm tt}");
                                    if (!string.IsNullOrWhiteSpace(c.TimeSlot))
                                    {
                                        inner.Item().AlignRight().Text($"Slot: {c.TimeSlot}").FontColor("#555555");
                                    }
                                }
                                else
                                {
                                    inner.Item().AlignRight().Text($"From: {c.StartOn:dd MMM yyyy}");
                                    inner.Item().AlignRight().Text($"To:   {c.EndOn:dd MMM yyyy}");
                                    inner.Item().AlignRight().Text($"Billing: {c.BillingPeriodLabel ?? c.BillingPeriodCode ?? "-"}").FontColor("#555555");
                                    if (c.NextBillDueDate.HasValue)
                                    {
                                        inner.Item().AlignRight().Text($"Next Bill Due: {c.NextBillDueDate:dd MMM yyyy}").FontColor("#0284c7");
                                    }
                                }
                            });
                        });

                        col.Item().LineHorizontal(1).LineColor("#e0e0e0");

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(35);
                                cols.RelativeColumn(3);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                foreach (var h in new[] { "S.No", "Description", "Office Number", "Unit Price", "Add Ons", "Discount", "Total" })
                                {
                                    var cell = header.Cell().Border(1).BorderColor("#000000").Background(Colors.White).Padding(6);
                                    if (h == "Office Number" || h == "Unit Price" || h == "Add Ons" || h == "Discount" || h == "Total")
                                    {
                                        cell.AlignRight().Text(h).FontColor("#000000").Bold().FontSize(9);
                                    }
                                    else
                                    {
                                        cell.Text(h).FontColor("#000000").Bold().FontSize(9);
                                    }
                                }
                            });

                            bool alt = false;
                            int sno = 1;
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
                                table.Cell().Background(bg).Padding(6).Text(sno++.ToString());
                                table.Cell().Background(bg).Padding(6).Text(desc);
                                table.Cell().Background(bg).Padding(6).AlignRight().Text(officeNo);
                                table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {line.UnitPrice:N0}");
                                table.Cell().Background(bg).Padding(6).AlignRight().Text("-");
                                table.Cell().Background(bg).Padding(6).AlignRight().Text(line.DiscountAmount > 0 ? $"PKR {line.DiscountAmount:N0}" : "-");
                                table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {line.LineTotal:N0}");
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
                                    r.ConstantItem(220).AlignRight().Text($"Advance ({c.BillingPeriodMonths} Month(s)):");
                                    r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {c.CurrentCycleAmount:N0}");
                                });
                                if (spaceType == "PrivateRoom" && c.SecurityDeposit > 0)
                                {
                                    inner.Item().Row(r =>
                                    {
                                        r.ConstantItem(220).AlignRight().Text("Security Deposit:");
                                        r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {c.SecurityDeposit:N0}").FontColor("#d97706");
                                    });
                                }
                            }
                            else
                            {
                                inner.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text("Booking Rent Amount:");
                                    r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {c.SubtotalAmount:N0}").Bold();
                                });
                            }

                            if (c.TaxAmount > 0)
                            {
                                inner.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text($"PST on Support Services ({(c.AppliedTaxPercentage > 0 ? c.AppliedTaxPercentage : 16.00m):G29}%):" ).FontColor("#000000ff");
                                    r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {c.TaxAmount:N0}").FontColor("#000000ff");
                                });
                            }
                            if (c.DiscountAmount > 0)
                            {
                                inner.Item().Row(r =>
                                {
                                    r.ConstantItem(220).AlignRight().Text($"Discount:").FontColor("#e74c3c");
                                    r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"- PKR {c.DiscountAmount:N0}").FontColor("#e74c3c");
                                });
                            }
                            inner.Item().LineHorizontal(1).LineColor("#000000");
                            inner.Item().Row(r =>
                            {
                                r.ConstantItem(220).AlignRight().Text("TOTAL INITIAL AMOUNT PAYABLE:").Bold().FontSize(11);
                                r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {c.TotalPayable:N0}").Bold().FontSize(11).FontColor("#000000");
                            });
                        });
                        if (!string.IsNullOrWhiteSpace(c.ChallanNotes))
                        {
                            col.Item().Background("#f5f5f5").Padding(10).Column(inner =>
                            {
                                inner.Item().Text("Notes").Bold().FontSize(9).FontColor("#000000");
                                inner.Item().Text(c.ChallanNotes);
                            });
                        }

                        col.Item().PaddingTop(8).Border(1).BorderColor("#dee2e6").Background("#f8f9fa").Padding(10).Column(tc =>
                        {
                            tc.Item().Text("Terms & Conditions").Bold().FontSize(9).FontColor("#495057");
                            tc.Spacing(2);
                            int itemNum = 1;

                            int billingMonths = c.BillingPeriodMonths > 0 ? c.BillingPeriodMonths : (c.NumberOfMonths > 0 ? c.NumberOfMonths : 1);
                            decimal firstCycleRent = c.CurrentCycleAmount > 0 ? c.CurrentCycleAmount : (c.FirstCycleRent > 0 ? c.FirstCycleRent : c.SubtotalAmount);
                            decimal secDeposit = c.SecurityDeposit > 0 ? c.SecurityDeposit : 0m;
                            decimal discountAmount = c.DiscountAmount;
                            decimal taxPct = c.AppliedTaxPercentage > 0 ? c.AppliedTaxPercentage : 16.00m;

                            decimal serviceChargeAmount = c.SupportChargeAmount > 0 
                                ? c.SupportChargeAmount 
                                : (spaceType == "MeetingRoom" 
                                    ? Math.Round(((c.SubtotalAmount - discountAmount) * 0.10m), 2) 
                                    : (2000.00m * (c.SpaceCapacity > 0 ? c.SpaceCapacity : 1) * billingMonths));
                            decimal supportCharge = serviceChargeAmount;
                            decimal ServiceCharges = serviceChargeAmount;
                            decimal RoomRent = Math.Max(0, ((spaceType == "MeetingRoom" ? c.SubtotalAmount : firstCycleRent) - discountAmount) - ServiceCharges);
                            decimal taxOnAdvanceRent = c.TaxAmountOnAdvanceRent > 0 ? c.TaxAmountOnAdvanceRent : (c.TaxAmount > 0 ? c.TaxAmount : Math.Round(ServiceCharges * (taxPct / 100.0m), 2));
                            decimal SalesTax = taxOnAdvanceRent;
                            decimal rawWht = c.WithholdingTaxRate > 0 ? c.WithholdingTaxRate : 15.00m;
                            decimal WithholdingTaxRate = rawWht > 1m ? (rawWht / 100.0m) : rawWht;

                            decimal roomRentNet = RoomRent + ServiceCharges;
                            decimal grossRentOnly = WithholdingTaxRate < 1m ? Math.Round(roomRentNet / (1m - WithholdingTaxRate), 2, MidpointRounding.AwayFromZero) : roomRentNet;
                            decimal grossedUpTotal = grossRentOnly + SalesTax + secDeposit;

                            tc.Item().Text($"{itemNum++}. The price is exclusive of all tax.").FontSize(8).FontColor("#495057");
                            tc.Item().Text($"{itemNum++}. The {spaceType} facility charges are inclusive of support service charges of PKR {supportCharge:N0}.").FontSize(8).FontColor("#495057");
                            tc.Item().Text($"{itemNum++}. WorkNest will charge Provincial sales tax on support service.").FontSize(8).FontColor("#495057");
                            tc.Item().Text($"{itemNum++}. This booking confirmation is valid until the date specified above. Prices are subject to change after expiry.").FontSize(8).FontColor("#495057");
                            if (spaceType == "PrivateRoom" || secDeposit > 0)
                            {
                                tc.Item().Text($"{itemNum++}. Security deposit is fully refundable upon termination of the agreement, subject to terms of agreement.").FontSize(8).FontColor("#495057");
                            }
                            if (spaceType != "MeetingRoom")
                            {
                                tc.Item().Text($"{itemNum++}. {spaceType} facility charges will be paid in advance for {billingMonths} month(s).").FontSize(8).FontColor("#495057");
                            }
                            tc.Item().Text($"{itemNum++}. Booking confirmation is subject to space availability at the time of payment.").FontSize(8).FontColor("#495057");
                            tc.Item().Text($"{itemNum++}. WorkNest reserves the right to modify pricing and terms with prior notice.").FontSize(8).FontColor("#495057");

                            tc.Item().Text($"{itemNum++}. If tax is withheld, the customer shall pay PKR {grossedUpTotal:N0}").FontSize(8).Bold().FontColor("#495057");
                            if (secDeposit > 0)
                            {
                                tc.Item().Text($"{itemNum++}. Withholding tax is not applicable on the Security Deposit.").FontSize(8).Bold().FontColor("#495057");
                            }
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
                                c.Item().AlignRight().Text($"Due Date: {(inv.DueOn.HasValue ? inv.DueOn.Value : inv.StartOn):dd MMM yyyy}").Bold().FontColor("#1a1a2e");
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
                                c.Item().Text("CUSTOMER").FontSize(9).Bold().FontColor("#000000");
                                c.Item().Text(inv.CustomerName ?? "-").Bold();
                                c.Item().Text(inv.CustomerEmail ?? "-").FontColor("#555555");
                                if (!string.IsNullOrWhiteSpace(inv.CustomerCode))
                                    c.Item().Text($"Code: {inv.CustomerCode}").FontColor("#555555");
                            });
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("SPACE").FontSize(9).Bold().FontColor("#000000");
                                c.Item().Text(inv.SpaceName ?? "-").Bold();
                                c.Item().Text(inv.SpaceTypeName ?? "-").FontColor("#555555");
                                c.Item().Text(inv.LocationName ?? "-").FontColor("#555555");
                            });
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("BOOKING PERIOD").FontSize(9).Bold().FontColor("#000000");
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
                                {
                                    var cell = header.Cell().Border(1).BorderColor("#000000").Background(Colors.White).Padding(6);
                                    if (h == "Amount (PKR)")
                                    {
                                        cell.AlignRight().Text(h).FontColor("#000000").Bold().FontSize(9);
                                    }
                                    else
                                    {
                                        cell.Text(h).FontColor("#000000").Bold().FontSize(9);
                                    }
                                }
                            });

                            bool alt = false;
                            if (inv.MonthsBreakdown != null && inv.MonthsBreakdown.Count > 0)
                            {
                                foreach (var m in inv.MonthsBreakdown)
                                {
                                    var bg = alt ? "#f9f9f9" : "#ffffff";
                                    table.Cell().Background(bg).Padding(6).Text($"Advance - {m.MonthName}");
                                    table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {m.Amount:N0}");
                                    alt = !alt;
                                }
                            }
                            else
                            {
                                var bg = "#ffffff";
                                table.Cell().Background(bg).Padding(6).Text($"Advance ({inv.AdvanceRentMonths} Month(s))");
                                table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {inv.AdvanceRentTotal:N0}");
                                alt = true;
                            }

                            if (inv.SecurityDepositTotal > 0)
                            {
                                var bg = alt ? "#f9f9f9" : "#ffffff";
                                table.Cell().Background(bg).Padding(6).Text($"Security Deposit ({inv.SecurityDepositMonths} Month(s))");
                                table.Cell().Background(bg).Padding(6).AlignRight().Text($"PKR {inv.SecurityDepositTotal:N0}");
                                alt = !alt;
                            }
                        });

                        col.Item().AlignRight().Column(c =>
                        {
                            c.Spacing(3);
                            c.Item().Row(r =>
                            {
                                r.ConstantItem(160).AlignRight().Text("Advance:");
                                r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {inv.AdvanceRentTotal:N0}");
                            });
                            if (inv.SecurityDepositTotal > 0)
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(160).AlignRight().Text("Security Deposit:");
                                    r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {inv.SecurityDepositTotal:N0}");
                                });
                            }
                            if (inv.TaxTotal > 0)
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(160).AlignRight().Text($"Provincial Sales Tax ({inv.AppliedTaxPercentage:G29}%):").FontColor("#15803d");
                                    r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {inv.TaxTotal:N0}").FontColor("#15803d");
                                });
                            }
                            if (inv.DiscountAmount > 0)
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(160).AlignRight().Text("Discount:").FontColor("#e74c3c");
                                    r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"- PKR {inv.DiscountAmount:N0}").FontColor("#e74c3c");
                                });
                            }
                            c.Item().LineHorizontal(1).LineColor("#000000");
                            c.Item().Row(r =>
                            {
                                r.ConstantItem(160).AlignRight().Text("Total Payable:").Bold().FontSize(12);
                                r.ConstantItem(120).PaddingRight(6).AlignRight().Text($"PKR {inv.TotalPayable:N0}").Bold().FontSize(12).FontColor("#000000");
                            });
                        });

                        if (!string.IsNullOrWhiteSpace(inv.Notes))
                        {
                            col.Item().Background("#f5f5f5").Padding(10).Column(c =>
                            {
                                c.Item().Text("Notes").Bold().FontSize(9).FontColor("#000000");
                                c.Item().Text(inv.Notes);
                            });
                        }

                        col.Item().PaddingTop(8).Border(1).BorderColor("#dee2e6").Background("#f8f9fa").Padding(10).Column(tc =>
                        {
                            tc.Item().Text("Terms & Conditions").Bold().FontSize(9).FontColor("#495057");
                            tc.Spacing(2);
                            int itemNum = 1;
                            tc.Item().Text($"{itemNum++}. The price includes 10% support services and Worknest will charge Provincial sales tax on this service.").FontSize(8).FontColor("#495057");
                            if (inv.SecurityDepositTotal > 0)
                            {
                                tc.Item().Text($"{itemNum++}. Security deposit is fully refundable upon termination, subject to lease terms.").FontSize(8).FontColor("#495057");
                            }
                            tc.Item().Text($"{itemNum++}. WorkNest reserves the right to modify pricing and terms with prior notice.").FontSize(8).FontColor("#495057");

                            decimal rawWht = inv.WithholdingTaxRate > 0 ? inv.WithholdingTaxRate : 15.00m;
                            decimal whtRate = rawWht > 1m ? (rawWht / 100.0m) : rawWht;
                            decimal taxableBase = Math.Max(0, inv.TotalPayable - inv.SecurityDepositTotal);
                            decimal grossedUpRent = whtRate < 1m ? taxableBase / (1 - whtRate) : taxableBase;
                            decimal grossedUpTotal = grossedUpRent + inv.SecurityDepositTotal;

                            tc.Item().Text($"{itemNum++}. If tax is withheld, the customer shall pay PKR {grossedUpTotal:N0}").FontSize(8).Bold().FontColor("#495057");
                            if (inv.SecurityDepositTotal > 0)
                            {
                                tc.Item().Text($"{itemNum++}. Withholding tax is not applicable on the Security Deposit.").FontSize(8).Bold().FontColor("#495057");
                            }
                        });
                    });

                    page.Footer().Element(ComposeFooter);
                });
            }).GeneratePdf();
        }

        private static string GetLogoPath()
        {
            var paths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "images", "Logo_black.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "Logo_black.png"),
                @"E:\WN_APIs\WorkNest.API\wwwroot\images\Logo_black.png",
                @"F:\WN_APIs\WorkNest.API\wwwroot\images\Logo_black.png",
                @"E:\WorkNest_FE\public\images\Logo_black.png",
                @"F:\WorkNest_FE\public\images\Logo_black.png",
                @"E:\WorkNest_FE\public\images\Logo.png",
                @"F:\WorkNest_FE\public\images\Logo.png"
            };
            foreach (var p in paths)
            {
                if (File.Exists(p)) return p;
            }
            return @"F:\WorkNest_FE\public\images\Logo.png";
        }

        private static void ComposeHeader(IContainer container)
        {
            string logoPath = GetLogoPath();
            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Column(c =>
                    {
                        if (File.Exists(logoPath))
                        {
                            c.Item().Row(r =>
                            {
                                r.AutoItem().Height(40).Image(logoPath).FitHeight();
                                r.AutoItem().AlignMiddle().PaddingLeft(2).Text("orkNest").FontSize(25).Bold().FontColor("#000000");
                            });
                        }
                        else
                        {
                            c.Item().Text("WorkNest").FontSize(22).Bold().FontColor("#000000");
                        }
                    });

                    row.ConstantItem(240).Column(c =>
                    {
                        c.Item().AlignRight().Text("3rd Floor EOBI Building-II, I-8 Markaz").FontSize(8.5f).FontColor("#475569");
                        c.Item().AlignRight().Text("Islamabad, Pakistan").FontSize(8.5f).FontColor("#475569");
                        c.Item().AlignRight().Text("Phone: +92 328 0256000 / +92 320 1809696").FontSize(8.5f).FontColor("#475569");
                        c.Item().AlignRight().Text("Email: sales@worknestpk.com").FontSize(8.5f).FontColor("#475569");
                    });
                });
                col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor("#000000");
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

        public byte[] GenerateSalesTaxInvoicePdf(WorkNest.Application.DTOs.Payment.CustomerSTInvoiceDto dto)
        {
            return SalesTaxInvoicePdfGenerator.GeneratePdf(dto);
        }

        public byte[] GenerateAgreementPdf(WorkNest.Application.DTOs.Agreement.SendAgreementRequest request, string quotationNumber)
        {
            return AgreementPdfGenerator.Generate(request, quotationNumber);
        }

        public byte[] GenerateESignatureCertificatePdf(WorkNest.Application.DTOs.Agreement.AgreementESignatureEvidence evidence)
        {
            return ESignatureCertificatePdfGenerator.Generate(evidence);
        }
    }
}
