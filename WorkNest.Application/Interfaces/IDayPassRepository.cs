using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.DayPass;

namespace WorkNest.Application.Interfaces
{
    public interface IDayPassRepository
    {
        /// <summary>DP_01 has run (cached; checked once a minute until it has).</summary>
        Task<bool> IsInstalledAsync();

        Task<DayPassSpaceInfo?> GetSpaceInfoAsync(int spaceId, DateTime today);

        /// <summary>Every active space (of the locations, or the one space) with what is in use in the window; nothing filtered by free seats.</summary>
        Task<List<DayPassSpaceUsage>> GetSpaceUsageAsync(DateTime date, string window, IReadOnlyCollection<int>? locationIds, int? spaceId);

        Task<List<DayPassCustomerRow>> SearchCustomersAsync(string? search, IReadOnlyCollection<int>? allowedLocationIds);
        Task<DayPassCustomerRow?> GetCustomerAsync(string code);
        /// <summary>A message when the walk-in's email or CNIC already belongs to a customer; else null.</summary>
        Task<string?> FindWalkInConflictAsync(string email, string cnicDigits);
        /// <summary>The account is a bank (not cash-in-hand) account the location's company can receive on.</summary>
        Task<bool> IsBankAccountForLocationAsync(int accountId, int locationId);
        Task<(int Id, string Name)?> ResolveActorAsync(string? email);

        /// <summary>Saves the booking, the pass, the paid invoice and the receipt in one transaction (availability checked again inside it). Returns the pass id.</summary>
        Task<int> CreateAsync(DayPassCreateData data);
        Task<DayPassPage> ListAsync(DayPassListFilter filter, IReadOnlyCollection<int>? allowedLocationIds, DateTime now);
        Task<DayPassDto?> GetAsync(int id, bool withHistory);
        Task CancelAsync(int id, string reason, int actorId, string actorName, DateTime today);
        Task AddHistoryAsync(int dayPassId, string action, string? note, int? byUserId, string? byName);

        Task<DayPassBookingInfo?> GetByBookingAsync(int bookingId);
        Task<List<DayPassAccessDue>> GetAccessDueAsync(DateTime now);
        Task<List<int>> GetAttendantPersonIdsAsync(int bookingDetailId);
        Task<string?> GetPersonNameAsync(int personId);
        Task<int?> GetBookingDetailIdAsync(int bookingId);
        Task MarkAccessRemovedAsync(int dayPassId);
    }

    /// <summary>One space and what the window already has on it.</summary>
    public class DayPassSpaceUsage
    {
        public int SpaceId { get; set; }
        public string SpaceName { get; set; } = "";
        public string CategoryCode { get; set; } = "";
        public int LocationId { get; set; }
        public string LocationName { get; set; } = "";
        public int Capacity { get; set; }
        /// <summary>Seats of the overlapping bookings (no seats recorded = all).</summary>
        public int SeatsInUse { get; set; }
        public int BookingCount { get; set; }
        public decimal? ListPrice { get; set; }
    }
}
