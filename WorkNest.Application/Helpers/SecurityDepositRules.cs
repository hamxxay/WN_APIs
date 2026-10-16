using System;

namespace WorkNest.Application.Helpers
{
    /// <summary>The three kinds of space the security deposit rule tells apart.</summary>
    public enum DepositSpaceKind
    {
        PrivateOffice,
        SharedSpace,
        MeetingRoom
    }

    /// <summary>
    /// "Security Deposit (Months)" (owner's rule, 16 Oct 2026). A quotation / booking stores the months (0, 1, 2 or 3)
    /// and the amount worked out when it is saved: months x monthly rent x (1 - rent discount), 2 decimals. Private
    /// offices choose 0-3 (default 2); shared spaces and meeting rooms never take a deposit. Reading a saved deposit
    /// later (invoice, agreement, PDFs, previews) uses the stored amount; a stored 0 with months above 0 (an older row)
    /// is worked out from the months; otherwise there is no deposit. Nothing turns 0 / missing months into 2 (or 1).
    /// The SD_01 database script applies the same rule in the procedures.
    /// </summary>
    public static class SecurityDepositRules
    {
        public const int MinMonths = 0;
        public const int MaxMonths = 3;
        public const int DefaultPrivateMonths = 2;

        public const string InvalidMonthsMessage = "Security deposit must be 0, 1, 2 or 3 months.";

        /// <summary>
        /// Kind of space from the space category code (PrivateOffice / SharedSpace / MeetingRoom ...) or, when the code
        /// does not say, the space type name. Unknown kinds count as shared (no deposit), as the forms always did.
        /// </summary>
        public static DepositSpaceKind KindOf(string? categoryCode, string? spaceTypeName = null) =>
            KindOfText(categoryCode) ?? KindOfText(spaceTypeName) ?? DepositSpaceKind.SharedSpace;

        private static DepositSpaceKind? KindOfText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            bool Has(string part) => text.Contains(part, StringComparison.OrdinalIgnoreCase);
            if (Has("Meeting") || Has("Conference") || Has("Hourly")) return DepositSpaceKind.MeetingRoom;
            if (Has("Private")) return DepositSpaceKind.PrivateOffice;
            if (Has("Shared") || Has("Co-Working") || Has("Coworking") || Has("Open") || Has("Desk")) return DepositSpaceKind.SharedSpace;
            return null;
        }

        /// <summary>0, 1, 2 or 3; null means "not sent" (the default for the space kind).</summary>
        public static bool IsValidMonths(int? months) => months is null or (>= MinMonths and <= MaxMonths);

        /// <summary>Throws <see cref="ArgumentException"/> with <see cref="InvalidMonthsMessage"/> for anything but null / 0-3.</summary>
        public static void EnsureValidMonths(int? months)
        {
            if (!IsValidMonths(months)) throw new ArgumentException(InvalidMonthsMessage);
        }

        /// <summary>
        /// The months to store: the request's value (checked 0-3) for a private office, 2 when none was sent; always 0
        /// for shared spaces and meeting rooms whatever was sent.
        /// </summary>
        public static int ResolveMonths(DepositSpaceKind kind, int? requested)
        {
            EnsureValidMonths(requested);
            if (kind != DepositSpaceKind.PrivateOffice) return 0;
            return requested ?? DefaultPrivateMonths;
        }

        public static bool IsFixedDiscount(string? discountType) =>
            string.Equals(discountType?.Trim(), "Amount", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(discountType?.Trim(), "Fixed", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The rent discount as a fraction (0..1): a percentage / 100, or a fixed PKR amount as its share of ONE month's
        /// rent (the fixed discount is per month), at most the whole rent.
        /// </summary>
        public static decimal DiscountFraction(string? discountType, decimal discountValue, decimal monthlyRent)
        {
            if (discountValue <= 0m) return 0m;
            if (IsFixedDiscount(discountType))
                return monthlyRent > 0m ? Math.Min(discountValue / monthlyRent, 1m) : 0m;
            return Math.Min(discountValue, 100m) / 100m;
        }

        /// <summary>months x monthly rent x (1 - discount fraction), rounded to 2 decimals; 0 without months or rent.</summary>
        public static decimal Amount(int months, decimal monthlyRent, decimal discountFraction)
        {
            if (months <= 0 || monthlyRent <= 0m) return 0m;
            decimal fraction = Math.Clamp(discountFraction, 0m, 1m);
            return Math.Round(months * monthlyRent * (1m - fraction), 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>Same as <see cref="Amount"/> with the discount given as a percentage (0-100).</summary>
        public static decimal AmountForPercent(int months, decimal monthlyRent, decimal discountPercent) =>
            Amount(months, monthlyRent, Math.Clamp(discountPercent, 0m, 100m) / 100m);

        /// <summary>
        /// A saved deposit read back: the stored amount; a stored 0 with months above 0 on a private office (an older
        /// row) = months x rent x (1 - discount); otherwise 0. Meeting rooms never have one.
        /// </summary>
        public static decimal Stored(DepositSpaceKind kind, decimal storedAmount, int? storedMonths, decimal monthlyRent, decimal discountFraction)
        {
            if (kind == DepositSpaceKind.MeetingRoom) return 0m;
            if (storedAmount > 0m) return Math.Round(storedAmount, 2, MidpointRounding.AwayFromZero);
            if (kind != DepositSpaceKind.PrivateOffice || (storedMonths ?? 0) <= 0) return 0m;
            return Amount(storedMonths!.Value, monthlyRent, discountFraction);
        }

        /// <summary>"Security Deposit (2 Months) (Refundable)" - the quotation detail line.</summary>
        public static string QuotationLineDescription(int months) =>
            $"Security Deposit ({months} Month{(months > 1 ? "s" : "")}) (Refundable)";
    }
}
