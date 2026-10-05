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

    /// <summary>Invoice status values stored in WN_Invoices.StatusId column (from dbo.OrderStatus).</summary>
    public enum InvoiceStatus : byte
    {
        Unpaid = 61,
        Paid   = 62
    }
}
