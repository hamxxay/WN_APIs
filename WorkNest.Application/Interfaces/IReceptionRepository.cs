using WorkNest.Application.DTOs.Reception;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Front-desk reads and the few writes the Receptionist role needs (script ROLE_01_receptionist adds the columns
    /// WN_Bookings.MeetingCheckedInAt / MeetingCheckedOutAt and WN_Contacts.PreferredDate / PreferredTime / Company / SpaceType).
    /// Everything that reads or writes those columns checks first that they exist.
    /// </summary>
    public interface IReceptionRepository
    {
        /// <summary>True once the meeting-room check-in columns exist (ROLE_01); cached for a minute.</summary>
        Task<bool> HasCheckInColumnsAsync();

        /// <summary>True once the tour-inquiry columns exist (ROLE_01); cached for a minute.</summary>
        Task<bool> HasTourColumnsAsync();

        /// <summary>
        /// Tour inquiries for the day: with the ROLE_01 columns those whose preferred date is the day, before it those that came
        /// in on that day. <paramref name="locations"/>: null = every branch; otherwise those branches plus the inquiries
        /// without a branch (as the Tour Inquiries list).
        /// </summary>
        Task<List<ReceptionTourRow>> GetToursForDayAsync(IReadOnlyList<int>? locations, DateTime day);

        /// <summary>Active meeting rooms (space category 2) of the locations (null = all).</summary>
        Task<List<ReceptionRoomRow>> GetMeetingRoomsAsync(IReadOnlyList<int>? locations);

        /// <summary>Meeting-room bookings (not cancelled) overlapping [from, to) at the locations (null = all), by start.</summary>
        Task<List<ReceptionBookingRow>> GetMeetingBookingsAsync(IReadOnlyList<int>? locations, DateTime from, DateTime to);

        Task<ReceptionBookingRow?> GetMeetingBookingAsync(int bookingId);

        /// <summary>Invoices due on the day with a balance left (not cancelled / not waiting for approval), at the locations (null = all).</summary>
        Task<List<ReceptionInvoiceRow>> GetInvoicesDueAsync(IReadOnlyList<int>? locations, DateTime day);

        /// <summary>Stamps the check-in (or check-out) time; false when the booking is not found / not in the required state.</summary>
        Task<bool> SetCheckInAsync(int bookingId, DateTime at, int? byUserId);
        Task<bool> SetCheckOutAsync(int bookingId, DateTime at, int? byUserId);

        /// <summary>Records who booked a meeting room (WN_Bookings.CreatedById = the staff member instead of the customer).</summary>
        Task SetBookingCreatorAsync(int bookingId, int staffUserId);

        /// <summary>Saves the extra tour details on a new inquiry; false when the ROLE_01 columns are not there.</summary>
        Task<bool> SaveTourExtrasAsync(int contactId, DateTime? preferredDate, string? preferredTime, string? company, string? spaceType);
    }
}
