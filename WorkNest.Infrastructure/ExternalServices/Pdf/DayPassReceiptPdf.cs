using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkNest.Application.DTOs.DayPass;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.Pdf
{
    /// <summary>The receipt of a day pass (A5): who paid what for which space, day and hours.</summary>
    public class DayPassReceiptPdf : IDayPassReceiptPdf
    {
        static DayPassReceiptPdf() { QuestPDF.Settings.License = LicenseType.Community; }

        private static string M(decimal v) => "PKR " + v.ToString("N2", CultureInfo.InvariantCulture);

        public byte[] Generate(DayPassDto p)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A5);
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial).FontColor("#1f2937"));
                    page.Header().Column(col =>
                    {
                        col.Item().Text("WorkNest").FontSize(18).Bold();
                        col.Item().Text("Receipt " + p.ReceiptNumber).FontSize(12).SemiBold();
                        col.Item().Text($"Day pass {p.PassNumber}   |   Invoice {p.InvoiceNumber}").FontColor("#64748b");
                        col.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor("#cbd5e1");
                    });
                    page.Content().Column(col =>
                    {
                        void Row(string k, string v) => col.Item().PaddingVertical(2).Row(r =>
                        {
                            r.RelativeItem(2).Text(k).FontColor("#64748b");
                            r.RelativeItem(4).AlignRight().Text(v);
                        });
                        Row("Received from", p.CustomerName + " (" + p.CustomerCode + ")");
                        Row("Location", p.LocationName);
                        Row("Space", p.SpaceName);
                        Row("Date", p.Date);
                        Row("Hours", p.WindowLabel);
                        Row("Seats", p.WholeSpace ? "Whole space" : p.Seats.ToString(CultureInfo.InvariantCulture));
                        col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor("#cbd5e1");
                        Row("Amount", M(p.SubTotal));
                        if (p.Tax > 0) Row($"Sales tax ({p.TaxPercent:0.##}%)", M(p.Tax));
                        col.Item().PaddingVertical(2).Row(r =>
                        {
                            r.RelativeItem(2).Text("Total received").Bold();
                            r.RelativeItem(4).AlignRight().Text(M(p.Total)).Bold();
                        });
                        col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor("#cbd5e1");
                        Row("Payment", p.PaymentMethod == "bank" ? $"Bank transfer - {p.BankAccountName}" : "Cash");
                        if (p.PaymentMethod == "bank") Row("Reference", p.PaymentReference ?? "");
                        Row("Door access", p.AccessMode == "cnic" ? "By CNIC (people added in the app)" : "Reception");
                        Row("Received by", p.CreatedByName + ", " + p.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                        if (p.Status == "Cancelled") col.Item().PaddingTop(8).Text("CANCELLED - " + p.CancelReason).Bold().FontColor("#b91c1c");
                    });
                    page.Footer().AlignCenter().Text("Thank you for choosing WorkNest.").FontSize(8).FontColor("#94a3b8");
                });
            }).GeneratePdf();
        }
    }
}
