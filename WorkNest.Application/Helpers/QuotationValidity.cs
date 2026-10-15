using System.Globalization;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// A quotation is valid through its whole ValidUntil day (a Pakistan date) and expired from the next day: the
    /// customer can no longer accept or decline it (they ask the team for a new one).
    /// </summary>
    public static class QuotationValidity
    {
        /// <summary>ValidUntil's day has passed (Pakistan date). A missing date never expires.</summary>
        public static bool IsPast(DateTime validUntil, DateTime todayPk) =>
            validUntil != default && validUntil.Date < todayPk.Date;

        /// <summary>Still waiting for the customer's answer (sent to them, not answered or replaced).</summary>
        public static bool IsWaitingForAnswer(string? status) =>
            string.Equals(status?.Trim(), "Sent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase);

        /// <summary>Waiting for an answer, but its last valid day has passed.</summary>
        public static bool IsExpired(string? status, DateTime validUntil, DateTime todayPk) =>
            IsWaitingForAnswer(status) && IsPast(validUntil, todayPk);

        /// <summary>The customer may accept or decline: Sent and still valid today.</summary>
        public static bool CanRespond(string? status, DateTime validUntil, DateTime todayPk) =>
            string.Equals(status?.Trim(), "Sent", StringComparison.OrdinalIgnoreCase) && !IsPast(validUntil, todayPk);

        public static string ExpiredMessage(DateTime validUntil) =>
            $"This quotation expired on {validUntil.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}. Please ask our team for a new quotation.";
    }
}
