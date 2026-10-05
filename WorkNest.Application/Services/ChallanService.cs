using WorkNest.Application.DTOs.Challan;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Challan Validity Extension: search a booking's challan and push its expiry date out.
    /// Uses the existing WN_Challan_Search / WN_Challan_ExtendValidity procedures.
    /// </summary>
    public class ChallanService : IChallanService
    {
        private readonly IDbRepository _db;

        public ChallanService(IDbRepository db)
        {
            _db = db;
        }

        public async Task<IDictionary<string, object?>?> SearchAsync(string query)
        {
            query = (query ?? "").Trim();
            if (query.Length == 0) return null;
            return await _db.SearchChallanDbAsync(query);
        }

        public async Task<(bool Ok, string? Error)> ExtendValidityAsync(ChallanExtendValidityRequest request, string updatedBy)
        {
            if (request == null || request.BookingId <= 0) return (false, "Booking is required.");
            if (!DateTime.TryParse(request.NewExpiryDate, out var newDate)) return (false, "Choose a valid new expiry date.");
            if (newDate.Date < DateTime.Today) return (false, "The new expiry date cannot be in the past.");

            var remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
            if (remarks?.Length > 500) remarks = remarks[..500];
            return await _db.ExtendChallanValidityDbAsync(request.BookingId, newDate.Date, updatedBy, remarks);
        }
    }
}
