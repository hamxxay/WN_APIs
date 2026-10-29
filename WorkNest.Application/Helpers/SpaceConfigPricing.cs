using System;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// What a space costs, from its WN_SpaceConfig row (PRC_01 / PRC_02, owner's rules): a meeting room (MeetingRoom / Meeting /
    /// Conference) is PricePerHour, Hourly; a private office (PrivateOffice / Private) is PricePerMonth per seat, Monthly, and an
    /// office costs the seat price x its seats; a shared space (SharedSpace / Shared) is PricePerMonth per seat, Monthly.
    /// 0 / missing = no price. The procedures (WN_Bookings_Insert and the space lists) apply the same rules.
    /// </summary>
    public static class SpaceConfigPricing
    {
        public const string Monthly = "Monthly";
        public const string Hourly = "Hourly";

        private static bool Is(string? code, params string[] names)
        {
            foreach (var n in names)
                if (string.Equals(code?.Trim(), n, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool IsPrivateOffice(string? categoryCode) => Is(categoryCode, "PrivateOffice", "Private");

        public static bool IsMeetingRoom(string? categoryCode) => Is(categoryCode, "MeetingRoom", "Meeting", "Conference");

        /// <summary>Monthly / Hourly for the three kinds above; null for any other category (it keeps the space's own price).</summary>
        public static string? PeriodOf(string? categoryCode) =>
            IsMeetingRoom(categoryCode) ? Hourly
            : IsPrivateOffice(categoryCode) || Is(categoryCode, "SharedSpace", "Shared") ? Monthly
            : null;

        /// <summary>Price per seat (per hour for a meeting room) from the config row's prices; 0 when none or another category.</summary>
        public static decimal SeatPrice(string? categoryCode, decimal pricePerHour, decimal pricePerMonth) =>
            PeriodOf(categoryCode) switch
            {
                Hourly => Math.Max(0m, pricePerHour),
                Monthly => Math.Max(0m, pricePerMonth),
                _ => 0m
            };

        /// <summary>Price of the whole space: a private office = seat price x its seats, anything else = the seat price.</summary>
        public static decimal RoomPrice(string? categoryCode, decimal seatPrice, int seats) =>
            IsPrivateOffice(categoryCode) ? seatPrice * Math.Max(1, seats) : seatPrice;

        /// <summary>Decimal of a config column (null / DBNull = 0).</summary>
        public static decimal Money(object? value) =>
            value is null || value is DBNull ? 0m : Convert.ToDecimal(value);

        /// <summary>Decimal of a column of the config row (no row / no column / null = 0).</summary>
        public static decimal Money(System.Collections.Generic.IDictionary<string, object?>? row, string column) =>
            row is not null && row.TryGetValue(column, out var v) ? Money(v) : 0m;
    }
}
