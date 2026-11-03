using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Transfer;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class TransferService : ITransferService
    {
        private readonly ITransferRepository _transferRepo;
        private readonly IEmailService _emailService;
        private readonly ITransferAddendumPdfService _pdfService;
        private readonly IBusinessClock _clock;
        private readonly ILogger<TransferService> _logger;

        public TransferService(
            ITransferRepository transferRepo,
            IEmailService emailService,
            ITransferAddendumPdfService pdfService,
            IBusinessClock clock,
            ILogger<TransferService> logger)
        {
            _transferRepo = transferRepo;
            _emailService = emailService;
            _pdfService = pdfService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<ApiResponse> GetPreviewAsync(
            TransferPreviewRequest request, string? userEmail, IReadOnlyCollection<int>? allowedLocationIds)
        {
            if (request.BookingId <= 0)
                return ApiResponse.Fail("Booking ID is required.");

            if (request.NewSpaceId <= 0)
                return ApiResponse.Fail("New space ID is required.");

            var booking = await _transferRepo.GetBookingSummaryAsync(request.BookingId);
            if (booking == null)
                return ApiResponse.Fail($"Active booking #{request.BookingId} not found.");

            // Location-bound check
            if (allowedLocationIds != null && allowedLocationIds.Count > 0 && !allowedLocationIds.Contains(booking.LocationId))
                return ApiResponse.Fail("You are not authorized to manage bookings for this location.");

            var effectiveDate = request.EffectiveDate ?? _clock.Today;

            var preview = await _transferRepo.GetTransferPreviewAsync(
                request.BookingId, request.NewSpaceId, effectiveDate, request.TransferFee);

            // Re-verify settlement with standalone calculator to ensure strict parity
            if (preview.CurrentBooking != null && preview.TargetSpace != null && preview.Settlement != null)
            {
                var calc = TransferSettlementCalculator.Calculate(
                    preview.Settlement.PeriodStart,
                    preview.Settlement.PeriodEnd,
                    effectiveDate,
                    preview.CurrentBooking.MonthlyRent,
                    preview.TargetSpace.MonthlyRate,
                    request.TransferFee);

                preview.Settlement.DaysUsed = calc.DaysUsed;
                preview.Settlement.RemainingDays = calc.RemainingDays;
                preview.Settlement.CreditAtA = calc.CreditAtA;
                preview.Settlement.RepricedAtB = calc.RepricedAtB;
                preview.Settlement.Adjustment = calc.Adjustment;
                preview.Settlement.TransferFee = calc.TransferFee;
                preview.Settlement.TotalDue = calc.TotalDue;
                preview.Settlement.Outcome = calc.Outcome;
                preview.Settlement.OutcomeDescription = calc.OutcomeDescription;
            }

            return ApiResponse.Ok(preview);
        }

        public async Task<ApiResponse> ExecuteTransferAsync(
            TransferExecuteRequest request, string? userEmail, int? actorId, IReadOnlyCollection<int>? allowedLocationIds)
        {
            if (request.BookingId <= 0)
                return ApiResponse.Fail("Booking ID is required.");

            if (request.NewSpaceId <= 0)
                return ApiResponse.Fail("New space ID is required.");

            if (string.IsNullOrWhiteSpace(request.Note))
                return ApiResponse.Fail("A transfer note is required.");

            var booking = await _transferRepo.GetBookingSummaryAsync(request.BookingId);
            if (booking == null)
                return ApiResponse.Fail($"Active booking #{request.BookingId} not found.");

            if (allowedLocationIds != null && allowedLocationIds.Count > 0 && !allowedLocationIds.Contains(booking.LocationId))
                return ApiResponse.Fail("You are not authorized to manage bookings for this location.");

            var effectiveDate = request.EffectiveDate ?? _clock.Today;

            // Run preview first to validate all conditions before transaction
            var preview = await _transferRepo.GetTransferPreviewAsync(
                request.BookingId, request.NewSpaceId, effectiveDate, request.TransferFee);

            if (!preview.IsValid)
            {
                string errorList = string.Join(" ", preview.ValidationErrors);
                return ApiResponse.Fail(string.IsNullOrWhiteSpace(errorList) ? "Transfer pre-validation failed." : errorList);
            }

            if (preview.BlockingInvoices.Any())
            {
                return ApiResponse.Fail($"Transfer blocked: {preview.BlockingInvoices.Count} unpaid invoice(s) exist on this booking.");
            }

            if (preview.DiscountValidation?.IsExceeded == true)
            {
                return ApiResponse.Fail(preview.DiscountValidation.Message ?? "Discount exceeds offering type cap.");
            }

            // Execute transactionally inside stored procedure (with UPDLOCK, HOLDLOCK and duplicate validation)
            var result = await _transferRepo.ExecuteTransferAsync(
                request.BookingId,
                request.NewSpaceId,
                effectiveDate,
                request.TransferFee,
                request.Note.Trim(),
                actorId,
                userEmail);

            if (!result.Success)
                return ApiResponse.Fail(result.Message);

            // After commit: Generate Addendum PDF and send to customer asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    if (preview.CurrentBooking != null && preview.TargetSpace != null && preview.Settlement != null)
                    {
                        var pdfBytes = _pdfService.GenerateAddendumPdf(
                            preview.CurrentBooking,
                            preview.TargetSpace,
                            preview.Settlement,
                            result.NewBookingId,
                            request.Note.Trim());

                        result.AddendumPdfGenerated = true;

                        if (!string.IsNullOrWhiteSpace(preview.CurrentBooking.CustomerEmail))
                        {
                            string customerDisplay = preview.CurrentBooking.CustomerName ?? "Valued Customer";
                            string refNumber = $"TRF-{request.BookingId}-TO-{result.NewBookingId}";
                            await _emailService.SendAgreementEmailAsync(
                                preview.CurrentBooking.CustomerEmail,
                                customerDisplay,
                                refNumber,
                                pdfBytes);

                            result.AddendumEmailSent = true;
                            _logger.LogInformation("Transfer Addendum PDF successfully emailed to {Email} for Transfer #{TransferId}.",
                                preview.CurrentBooking.CustomerEmail, result.TransferId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate or email Transfer Addendum PDF for Transfer #{TransferId}.", result.TransferId);
                }
            });

            return ApiResponse.Ok(result, "Space change executed successfully.");
        }

        public async Task<ApiResponse> GetAvailableTargetSpacesAsync(
            int bookingId, DateTime effectiveDate, IReadOnlyCollection<int>? allowedLocationIds)
        {
            var booking = await _transferRepo.GetBookingSummaryAsync(bookingId);
            if (booking == null)
                return ApiResponse.Fail($"Booking #{bookingId} not found.");

            if (allowedLocationIds != null && allowedLocationIds.Count > 0 && !allowedLocationIds.Contains(booking.LocationId))
                return ApiResponse.Fail("You are not authorized to view spaces for this location.");

            var spaces = await _transferRepo.GetAvailableTargetSpacesAsync(
                booking.LocationId, effectiveDate, booking.EndOn, bookingId);

            return ApiResponse.Ok(spaces);
        }

        public async Task<byte[]> GenerateAddendumPdfAsync(
            int oldBookingId, int newBookingId, int newSpaceId, DateTime effectiveDate, string note)
        {
            var oldBooking = await _transferRepo.GetBookingSummaryAsync(oldBookingId)
                ?? throw new InvalidOperationException($"Booking #{oldBookingId} not found.");
            var newSpace = await _transferRepo.GetTargetSpaceSummaryAsync(newSpaceId)
                ?? throw new InvalidOperationException($"Space #{newSpaceId} not found.");

            var preview = await _transferRepo.GetTransferPreviewAsync(oldBookingId, newSpaceId, effectiveDate, 0m);
            var settlement = preview.Settlement ?? TransferSettlementCalculator.Calculate(
                effectiveDate, effectiveDate.AddDays(30), effectiveDate, oldBooking.MonthlyRent, newSpace.MonthlyRate, 0m);

            return _pdfService.GenerateAddendumPdf(oldBooking, newSpace, settlement, newBookingId, note);
        }
    }
}
