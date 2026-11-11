using System;
using System.ComponentModel.DataAnnotations;

namespace WorkNest.Application.DTOs.Invoice
{
    /// <summary>Input for recalculating rent after an invoice's rent period is edited.</summary>
    public sealed record CalculateRentPeriodRequest
    {
        /// <summary>The rent amount for one full month.</summary>
        [Range(typeof(decimal), "0", "999999999999")]
        public decimal MonthlyRent { get; init; }

        /// <summary>First day of the selected rent period, inclusive.</summary>
        public DateTimeOffset PeriodStart { get; init; }

        /// <summary>Last day of the selected rent period, inclusive.</summary>
        public DateTimeOffset PeriodEnd { get; init; }
    }

    /// <summary>Rent recalculated for an edited period; does not include discounts, fees, tax, or deposit.</summary>
    public sealed record CalculateRentPeriodResponse(
        decimal MonthlyRent,
        DateTimeOffset PeriodStart,
        DateTimeOffset PeriodEnd,
        decimal EffectiveMonths,
        decimal Rent);
}
