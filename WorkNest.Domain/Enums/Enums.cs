namespace WorkNest.Domain.Enums
{
    /// <summary>Booking status values stored in WN_Bookings.BookingStatus column.</summary>
    public enum BookingStatus
    {
        Confirmed = 1,
        Cancelled = 2,
        Rejected  = 3,
        Completed = 4
    }

    /// <summary>User role IDs stored in WN_Users.RoleId column.</summary>
    public enum UserRole
    {
        SuperAdmin = 1,
        Admin      = 2,
        General    = 14
    }
}
