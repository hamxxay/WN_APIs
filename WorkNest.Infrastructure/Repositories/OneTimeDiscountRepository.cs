using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.OneTimeDiscount;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    public class OneTimeDiscountRepository : IOneTimeDiscountRepository
    {
        private readonly string _connectionString;

        public OneTimeDiscountRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection string is missing.");
        }

        private static bool HasCol(SqlDataReader r, string name)
        {
            for (int i = 0; i < r.FieldCount; i++)
            {
                if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static async Task SetAuditSessionContextAsync(SqlConnection conn, int? userId, string? userEmail)
        {
            try
            {
                await using var cmd = new SqlCommand("dbo.WN_Audit_SetSessionContext", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };
                cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@UserEmail", (object?)userEmail ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SourceApp", "WorkNest.API.OneTimeDiscounts");
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignore if session context procedure is unavailable
            }
        }

        private static OneTimeDiscountDto MapItem(SqlDataReader r)
        {
            return new OneTimeDiscountDto
            {
                Id = r.GetInt32(r.GetOrdinal("Id")),
                BookingId = r.GetInt32(r.GetOrdinal("BookingId")),
                AppliesTo = r.GetString(r.GetOrdinal("AppliesTo")),
                TargetInvoiceId = r.IsDBNull(r.GetOrdinal("TargetInvoiceId")) ? null : r.GetInt32(r.GetOrdinal("TargetInvoiceId")),
                TargetMonth = r.IsDBNull(r.GetOrdinal("TargetMonth")) ? null : r.GetDateTime(r.GetOrdinal("TargetMonth")),
                DiscountType = r.GetString(r.GetOrdinal("DiscountType")),
                DiscountValue = r.GetDecimal(r.GetOrdinal("DiscountValue")),
                Reason = r.GetString(r.GetOrdinal("Reason")),
                Status = r.GetString(r.GetOrdinal("Status")),
                AppliedInvoiceId = r.IsDBNull(r.GetOrdinal("AppliedInvoiceId")) ? null : r.GetInt32(r.GetOrdinal("AppliedInvoiceId")),
                AppliedInvoiceNumber = HasCol(r, "AppliedInvoiceNumber") && !r.IsDBNull(r.GetOrdinal("AppliedInvoiceNumber"))
                    ? r.GetString(r.GetOrdinal("AppliedInvoiceNumber"))
                    : null,
                AppliedAt = r.IsDBNull(r.GetOrdinal("AppliedAt")) ? null : r.GetDateTime(r.GetOrdinal("AppliedAt")),
                AppliedDiscountAmount = HasCol(r, "AppliedDiscountAmount") && !r.IsDBNull(r.GetOrdinal("AppliedDiscountAmount"))
                    ? r.GetDecimal(r.GetOrdinal("AppliedDiscountAmount"))
                    : (HasCol(r, "PreviewDiscountAmount") && !r.IsDBNull(r.GetOrdinal("PreviewDiscountAmount"))
                        ? r.GetDecimal(r.GetOrdinal("PreviewDiscountAmount"))
                        : null),
                CreatedBy = r.GetInt32(r.GetOrdinal("CreatedBy")),
                CreatedByName = HasCol(r, "CreatedByName") && !r.IsDBNull(r.GetOrdinal("CreatedByName"))
                    ? r.GetString(r.GetOrdinal("CreatedByName"))
                    : null,
                CreatedByEmail = HasCol(r, "CreatedByEmail") && !r.IsDBNull(r.GetOrdinal("CreatedByEmail"))
                    ? r.GetString(r.GetOrdinal("CreatedByEmail"))
                    : null,
                CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
                CancelledBy = HasCol(r, "CancelledBy") && !r.IsDBNull(r.GetOrdinal("CancelledBy"))
                    ? r.GetInt32(r.GetOrdinal("CancelledBy"))
                    : null,
                CancelledByName = HasCol(r, "CancelledByName") && !r.IsDBNull(r.GetOrdinal("CancelledByName"))
                    ? r.GetString(r.GetOrdinal("CancelledByName"))
                    : null,
                CancelledAt = HasCol(r, "CancelledAt") && !r.IsDBNull(r.GetOrdinal("CancelledAt"))
                    ? r.GetDateTime(r.GetOrdinal("CancelledAt"))
                    : null
            };
        }

        private static OneTimeDiscountPreviewDto MapPreview(SqlDataReader r)
        {
            return new OneTimeDiscountPreviewDto
            {
                BookingId = r.GetInt32(r.GetOrdinal("BookingId")),
                AppliesTo = r.GetString(r.GetOrdinal("AppliesTo")),
                TargetInvoiceId = r.IsDBNull(r.GetOrdinal("TargetInvoiceId")) ? null : r.GetInt32(r.GetOrdinal("TargetInvoiceId")),
                TargetMonth = r.IsDBNull(r.GetOrdinal("TargetMonth")) ? null : r.GetDateTime(r.GetOrdinal("TargetMonth")),
                DiscountType = r.GetString(r.GetOrdinal("DiscountType")),
                DiscountValue = r.GetDecimal(r.GetOrdinal("DiscountValue")),
                MonthlyRent = r.GetDecimal(r.GetOrdinal("MonthlyRent")),
                BillingPeriodStart = r.IsDBNull(r.GetOrdinal("BillingPeriodStart")) ? null : r.GetDateTime(r.GetOrdinal("BillingPeriodStart")),
                BillingPeriodEnd = r.IsDBNull(r.GetOrdinal("BillingPeriodEnd")) ? null : r.GetDateTime(r.GetOrdinal("BillingPeriodEnd")),
                BillingPeriodMonths = r.GetInt32(r.GetOrdinal("BillingPeriodMonths")),
                EffectiveMonths = r.GetDecimal(r.GetOrdinal("EffectiveMonths")),
                MonthProrationShare = r.GetDecimal(r.GetOrdinal("MonthProrationShare")),
                ProratedDays = r.GetInt32(r.GetOrdinal("ProratedDays")),
                InvoiceGrossRent = r.GetDecimal(r.GetOrdinal("InvoiceGrossRent")),
                TargetBaseRent = r.GetDecimal(r.GetOrdinal("TargetBaseRent")),
                ExistingBookingDiscountAmount = r.GetDecimal(r.GetOrdinal("ExistingBookingDiscountAmount")),
                ExistingBookingDiscountPct = r.GetDecimal(r.GetOrdinal("ExistingBookingDiscountPct")),
                OneTimeDiscountAmount = r.GetDecimal(r.GetOrdinal("OneTimeDiscountAmount")),
                OneTimeEquivalentPct = r.GetDecimal(r.GetOrdinal("OneTimeEquivalentPct")),
                CombinedDiscountPct = r.GetDecimal(r.GetOrdinal("CombinedDiscountPct")),
                OfferingCapPct = r.GetDecimal(r.GetOrdinal("OfferingCapPct")),
                IsCapExceeded = r.GetBoolean(r.GetOrdinal("IsCapExceeded")),
                RoomRentExclTaxBefore = r.GetDecimal(r.GetOrdinal("RoomRentExclTaxBefore")),
                RoomRentExclTaxAfter = r.GetDecimal(r.GetOrdinal("RoomRentExclTaxAfter")),
                ServiceCharges = r.GetDecimal(r.GetOrdinal("ServiceCharges")),
                TaxOnServiceCharges = r.GetDecimal(r.GetOrdinal("TaxOnServiceCharges")),
                SecurityDepositAmount = r.GetDecimal(r.GetOrdinal("SecurityDepositAmount")),
                TotalDiscountAfter = r.GetDecimal(r.GetOrdinal("TotalDiscountAfter")),
                GrandTotalBefore = r.GetDecimal(r.GetOrdinal("GrandTotalBefore")),
                GrandTotalAfter = r.GetDecimal(r.GetOrdinal("GrandTotalAfter"))
            };
        }

        public async Task<List<OneTimeDiscountDto>> GetByBookingDbAsync(int bookingId, string? allowedLocationIds = null)
        {
            var list = new List<OneTimeDiscountDto>();
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_OneTimeDiscounts_GetByBooking", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            cmd.Parameters.AddWithValue("@AllowedLocationIds", (object?)allowedLocationIds ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(MapItem(r));
            }
            return list;
        }

        public async Task<OneTimeDiscountPreviewDto> PreviewDbAsync(
            OneTimeDiscountPreviewRequest request,
            string? allowedLocationIds = null,
            int? actorUserId = null,
            string? actorEmail = null)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await SetAuditSessionContextAsync(conn, actorUserId, actorEmail);

            await using var cmd = new SqlCommand("dbo.WN_OneTimeDiscounts_Preview", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@BookingId", request.BookingId);
            cmd.Parameters.AddWithValue("@AppliesTo", request.AppliesTo ?? "Invoice");
            cmd.Parameters.AddWithValue("@TargetInvoiceId", (object?)request.TargetInvoiceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TargetMonth", request.TargetMonth.HasValue
                ? new DateTime(request.TargetMonth.Value.Year, request.TargetMonth.Value.Month, 1)
                : DBNull.Value);
            cmd.Parameters.AddWithValue("@DiscountType", request.DiscountType ?? "Percentage");
            cmd.Parameters.AddWithValue("@DiscountValue", request.DiscountValue);
            cmd.Parameters.AddWithValue("@BillingPeriodStart", (object?)request.BillingPeriodStart ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BillingPeriodEnd", (object?)request.BillingPeriodEnd ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AllowedLocationIds", (object?)allowedLocationIds ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return MapPreview(r);
            }
            throw new InvalidOperationException("Failed to compute one-time discount preview.");
        }

        public async Task<OneTimeDiscountDto> CreateDbAsync(
            OneTimeDiscountCreateRequest request,
            int createdBy,
            string? allowedLocationIds = null,
            string? actorEmail = null)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await SetAuditSessionContextAsync(conn, createdBy, actorEmail);

            await using var cmd = new SqlCommand("dbo.WN_OneTimeDiscounts_Create", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@BookingId", request.BookingId);
            cmd.Parameters.AddWithValue("@AppliesTo", request.AppliesTo ?? "Invoice");
            cmd.Parameters.AddWithValue("@TargetInvoiceId", (object?)request.TargetInvoiceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TargetMonth", request.TargetMonth.HasValue
                ? new DateTime(request.TargetMonth.Value.Year, request.TargetMonth.Value.Month, 1)
                : DBNull.Value);
            cmd.Parameters.AddWithValue("@DiscountType", request.DiscountType ?? "Percentage");
            cmd.Parameters.AddWithValue("@DiscountValue", request.DiscountValue);
            cmd.Parameters.AddWithValue("@Reason", request.Reason ?? string.Empty);
            cmd.Parameters.AddWithValue("@CreatedBy", createdBy);
            cmd.Parameters.AddWithValue("@AllowedLocationIds", (object?)allowedLocationIds ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return MapItem(r);
            }
            throw new InvalidOperationException("Failed to create one-time discount.");
        }

        public async Task<OneTimeDiscountDto> CancelDbAsync(
            int id,
            int cancelledBy,
            string? allowedLocationIds = null,
            string? actorEmail = null)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await SetAuditSessionContextAsync(conn, cancelledBy, actorEmail);

            await using var cmd = new SqlCommand("dbo.WN_OneTimeDiscounts_Cancel", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@CancelledBy", cancelledBy);
            cmd.Parameters.AddWithValue("@AllowedLocationIds", (object?)allowedLocationIds ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return MapItem(r);
            }
            throw new InvalidOperationException("Failed to cancel one-time discount.");
        }

        public async Task<BookingOneTimeDiscountsSummaryDto> GetBookingSummaryDbAsync(int bookingId, string? allowedLocationIds = null)
        {
            var discounts = await GetByBookingDbAsync(bookingId, allowedLocationIds);

            // Baseline preview with 0% discount to extract cap, existing discount, and initial cycle dates
            var baseline = await PreviewDbAsync(new OneTimeDiscountPreviewRequest
            {
                BookingId = bookingId,
                AppliesTo = "Invoice",
                DiscountType = "Percentage",
                DiscountValue = 0m
            }, allowedLocationIds);

            var summary = new BookingOneTimeDiscountsSummaryDto
            {
                BookingId = bookingId,
                MonthlyRent = baseline.MonthlyRent,
                ExistingBookingDiscountPct = baseline.ExistingBookingDiscountPct,
                ExistingBookingDiscountAmount = baseline.ExistingBookingDiscountAmount,
                OfferingCapPct = baseline.OfferingCapPct,
                RemainingCapPct = Math.Max(0m, baseline.OfferingCapPct - baseline.ExistingBookingDiscountPct),
                Discounts = discounts
            };

            // Load booking contract dates & existing invoices to populate upcoming invoice & month pickers
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            DateTime startOn = DateTime.Today;
            DateTime endOn = DateTime.Today.AddMonths(12);
            int billingMonths = 1;
            int existingInvCount = 0;
            DateTime? latestInvPeriodEnd = null;

            const string metaSql = @"
                SELECT TOP 1
                    CAST(b.StartOn AS DATE) AS StartOn,
                    CAST(b.EndOn AS DATE)   AS EndOn,
                    ISNULL(NULLIF(b.BillingPeriodMonths, 0), 1) AS BillingPeriodMonths,
                    (SELECT COUNT(*) FROM dbo.WN_Invoices i WITH (NOLOCK)
                     WHERE i.BookingId = b.Id AND i.InvoiceTypeId <> 7 AND ISNULL(i.IsCustom, 0) = 0 AND ISNULL(i.StatusId, 0) NOT IN (5, 40)) AS ExistingInvCount,
                    (SELECT MAX(CAST(ISNULL(i.BillingPeriodEnd, i.DueOn) AS DATE)) FROM dbo.WN_Invoices i WITH (NOLOCK)
                     WHERE i.BookingId = b.Id AND i.InvoiceTypeId <> 7 AND ISNULL(i.IsCustom, 0) = 0 AND ISNULL(i.StatusId, 0) NOT IN (5, 40)) AS LatestPeriodEnd
                FROM dbo.WN_Bookings b WITH (NOLOCK)
                WHERE b.Id = @BookingId;";

            await using (var cmd = new SqlCommand(metaSql, conn))
            {
                cmd.Parameters.AddWithValue("@BookingId", bookingId);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    if (!r.IsDBNull(0)) startOn = r.GetDateTime(0);
                    if (!r.IsDBNull(1)) endOn = r.GetDateTime(1);
                    if (!r.IsDBNull(2)) billingMonths = r.GetInt32(2);
                    if (!r.IsDBNull(3)) existingInvCount = r.GetInt32(3);
                    if (!r.IsDBNull(4)) latestInvPeriodEnd = r.GetDateTime(4);
                }
            }

            // Compute initial advance period
            var firstOfStartMonth = new DateTime(startOn.Year, startOn.Month, 1);
            DateTime initStart = startOn.Day == 1 ? startOn : startOn.AddDays(1);
            DateTime initEnd = (startOn.Day > 1 && startOn.Day >= 15)
                ? firstOfStartMonth.AddMonths(billingMonths + 1).AddDays(-1)
                : firstOfStartMonth.AddMonths(billingMonths).AddDays(-1);
            if (initEnd > endOn) initEnd = endOn;

            // Build Upcoming Invoices list
            int nextSeq = existingInvCount + 1;
            DateTime nextStart = existingInvCount == 0
                ? initStart
                : (latestInvPeriodEnd?.AddDays(1) ?? initEnd.AddDays(1));
            DateTime nextEnd = existingInvCount == 0
                ? initEnd
                : new DateTime(nextStart.Year, nextStart.Month, DateTime.DaysInMonth(nextStart.Year, nextStart.Month));
            if (nextEnd > endOn) nextEnd = endOn;

            if (nextStart <= endOn)
            {
                summary.UpcomingInvoices.Add(new UpcomingInvoiceOptionDto
                {
                    TargetInvoiceId = null,
                    Label = $"Next Upcoming Invoice (#{nextSeq}: {nextStart:MMM d, yyyy} – {nextEnd:MMM d, yyyy})",
                    PeriodStart = nextStart,
                    PeriodEnd = nextEnd,
                    IsInitialInvoice = existingInvCount == 0
                });
            }

            // Add numbered upcoming invoice cycles up to contract end (max 24 cycles)
            DateTime curStart = nextStart;
            int seq = nextSeq;
            int safety = 0;
            while (curStart <= endOn && safety++ < 24)
            {
                DateTime curEnd = (seq == 1 && existingInvCount == 0)
                    ? initEnd
                    : new DateTime(curStart.Year, curStart.Month, DateTime.DaysInMonth(curStart.Year, curStart.Month));
                if (curEnd > endOn) curEnd = endOn;

                summary.UpcomingInvoices.Add(new UpcomingInvoiceOptionDto
                {
                    TargetInvoiceId = seq,
                    Label = seq == 1 && existingInvCount == 0
                        ? $"Invoice #1 (Initial Advance: {curStart:MMM d, yyyy} – {curEnd:MMM d, yyyy})"
                        : $"Invoice #{seq} (Recurring: {curStart:MMM d, yyyy} – {curEnd:MMM d, yyyy})",
                    PeriodStart = curStart,
                    PeriodEnd = curEnd,
                    IsInitialInvoice = seq == 1 && existingInvCount == 0
                });

                curStart = curEnd.AddDays(1);
                seq++;
            }

            // Build Contract Months list
            DateTime monthCursor = firstOfStartMonth;
            DateTime lastMonth = new DateTime(endOn.Year, endOn.Month, 1);
            int monthSafety = 0;
            while (monthCursor <= lastMonth && monthSafety++ < 36)
            {
                bool isFirstProrated = monthCursor == firstOfStartMonth && startOn.Day > 1;
                int proratedDays = isFirstProrated ? (30 - Math.Min(startOn.Day, 30)) : 30;
                DateTime monthEnd = new DateTime(monthCursor.Year, monthCursor.Month, DateTime.DaysInMonth(monthCursor.Year, monthCursor.Month));
                bool alreadyInvoiced = latestInvPeriodEnd.HasValue && monthEnd <= latestInvPeriodEnd.Value;

                summary.ContractMonths.Add(new ContractMonthOptionDto
                {
                    TargetMonth = monthCursor.ToString("yyyy-MM-01"),
                    Label = isFirstProrated
                        ? $"{monthCursor:MMMM yyyy} ({proratedDays}/30 days prorated)"
                        : $"{monthCursor:MMMM yyyy}",
                    IsProrated = isFirstProrated,
                    ProratedDays = proratedDays,
                    IsAlreadyInvoiced = alreadyInvoiced
                });

                monthCursor = monthCursor.AddMonths(1);
            }

            return summary;
        }
    }
}
