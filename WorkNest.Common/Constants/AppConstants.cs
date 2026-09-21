namespace WorkNest.Common.Constants
{
    /// <summary>
    /// Application-wide string constants to avoid magic strings across the codebase.
    /// </summary>
    public static class AppConstants
    {
        public const int DefaultCompanyId = 1;

        public static class BookingStatus
        {
            public const int Pending   = 5;
            public const int Cancelled = 86;
            public const int Rejected  = 3;
            public const int Confirmed = 33;

            public static readonly Dictionary<string, int> Map = new()
            {
                { "Pending",   Pending },
                { "Cancelled", Cancelled },
                { "Rejected",  Rejected },
                { "Confirmed", Confirmed },
            };
        }

        public static class Headers
        {
            public const string UserEmail = "x-user-email";
        }

        public static class PaymentStatus
        {
            public const string Paid    = "Paid";
            public const string Failed  = "Failed";
            public const string Pending = "Pending";
            public const string Deleted = "Deleted";
            public const string Refunded = "Refunded";
        }

        public static class SpaceStatus
        {
            public const string Available = "Available";
            public const string Inactive  = "Inactive";
        }
    }
}
