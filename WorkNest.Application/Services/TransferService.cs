using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.RentSchedule;
using WorkNest.Application.DTOs.Transfer;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services.Transfers;
using System.Text.Json;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class TransferService : ITransferService
    {
        private readonly ITransferRepository _transferRepo;
        private readonly IEmailService _emailService;
        private readonly ITransferAddendumPdfService _pdfService;
        private readonly IBusinessClock _clock;
        private readonly IBookingScheduleService _schedules;
        private readonly ILogger<TransferService> _logger;

        public TransferService(
            ITransferRepository transferRepo,
            IEmailService emailService,
            ITransferAddendumPdfService pdfService,
            IBusinessClock clock,
            IBookingScheduleService schedules,
            ILogger<TransferService> logger)
        {
            _transferRepo = transferRepo;
            _emailService = emailService;
            _pdfService = pdfService;
            _clock = clock;
            _schedules = schedules;
            _logger = logger;
        }

        public const string MeetingRoomMessage = "A meeting room is booked by the hour: choose an office or a shared space.";

        public const string PricingNotReadyMessage =
            "Space transfer needs the TRF_02 database update (price per seat x seats). Ask your administrator to run it.";
        public const string RedesignNotReadyMessage =
            "Space transfer needs the transfer redesign database update (TRF_02). Ask your administrator to run it.";

        public async Task<ApiResponse> GetPreviewAsync(
            TransferPreviewRequest request, string? userEmail, IReadOnlyCollection<int>? allowedLocationIds, bool salesExecutiveOnly = false)
        {
            if (request.BookingId <= 0)
                return ApiResponse.Fail("Booking ID is required.");

            if (request.NewSpaceId <= 0)
                return ApiResponse.Fail("New space ID is required.");

            var booking = await _transferRepo.GetBookingSummaryAsync(request.BookingId);
            if (booking == null)
                return ApiResponse.Fail($"Active booking #{request.BookingId} not found.");

            // Location-bound check
            if (allowedLocationIds != null && !allowedLocationIds.Contains(booking.LocationId))
                return ApiResponse.Fail("You are not authorized to manage bookings for this location.");

            var effectiveDate = request.EffectiveDate ?? _clock.Today;

            if (request.PerSeatPrice is < 0)
                return ApiResponse.Fail("The price per seat cannot be negative.");
            if (await _transferRepo.IsMeetingRoomAsync(request.NewSpaceId))
                return ApiResponse.Fail(MeetingRoomMessage);

            var preview = await _transferRepo.GetTransferPreviewAsync(
                request.BookingId, request.NewSpaceId, effectiveDate, request.TransferFee, request.PerSeatPrice);

            // Before TRF_02 the database prices the new space at one seat: show it but do not allow the transfer.
            if (!await _transferRepo.IsPerSeatPricingReadyAsync())
            {
                preview.IsValid = false;
                preview.ValidationErrors.Add(PricingNotReadyMessage);
            }
            else preview.RequiresApproval = salesExecutiveOnly;

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

        // ---- Space transfer redesign: plan, execute, requests -----------------------------------------------------------------

        private static readonly JsonSerializerOptions PlanJsonOptions = new(JsonSerializerDefaults.Web);

        private sealed record Built(TransferPlanResult Result, TransferPreviewResponseDto Preview, ActiveBookingSummaryDto Booking, DateTime Effective);

        /// <summary>Loads the facts and runs the plan maths. Fail = the request itself is unusable (booking missing, no access, meeting room).</summary>
        private async Task<(Built? Built, string? Fail)> BuildAsync(TransferPlanRequest req, IReadOnlyCollection<int>? allowed, bool requireReady)
        {
            if (req == null || req.BookingId <= 0) return (null, "Booking ID is required.");
            if (req.NewSpaceId <= 0) return (null, "New space ID is required.");

            var booking = await _transferRepo.GetBookingSummaryAsync(req.BookingId);
            if (booking == null) return (null, $"Active booking #{req.BookingId} not found.");
            if (allowed != null && !allowed.Contains(booking.LocationId))
                return (null, "You are not authorized to manage bookings for this location.");
            if (await _transferRepo.IsMeetingRoomAsync(req.NewSpaceId)) return (null, MeetingRoomMessage);

            var eff = (req.EffectiveDate ?? _clock.Today).Date;
            var preview = await _transferRepo.GetTransferPreviewAsync(req.BookingId, req.NewSpaceId, eff, req.TransferFee);
            var ts = preview.TargetSpace;
            if (ts == null)
                return (null, preview.ValidationErrors.Count > 0 ? string.Join(" ", preview.ValidationErrors) : "The transfer could not be previewed.");

            BookingScheduleResponse sched;
            try { sched = await _schedules.GetAsync(req.BookingId); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Transfer plan: rent schedule of booking {BookingId} not available", req.BookingId);
                return (null, "The rent schedule of this booking could not be loaded: " + ex.Message);
            }

            int months = await _transferRepo.GetSecurityDepositMonthsAsync(req.BookingId);
            var view = sched.Schedule;
            var input = new TransferPlanInput
            {
                BookingId = req.BookingId, NewSpaceId = req.NewSpaceId, NewSpaceName = ts.SpaceName,
                EffectiveDate = eff, ContractEnd = booking.EndOn, OldRows = view.Rows,
                OldSeats = Math.Max(1, view.Seats), OldPerSeatPrice = ts.CurrentPerSeatPrice is > 0 ? ts.CurrentPerSeatPrice.Value : view.PerSeatPrice,
                OldDeposit = view.DefaultSecurityDeposit > 0 ? view.DefaultSecurityDeposit : booking.SecurityDeposit,
                NewSeats = Math.Max(1, ts.Capacity), ListPerSeatPrice = ts.ListPerSeatPrice ?? 0m,
                SecurityDepositMonths = months, TaxPercent = view.TaxPercent, SupportRate = view.SupportRate,
                DiscountCapPercent = ts.OfferingTypeDiscountCap
            };
            var result = TransferPlanCalculator.Calculate(input, req);
            var plan = result.Plan;
            if (!preview.IsValid)
                plan.Errors.Add(preview.ValidationErrors.Count > 0 ? string.Join(" ", preview.ValidationErrors) : "Transfer pre-validation failed.");
            if (preview.BlockingInvoices.Any())
                plan.Errors.Add($"Transfer blocked: {preview.BlockingInvoices.Count} unpaid invoice(s) exist on this booking.");
            if (requireReady && !await _transferRepo.IsRedesignReadyAsync()) plan.Errors.Add(RedesignNotReadyMessage);
            return (new Built(result, preview, booking, eff), null);
        }

        public async Task<ApiResponse> PlanAsync(TransferPlanRequest request, TransferCaller caller)
        {
            var (built, fail) = await BuildAsync(request, caller.LocationIds, requireReady: true);
            if (built == null) return ApiResponse.Fail(fail!);
            built.Result.Plan.RequiresApproval = TransferAccess.NeedsApproval(caller);
            return ApiResponse.Ok(built.Result.Plan);
        }

        public async Task<ApiResponse> ExecuteAsync(TransferPlanRequest request, TransferCaller caller, string? userEmail)
        {
            if (request == null) return ApiResponse.Fail("Transfer request payload is required.");
            if (string.IsNullOrWhiteSpace(request.Note)) return ApiResponse.Fail("A transfer note is required.");
            request.Note = request.Note.Trim();

            var (built, fail) = await BuildAsync(request, caller.LocationIds, requireReady: true);
            if (built == null) return ApiResponse.Fail(fail!);
            if (built.Result.HasErrors) return ApiResponse.Fail(string.Join(" ", built.Result.Plan.Errors), built.Result.Plan.Errors);

            if (TransferAccess.NeedsApproval(caller))
            {
                var plan = built.Result.Plan;
                var id = await _transferRepo.InsertTransferRequestAsync(request.BookingId, request.NewSpaceId, built.Effective,
                    built.Booking.LocationId, JsonSerializer.Serialize(request, PlanJsonOptions), plan, request.Note, caller.UserId ?? 0);
                if (id == null) return ApiResponse.Fail("A transfer for this booking is already waiting for the sales manager's approval.");
                return ApiResponse.Ok(new TransferExecuteResponseDto
                {
                    Success = true, PendingApproval = true, RequestId = id, OldBookingId = request.BookingId,
                    Message = "Sent to the sales manager for approval."
                }, "Sent to the sales manager for approval.");
            }
            return await RunAsync(built, request, caller.UserId, userEmail, null);
        }

        /// <summary>Runs the transfer procedure with the plan's values (BuildAsync already checked them).</summary>
        private async Task<ApiResponse> RunAsync(Built b, TransferPlanRequest req, int? actorId, string? userEmail, int? requestId)
        {
            var r = b.Result; var plan = r.Plan;
            var args = new TransferExecutionArgs
            {
                PerSeatPrice = plan.PerSeatPrice, NewDeposit = plan.NewDeposit,
                SettleRent = r.SettleRent, SettleServiceCharge = r.SettleServiceCharge, SettleTax = r.SettleTax,
                CreditAmount = r.CreditAmount, NextBillingDate = r.NextBillingDate,
                ScheduleJson = TransferPlanCalculator.ToScheduleJson(r.Rows), TransferRequestId = requestId
            };
            var result = await _transferRepo.ExecuteTransferAsync(req.BookingId, req.NewSpaceId, b.Effective, plan.TransferFee,
                (req.Note ?? "").Trim(), actorId, userEmail, args);
            if (!result.Success) return ApiResponse.Fail(result.Message);
            result.Executed = true; result.Credit = r.CreditAmount; result.Refund = r.RefundAmount;
            if (r.RefundAmount > 0)
                result.Message += $" A security deposit refund of {r.RefundAmount:N2} is due to the client (finance pays it).";

            var preview = b.Preview;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (preview.CurrentBooking != null && preview.TargetSpace != null && preview.Settlement != null)
                    {
                        var pdfBytes = _pdfService.GenerateAddendumPdf(preview.CurrentBooking, preview.TargetSpace, preview.Settlement, result.NewBookingId, (req.Note ?? "").Trim());
                        result.AddendumPdfGenerated = true;
                        if (!string.IsNullOrWhiteSpace(preview.CurrentBooking.CustomerEmail))
                        {
                            await _emailService.SendAgreementEmailAsync(preview.CurrentBooking.CustomerEmail,
                                preview.CurrentBooking.CustomerName ?? "Valued Customer", $"TRF-{req.BookingId}-TO-{result.NewBookingId}", pdfBytes);
                            result.AddendumEmailSent = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate or email Transfer Addendum PDF for Transfer #{TransferId}.", result.TransferId);
                }
            });
            return ApiResponse.Ok(result, result.Message);
        }

        // ---- requests ---------------------------------------------------------------------------------------------------------

        public async Task<ApiResponse> ListRequestsAsync(TransferCaller caller, bool pendingOnly)
        {
            if (!await _transferRepo.IsRedesignReadyAsync()) return ApiResponse.Ok(new List<TransferRequestDto>());
            List<TransferRequestDto> list;
            if (TransferAccess.CanDecide(caller)) list = await _transferRepo.ListTransferRequestsAsync(pendingOnly, caller.LocationIds, null);
            else if (caller.IsSalesExecutive) list = await _transferRepo.ListTransferRequestsAsync(pendingOnly, null, caller.UserId ?? -1);
            else list = new List<TransferRequestDto>();
            return ApiResponse.Ok(list);
        }

        private static TransferPlanRequest? ReadPlanRequest(string json)
        {
            try { return JsonSerializer.Deserialize<TransferPlanRequest>(json, PlanJsonOptions); } catch { return null; }
        }

        private static TransferPlanRequest Merge(TransferPlanRequest stored, TransferDecisionRequest? d)
        {
            if (d == null) return stored;
            if (d.PerSeatPrice != null) { stored.PriceMode = TransferPlanCalculator.Custom; stored.PerSeatPrice = d.PerSeatPrice; }
            if (d.NewDeposit != null) { stored.DepositMode = TransferPlanCalculator.Custom; stored.NewDeposit = d.NewDeposit; }
            if (d.Rows != null) stored.Rows = d.Rows;
            return stored;
        }

        private async Task<(TransferRequestRecord? Rec, TransferPlanRequest? Req, string? Fail)> LoadRequestAsync(TransferCaller caller, int id, bool deciding)
        {
            var rec = await _transferRepo.GetTransferRequestAsync(id);
            if (rec == null || !TransferAccess.CanSee(caller, rec)) return (null, null, "Transfer request not found.");
            if (deciding && !TransferAccess.CanDecide(caller)) return (null, null, "Only the sales manager can decide a transfer request.");
            var req = ReadPlanRequest(rec.PlanJson);
            if (req == null) return (null, null, "The saved transfer request cannot be read.");
            return (rec, req, null);
        }

        public async Task<ApiResponse> GetRequestAsync(TransferCaller caller, int id)
        {
            var (rec, req, fail) = await LoadRequestAsync(caller, id, false);
            if (rec == null) return ApiResponse.Fail(fail!);
            try
            {
                var (built, _) = await BuildAsync(req!, null, requireReady: false);
                if (built != null) { built.Result.Plan.RequiresApproval = rec.Dto.Status == "Pending"; rec.Dto.Plan = built.Result.Plan; }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Transfer request {Id}: plan could not be rebuilt", id); }
            return ApiResponse.Ok(rec.Dto);
        }

        public async Task<ApiResponse> RecalculateRequestAsync(TransferCaller caller, int id, TransferDecisionRequest? decision)
        {
            var (rec, req, fail) = await LoadRequestAsync(caller, id, true);
            if (rec == null) return ApiResponse.Fail(fail!);
            var (built, bf) = await BuildAsync(Merge(req!, decision), null, requireReady: true);
            if (built == null) return ApiResponse.Fail(bf!);
            built.Result.Plan.RequiresApproval = false;
            return ApiResponse.Ok(built.Result.Plan);
        }

        public async Task<ApiResponse> ApproveRequestAsync(TransferCaller caller, int id, TransferDecisionRequest? decision, string? userEmail)
        {
            var (rec, req, fail) = await LoadRequestAsync(caller, id, true);
            if (rec == null) return ApiResponse.Fail(fail!);
            if (rec.Dto.Status != "Pending") return ApiResponse.Fail("This request has already been decided.");
            var merged = Merge(req!, decision);
            var (built, bf) = await BuildAsync(merged, null, requireReady: true);
            if (built == null) return ApiResponse.Fail(bf!);
            if (built.Result.HasErrors) return ApiResponse.Fail(string.Join(" ", built.Result.Plan.Errors), built.Result.Plan.Errors);
            if (string.IsNullOrWhiteSpace(merged.Note)) merged.Note = rec.Dto.Note ?? "Space transfer";

            var result = await RunAsync(built, merged, caller.UserId, userEmail, id);
            if (!result.IsSuccessful) return result;
            var note = string.IsNullOrWhiteSpace(decision?.Note) ? null : decision!.Note!.Trim();
            try { await _transferRepo.FinalizeApprovalAsync(id, caller.UserId, note, JsonSerializer.Serialize(merged, PlanJsonOptions), built.Result.Plan); }
            catch (Exception ex) { _logger.LogError(ex, "Transfer request {Id} was executed but the decision details could not be saved", id); }
            return result;
        }

        public async Task<ApiResponse> SendBackRequestAsync(TransferCaller caller, int id, string? note)
        {
            if (string.IsNullOrWhiteSpace(note)) return ApiResponse.Fail("Enter a note for the sales executive.");
            var (rec, _, fail) = await LoadRequestAsync(caller, id, true);
            if (rec == null) return ApiResponse.Fail(fail!);
            var n = note.Trim(); if (n.Length > 1000) n = n[..1000];
            if (!await _transferRepo.SendBackTransferRequestAsync(id, caller.UserId, n)) return ApiResponse.Fail("This request has already been decided.");
            return ApiResponse.Ok((await _transferRepo.GetTransferRequestAsync(id))?.Dto, "Sent back to the sales executive.");
        }

        public async Task<ApiResponse> GetAvailableTargetSpacesAsync(
            int bookingId, DateTime effectiveDate, IReadOnlyCollection<int>? allowedLocationIds)
        {
            var booking = await _transferRepo.GetBookingSummaryAsync(bookingId);
            if (booking == null)
                return ApiResponse.Fail($"Booking #{bookingId} not found.");

            if (allowedLocationIds != null && !allowedLocationIds.Contains(booking.LocationId))
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
    
        public async Task<ApiResponse> GetHistoryAsync(
            string? search, DateTime? from, DateTime? to, int page, int pageSize, IReadOnlyCollection<int>? allowedLocationIds)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 25 : Math.Min(pageSize, 200);
            if (allowedLocationIds != null && allowedLocationIds.Count == 0)
                return ApiResponse.Ok(new TransferHistoryPageDto { Page = page, PageSize = pageSize });
            if (from.HasValue && to.HasValue && from.Value > to.Value) (from, to) = (to, from);
            var result = await _transferRepo.ListTransferHistoryAsync(search, from, to, page, pageSize, allowedLocationIds);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetActiveBookingsAsync(
            int? locationId, string? search, IReadOnlyCollection<int>? allowedLocationIds)
        {
            if (allowedLocationIds != null)
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

            if (allowedLocationIds != null)
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

            if (allowedLocationIds != null && !allowedLocationIds.Contains(booking.LocationId))
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
                if (allowedLocationIds != null && userRole != "super_admin")
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
                if (allowedLocationIds != null && userRole != "super_admin")
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
                if (allowedLocationIds != null && userRole != "super_admin")
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

