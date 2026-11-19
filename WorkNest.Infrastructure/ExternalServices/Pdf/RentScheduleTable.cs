using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using WorkNest.Application.DTOs.Quotation;

namespace WorkNest.Infrastructure.ExternalServices.Pdf
{
    /// <summary>The rent schedule as a printed table (quotation PDF, agreement page 1): one row per month and a totals row.</summary>
    public static class RentScheduleTable
    {
        public static string Money(decimal v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The cell texts of one month (also used by the tests): month, period, rent, discount, after discount, deposit, tax, total.</summary>
        public static string[] RowCells(ScheduleRowDto r) => new[]
        {
            r.RowNo.ToString(), r.Label, Money(r.MonthlyRent), r.DiscountAmount > 0m ? Money(r.DiscountAmount) : "-",
            Money(r.RentAfterDiscount), r.SecurityDeposit is > 0m ? Money(r.SecurityDeposit.Value) : "-", r.Tax > 0m ? Money(r.Tax) : "-", Money(r.Total)
        };

        public static string[] TotalsCells(ScheduleTotalsDto t) => new[]
        {
            "", "Total", Money(t.MonthlyRent), t.DiscountAmount > 0m ? Money(t.DiscountAmount) : "-",
            Money(t.RentAfterDiscount), t.SecurityDeposit > 0m ? Money(t.SecurityDeposit) : "-", t.Tax > 0m ? Money(t.Tax) : "-", Money(t.Total)
        };

        public static void Compose(IContainer container, RentScheduleDto schedule, float fontSize = 8f)
        {
            string[] heads = { "#", "Period", "Monthly rent", "Discount", "Rent after discount", "Security deposit", "Tax", "Total" };
            container.Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(22);
                    cols.RelativeColumn(2.2f);
                    for (int i = 0; i < 6; i++) cols.RelativeColumn(1.6f);
                });
                table.Header(h =>
                {
                    for (int i = 0; i < heads.Length; i++)
                    {
                        var cell = h.Cell().Border(0.5f).BorderColor("#000000").Padding(4);
                        if (i >= 2) cell.AlignRight().Text(heads[i]).Bold().FontSize(fontSize);
                        else cell.Text(heads[i]).Bold().FontSize(fontSize);
                    }
                });
                bool alt = false;
                void Line(string[] cells, bool bold, string bg)
                {
                    for (int i = 0; i < cells.Length; i++)
                    {
                        var cell = table.Cell().Background(bg).Padding(4);
                        var text = i >= 2 ? cell.AlignRight().Text(cells[i]) : cell.Text(cells[i]);
                        text.FontSize(fontSize);
                        if (bold) text.Bold();
                    }
                }
                foreach (var r in schedule.Rows)
                {
                    Line(RowCells(r), false, alt ? "#f9f9f9" : "#ffffff");
                    alt = !alt;
                }
                Line(TotalsCells(schedule.Totals), true, "#eeeeee");
            });
        }
    }
}
