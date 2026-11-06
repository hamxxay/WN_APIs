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
    
        public async Task<ApiResponse> GetActiveBookingsAsync(
            int? locationId, string? search, IReadOnlyCollection<int>? allowedLocationIds)
        {
            if (allowedLocationIds != null && allowedLocationIds.Count > 0)
            {
                if (locationId.HasValue && !allowedLocationIds.Contains(locationId.Value))
                {
                    return ApiResponse.Fail("You are not authorized to view bookings for this location.");
                }
                if (!locationId.HasValue && allowedLocationIds.Count == 1)
                {
                    locationId = allowedLocationIds.First();
                }
            }

            var bookings = await _transferRepo.GetActiveBookingsAsync(locationId, search);

            if (allowedLocationIds != null && allowedLocationIds.Count > 0)
            {
                bookings = bookings.Where(b => allowedLocationIds.Contains(b.LocationId)).ToList();
            }

            return ApiResponse.Ok(bookings);
        }
    
        // =========================================================================================
        // Phase 2: Branch Transfers & Pending Payments
        // =========================================================================================

        public async Task<ApiResponse> CreateBranchTransferRequestAsync(
            BranchTransferCreateRequestDto request, string? userEmail, int? actorId, IReadOnlyCollection<int>? allowedLocationIds)
        {
            var booking = await _transferRepo.GetBookingSummaryAsync(request.BookingId);
            if (booking == null)
                return ApiResponse.Fail($"Booking #{request.BookingId} not found.");

            if (allowedLocationIds != null && allowedLocationIds.Count > 0 && !allowedLocationIds.Contains(booking.LocationId))
                return ApiResponse.Fail("You are not authorized to initiate transfers for this booking's location.");

            try
            {
                var requestId = await _transferRepo.CreateBranchTransferRequestAsync(
                    request, actorId ?? 0, userEmail ?? "system@worknest.com");
                return ApiResponse.Ok(new { RequestId = requestId, Message = "Branch transfer request submitted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating branch transfer request for booking {BookingId}", request.BookingId);
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> CancelBranchTransferRequestAsync(
            int requestId, BranchTransferCancelRequestDto request, string? userEmail, int? actorId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds)
        {
            try
            {
                await _transferRepo.CancelBranchTransferRequestAsync(
                    requestId, request.Reason, actorId ?? 0, userEmail ?? "system@worknest.com");
                return ApiResponse.Ok(new { Success = true, Message = "Transfer request cancelled successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling transfer request {RequestId}", requestId);
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> DeclineBranchTransferRequestAsync(
            int requestId, BranchTransferDeclineRequestDto request, string? userEmail, int? actorId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds)
        {
            try
            {
                await _transferRepo.DeclineBranchTransferRequestAsync(
                    requestId, request.Reason, actorId ?? 0, userEmail ?? "system@worknest.com");
                return ApiResponse.Ok(new { Success = true, Message = "Transfer request declined." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error declining transfer request {RequestId}", requestId);
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> ListSendingRequestsAsync(
            int? companyId, int? locationId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds)
        {
            try
            {
                var list = await _transferRepo.ListSendingRequestsAsync(companyId, locationId, userRole);
                if (allowedLocationIds != null && allowedLocationIds.Count > 0 && userRole != "super_admin")
                {
                    list = list.Where(r => allowedLocationIds.Contains(r.FromLocationId)).ToList();
                }
                return ApiResponse.Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing sending transfer requests");
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> GetIncomingAvailableSpacesAsync(
            int requestId, IReadOnlyCollection<int>? allowedLocationIds)
        {
            try
            {
                var spaces = await _transferRepo.GetIncomingAvailableSpacesAsync(requestId);
                return ApiResponse.Ok(spaces);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available spaces for incoming request {RequestId}", requestId);
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> ListIncomingRequestsAsync(
            int? companyId, int? locationId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds)
        {
            try
            {
                var list = await _transferRepo.ListIncomingRequestsAsync(companyId, locationId, userRole);
                if (allowedLocationIds != null && allowedLocationIds.Count > 0 && userRole != "super_admin")
                {
                    list = list.Where(r => allowedLocationIds.Contains(r.ToLocationId)).ToList();
                }
                return ApiResponse.Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing incoming transfer requests");
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> GetMatchResolutionAsync(
            int requestId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds)
        {
            try
            {
                var details = await _transferRepo.GetMatchResolutionAsync(requestId);
                if (details == null)
                    return ApiResponse.Fail($"Transfer request #{requestId} not found.");

                return ApiResponse.Ok(details);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting match resolution for request {RequestId}", requestId);
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> AcceptWithSpaceAsync(
            int requestId, BranchTransferAcceptWithSpaceRequestDto request, string? userEmail, int? actorId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds)
        {
            try
            {
                var result = await _transferRepo.AcceptWithSpaceAsync(
                    requestId, request.SpaceId, actorId ?? 0, userEmail ?? "system@worknest.com");
                return ApiResponse.Ok(new
                {
                    Status = result.Status,
                    Message = result.Message,
                    NewBookingId = result.NewBookingId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting transfer request {RequestId}", requestId);
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> ResolveMatchAsync(
            int requestId, BranchTransferResolveMatchRequestDto request, string? userEmail, int? actorId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds)
        {
            try
            {
                var result = await _transferRepo.ResolveMatchAsync(
                    requestId, request.ResolutionType, request.SelectedCustomerId, request.Note, actorId ?? 0, userEmail ?? "system@worknest.com");
                return ApiResponse.Ok(new
                {
                    Status = result.Status,
                    Message = result.Message,
                    NewBookingId = result.NewBookingId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving match for request {RequestId}", requestId);
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> ListPendingPaymentsAsync(
            int? companyId, int? locationId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds)
        {
            try
            {
                var list = await _transferRepo.ListPendingPaymentsAsync(companyId, locationId, userRole);
                if (allowedLocationIds != null && allowedLocationIds.Count > 0 && userRole != "super_admin")
                {
                    list = list.Where(p => allowedLocationIds.Contains(p.FromLocationId) || allowedLocationIds.Contains(p.ToLocationId)).ToList();
                }
                return ApiResponse.Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing pending transfer payments");
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> ConfirmPendingPaymentAsync(
            int paymentId, PendingPaymentConfirmRequestDto request, string? userEmail, int? actorId, string? userRole, IReadOnlyCollection<int>? allowedLocationIds)
        {
            try
            {
                var success = await _transferRepo.ConfirmPendingPaymentAsync(
                    paymentId, request.BankAccount, request.PaymentReference, request.PaymentDate, actorId ?? 0, userEmail ?? "system@worknest.com");
                return ApiResponse.Ok(new { Success = success, Message = "Transfer payment confirmed successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error confirming pending payment {PaymentId}", paymentId);
                return ApiResponse.Fail(ex.Message);
            }
        }
    }
}

