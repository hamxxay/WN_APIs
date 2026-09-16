namespace WorkNest.Domain.Enums
{
    /// <summary>Booking status values stored in WN_Bookings.BookingStatus column.</summary>
    public enum BookingStatus
    {
        Pending   = 5,
        Cancelled = 86,
        Rejected  = 3,
        Confirmed = 33
    }

    /// <summary>User role IDs stored in WN_Users.RoleId column.</summary>
    public enum UserRole
    {
        SuperAdmin     = 1,
        Admin          = 2,
        General        = 14,
        SalesExecutive = 16
    }
}
