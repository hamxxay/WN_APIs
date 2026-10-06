using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Business clock in Pakistan Standard Time (config "Business:TimeZone", default "Asia/Karachi").
    /// Accepts either the IANA id ("Asia/Karachi", macOS/Linux) or the Windows id ("Pakistan Standard Time")
    /// and falls back to the other one, then to a fixed UTC+05:00 zone (Pakistan has no DST).
    /// </summary>
    public sealed class BusinessClock : IBusinessClock
    {
        public const string DefaultTimeZoneId = "Asia/Karachi";

        /// <summary>Shared default instance for static helpers (e.g. InvoiceCalculationEngine) that cannot take DI.</summary>
        public static BusinessClock Default { get; private set; } = new BusinessClock(DefaultTimeZoneId);

        private readonly TimeZoneInfo _zone;

        public BusinessClock(string? timeZoneId = null)
        {
            _zone = ResolveZone(string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId.Trim());
        }

        /// <summary>Makes this instance the one used by static helpers.</summary>
        public BusinessClock UseAsDefault()
        {
            Default = this;
            return this;
        }

        public TimeZoneInfo TimeZone => _zone;

        public DateTime Now => DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zone), DateTimeKind.Unspecified);

        public DateTime Today => Now.Date;

        public DateTime FromUtc(DateTime utc)
        {
            var u = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(u, _zone), DateTimeKind.Unspecified);
        }

        private static TimeZoneInfo ResolveZone(string id)
        {
            var candidates = new List<string> { id };
            if (string.Equals(id, "Asia/Karachi", StringComparison.OrdinalIgnoreCase)) candidates.Add("Pakistan Standard Time");
            else if (string.Equals(id, "Pakistan Standard Time", StringComparison.OrdinalIgnoreCase)) candidates.Add("Asia/Karachi");

            foreach (var candidate in candidates)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }

            return TimeZoneInfo.CreateCustomTimeZone("PKT", TimeSpan.FromHours(5), "Pakistan Standard Time", "Pakistan Standard Time");
        }
    }
}
