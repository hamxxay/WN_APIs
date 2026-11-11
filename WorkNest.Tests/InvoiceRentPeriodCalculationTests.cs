using WorkNest.Application.Services;

namespace WorkNest.Tests;

public class InvoiceRentPeriodCalculationTests
{
    [Fact]
    public void FullCalendarMonthsChargeOneMonthlyRentPerMonth()
    {
        var result = InvoiceCalculationEngine.CalculateRentForPeriod(
            100_000m, new DateTime(2026, 11, 1), new DateTime(2027, 1, 31));

        Assert.Equal(3m, result.EffectiveMonths);
        Assert.Equal(300_000m, result.Rent);
    }

    [Fact]
    public void PartialMonthsUseThirtyDayProration()
    {
        var result = InvoiceCalculationEngine.CalculateRentForPeriod(
            100_000m, new DateTime(2026, 11, 10), new DateTime(2027, 1, 31));

        Assert.Equal(2.7m, result.EffectiveMonths);
        Assert.Equal(270_000m, result.Rent);
    }

    [Fact]
    public void FullFebruaryCountsAsOneMonth()
    {
        var result = InvoiceCalculationEngine.CalculateRentForPeriod(
            100_000m, new DateTime(2028, 2, 1), new DateTime(2028, 2, 29));

        Assert.Equal(1m, result.EffectiveMonths);
        Assert.Equal(100_000m, result.Rent);
    }

    [Fact]
    public void RejectsPeriodEndingBeforeItStarts()
    {
        Assert.Throws<ArgumentException>(() => InvoiceCalculationEngine.CalculateRentForPeriod(
            100_000m, new DateTime(2026, 11, 2), new DateTime(2026, 11, 1)));
    }
}
