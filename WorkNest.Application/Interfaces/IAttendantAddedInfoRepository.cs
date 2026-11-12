using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Who put a person on a booked room, and when (read-only; sources missing on a database are skipped).</summary>
    public class AttendantAddedInfo
    {
        public int PersonId { get; set; }
        /// <summary>True = added by the customer (WN_BookingEmployeeRequests), false = staff assignment.</summary>
        public bool ByCustomer { get; set; }
        /// <summary>E-mail of the adding user when known.</summary>
        public string? ByEmail { get; set; }
        /// <summary>yyyy-MM-dd (Pakistan date) or null.</summary>
        public string? On { get; set; }
    }

    public interface IAttendantAddedInfoRepository
    {
        Task<List<AttendantAddedInfo>> GetAsync(int bookingDetailId);
    }
}
