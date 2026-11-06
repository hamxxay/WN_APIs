using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.OneTimeDiscount;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class OneTimeDiscountService : IOneTimeDiscountService
    {
        private readonly IOneTimeDiscountRepository _repo;

        public OneTimeDiscountService(IOneTimeDiscountRepository repo)
        {
            _repo = repo;
        }

        private static string? FormatLocations(IReadOnlyCollection<int>? allowedLocations) =>
            allowedLocations != null && allowedLocations.Count > 0
                ? string.Join(",", allowedLocations)
                : null;

        public async Task<ApiResponse> GetByBookingAsync(int bookingId, IReadOnlyCollection<int>? allowedLocations = null)
        {
            if (bookingId <= 0)
                return ApiResponse.Fail("Valid BookingId is required.");

            try
            {
                var summary = await _repo.GetBookingSummaryDbAsync(bookingId, FormatLocations(allowedLocations));
                return ApiResponse.Ok(summary);
            }
            catch (Exception ex)
            {
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> PreviewAsync(
            OneTimeDiscountPreviewRequest request,
            string? actorEmail = null,
            IReadOnlyCollection<int>? allowedLocations = null)
        {
            if (request == null || request.BookingId <= 0)
                return ApiResponse.Fail("Valid BookingId is required.");

            if (request.DiscountValue < 0)
                return ApiResponse.Fail("DiscountValue cannot be negative.");

            try
            {
                var preview = await _repo.PreviewDbAsync(request, FormatLocations(allowedLocations), null, actorEmail);
                return ApiResponse.Ok(preview);
            }
            catch (Exception ex)
            {
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> CreateAsync(
            OneTimeDiscountCreateRequest request,
            int createdBy,
            string? actorEmail = null,
            IReadOnlyCollection<int>? allowedLocations = null)
        {
            if (request == null || request.BookingId <= 0)
                return ApiResponse.Fail("Valid BookingId is required.");

            if (string.IsNullOrWhiteSpace(request.Reason))
                return ApiResponse.Fail("Reason is required for a one-time discount.");

            if (request.DiscountValue <= 0)
                return ApiResponse.Fail("DiscountValue must be greater than zero.");

            try
            {
                var created = await _repo.CreateDbAsync(request, createdBy, FormatLocations(allowedLocations), actorEmail);
                return ApiResponse.Ok(created, "One-time discount created successfully.");
            }
            catch (Exception ex)
            {
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> CancelAsync(
            int id,
            int cancelledBy,
            string? actorEmail = null,
            IReadOnlyCollection<int>? allowedLocations = null)
        {
            if (id <= 0)
                return ApiResponse.Fail("Valid discount Id is required.");

            try
            {
                var cancelled = await _repo.CancelDbAsync(id, cancelledBy, FormatLocations(allowedLocations), actorEmail);
                return ApiResponse.Ok(cancelled, "One-time discount cancelled successfully.");
            }
            catch (Exception ex)
            {
                return ApiResponse.Fail(ex.Message);
            }
        }
    }
}
