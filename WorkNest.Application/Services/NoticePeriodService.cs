using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.NoticePeriod;
using WorkNest.Application.DTOs.Announcement;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class NoticePeriodService : INoticePeriodService
    {
        private readonly INoticePeriodRepository _repository;
        private readonly IEmailService _email;
        private readonly IAnnouncementService _announcement;
        private readonly IDbRepository _db;
        private readonly ILogger<NoticePeriodService> _logger;

        public NoticePeriodService(
            INoticePeriodRepository repository,
            IEmailService email,
            IAnnouncementService announcement,
            IDbRepository db,
            ILogger<NoticePeriodService> logger)
        {
            _repository = repository;
            _email = email;
            _announcement = announcement;
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse> SubmitNoticeAsync(int bookingId, int userId, string? note)
        {
            if (bookingId <= 0)
                return ApiResponse.Fail("A valid booking ID is required.");

            try
            {
                var result = await _repository.SubmitNoticeDbAsync(bookingId, userId, "Mobile", note);
                if (result == null)
                    return ApiResponse.Fail("Failed to submit notice period request.");

                // Notify staff about new mobile notice request
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var html = $@"
                            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px;'>
                                <h2 style='color: #1e293b; margin-top: 0;'>New Notice Period Leave Request</h2>
                                <p>A customer has submitted a leave request for booking <strong>#{bookingId}</strong>.</p>
                                <ul>
                                    <li><strong>Space:</strong> {result.SpaceName}</li>
                                    <li><strong>Location:</strong> {result.LocationName}</li>
                                    <li><strong>Customer:</strong> {result.CustomerName} ({result.CustomerCode})</li>
                                    <li><strong>Requested On:</strong> {result.RequestedOn:yyyy-MM-dd HH:mm}</li>
                                    <li><strong>Notice Days:</strong> {result.NoticeDays} days (Calculated Exit Date: {result.ExitDate:yyyy-MM-dd})</li>
                                </ul>
                                <p>Please review and issue or decline the notice in the Staff Portal.</p>
                            </div>";

                        // Send email to staff / admin
                        await _email.SendTypedEmailAsync(EmailTypes.NoticeRequest, "admin@worknestpk.com", $"[Notice Request] New leave request for Booking #{bookingId}", html, "WorkNest Notices");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send staff email notification for Notice request on Booking #{BookingId}", bookingId);
                    }
                });

                return ApiResponse.Ok(result, "Notice period request submitted successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting notice period request for Booking #{BookingId}", bookingId);
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> WithdrawNoticeAsync(int noticeId, int userId)
        {
            if (noticeId <= 0)
                return ApiResponse.Fail("Invalid notice ID.");

            try
            {
                var result = await _repository.WithdrawNoticeDbAsync(noticeId, userId);
                if (result == null)
                    return ApiResponse.Fail("Notice period request could not be withdrawn.");

                return ApiResponse.Ok(result, "Notice period request withdrawn successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error withdrawing notice #{NoticeId}", noticeId);
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> GetCustomerNoticesAsync(int userId)
        {
            try
            {
                var items = await _repository.GetCustomerNoticesDbAsync(userId);
                return ApiResponse.Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching customer notices for User #{UserId}", userId);
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> GetStaffNoticesAsync(int? locationId, string? status, string? allowedLocationIds)
        {
            try
            {
                var items = await _repository.GetStaffNoticesDbAsync(locationId, status, allowedLocationIds);
                return ApiResponse.Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching staff notices");
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> IssueNoticeAsync(StaffIssueNoticeRequest request, int staffUserId)
        {
            if (request == null)
                return ApiResponse.Fail("Invalid request body.");

            if (!request.NoticeId.HasValue && !request.BookingId.HasValue)
                return ApiResponse.Fail("Either NoticeId or BookingId must be provided.");

            try
            {
                var result = await _repository.IssueNoticeDbAsync(
                    request.NoticeId, request.BookingId, request.NoticeDate, request.NoticeDays, request.StaffNote, staffUserId);

                if (result == null)
                    return ApiResponse.Fail("Failed to issue notice period.");

                // Notify customer via push + email
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(result.CustomerEmail))
                        {
                            var html = $@"
                                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px;'>
                                    <h2 style='color: #1e293b; margin-top: 0;'>Notice Period Confirmed</h2>
                                    <p>Dear {result.CustomerName ?? "Customer"},</p>
                                    <p>Your notice period for space <strong>{result.SpaceName}</strong> at <strong>{result.LocationName}</strong> has been issued.</p>
                                    <ul>
                                        <li><strong>Notice Date:</strong> {result.NoticeDate:yyyy-MM-dd}</li>
                                        <li><strong>Notice Duration:</strong> {result.NoticeDays} days</li>
                                        <li><strong>Final Exit Date:</strong> {result.ExitDate:yyyy-MM-dd}</li>
                                    </ul>
                                    <p>Please note that your final settlement and security deposit reconciliation will be processed after your exit date ({result.ExitDate:yyyy-MM-dd}).</p>
                                    <p>Thank you for choosing WorkNest.</p>
                                </div>";

                            await _email.SendTypedEmailAsync(EmailTypes.NoticeConfirmed, result.CustomerEmail, $"[WorkNest] Notice Period Confirmed - Exit Date: {result.ExitDate:yyyy-MM-dd}", html, "WorkNest Reports");
                        }

                        // Push Notification / In-App Alert
                        await _announcement.CreateAnnouncementAsync(new CreateAnnouncementRequest
                        {
                            Title = "Notice Period Confirmed",
                            Body = $"Your notice period has been confirmed. Exit date is {result.ExitDate:yyyy-MM-dd}.",
                            Type = "Alert",
                            TargetScope = "CustomList",
                            CustomUserIds = new List<int> { result.UserId }
                        }, staffUserId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send customer notification for issued notice #{NoticeId}", result.Id);
                    }
                });

                return ApiResponse.Ok(result, "Notice period issued successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error issuing notice");
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> DeclineNoticeAsync(int noticeId, string reason, int staffUserId)
        {
            if (noticeId <= 0) return ApiResponse.Fail("Invalid notice ID.");
            if (string.IsNullOrWhiteSpace(reason)) return ApiResponse.Fail("Decline reason is required.");

            try
            {
                var result = await _repository.DeclineNoticeDbAsync(noticeId, reason.Trim(), staffUserId);
                if (result == null) return ApiResponse.Fail("Failed to decline notice.");

                // Notify customer via push + email
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var customer = await _db.GetUserByIdAsync(result.UserId);
                        var email = customer != null && customer.ContainsKey("Email") ? customer["Email"]?.ToString() : null;
                        var name = customer != null && customer.ContainsKey("Name") ? customer["Name"]?.ToString() : "Customer";

                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            var html = $@"
                                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px;'>
                                    <h2 style='color: #1e293b; margin-top: 0;'>Notice Period Request Declined</h2>
                                    <p>Dear {name},</p>
                                    <p>Your notice period request for booking #{result.BookingId} could not be approved at this time.</p>
                                    <p><strong>Reason:</strong> {reason}</p>
                                    <p>Please contact your location manager for any questions.</p>
                                </div>";

                            await _email.SendTypedEmailAsync(EmailTypes.NoticeDeclined, email, "[WorkNest] Notice Period Request Declined", html, "WorkNest Reports");
                        }

                        // Push / In-App Alert
                        await _announcement.CreateAnnouncementAsync(new CreateAnnouncementRequest
                        {
                            Title = "Notice Request Declined",
                            Body = $"Your notice period request was declined: {reason}",
                            Type = "Alert",
                            TargetScope = "CustomList",
                            CustomUserIds = new List<int> { result.UserId }
                        }, staffUserId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send decline notification for notice #{NoticeId}", noticeId);
                    }
                });

                return ApiResponse.Ok(result, "Notice request declined.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error declining notice #{NoticeId}", noticeId);
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> UpdateNoticeDateAsync(int noticeId, StaffUpdateNoticeDateRequest request, int staffUserId)
        {
            if (noticeId <= 0) return ApiResponse.Fail("Invalid notice ID.");
            if (request == null) return ApiResponse.Fail("Invalid request body.");

            try
            {
                var result = await _repository.UpdateNoticeDateDbAsync(noticeId, request.NoticeDate, request.StaffNote, staffUserId);
                if (result == null) return ApiResponse.Fail("Failed to update notice date.");

                return ApiResponse.Ok(result, "Notice date updated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating notice date for #{NoticeId}", noticeId);
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> GetSettlementPreCheckAsync(int noticeId)
        {
            if (noticeId <= 0) return ApiResponse.Fail("Invalid notice ID.");

            try
            {
                var preCheck = await _repository.GetSettlementPreCheckDbAsync(noticeId);
                if (preCheck == null) return ApiResponse.Fail("Notice record not found.");

                return ApiResponse.Ok(preCheck);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching settlement pre-check for notice #{NoticeId}", noticeId);
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> SaveSettlementAsync(SaveNoticeSettlementRequest request, int staffUserId)
        {
            if (request == null) return ApiResponse.Fail("Invalid request body.");
            if (request.NoticeId <= 0) return ApiResponse.Fail("Valid NoticeId is required.");
            if (request.PayoutAccountId <= 0) return ApiResponse.Fail("Payout account is required.");
            if (request.Lines == null || request.Lines.Count == 0) return ApiResponse.Fail("At least one deduction line is required.");

            foreach (var l in request.Lines)
            {
                if (string.IsNullOrWhiteSpace(l.Description)) return ApiResponse.Fail("Line description is required.");
                if (l.Amount < 0) return ApiResponse.Fail("Line amount cannot be negative.");
            }

            try
            {
                var result = await _repository.SaveSettlementDbAsync(request, staffUserId);
                if (result == null) return ApiResponse.Fail("Failed to save notice settlement.");

                return ApiResponse.Ok(result, "Notice settlement saved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving notice settlement for notice #{NoticeId}", request.NoticeId);
                return ApiResponse.Fail(ex.Message);
            }
        }
    }
}

