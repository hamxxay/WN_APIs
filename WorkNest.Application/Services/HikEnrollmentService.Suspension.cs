using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Challan-based access suspension. WN_HIK_AccessSuspension_Run decides which bookings must change
    /// (overdue unpaid challan → block; paid or manually extended → unblock); this pushes that to the
    /// machines and records what was applied. Existing tables are only read — the suspension state lives
    /// in WN_HIK_BookingAccessSuspensions / WN_HIK_BookingAccessOverrides.
    /// </summary>
    public partial class HikEnrollmentService : IHikAccessSuspensionService
    {
        public async Task<int> RunAccessSuspensionCycleAsync(CancellationToken cancellationToken = default)
        {
            var changes = (await _db.RunHikAccessSuspensionDbAsync()).ToList();
            var applied = 0;

            foreach (var change in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await ApplyBookingSuspensionAsync(change.BookingId, change.ShouldBlock))
                    {
                        await _db.SetHikAccessSuspensionAppliedDbAsync(change.SuspensionId, change.ShouldBlock);
                        applied++;
                        _logger.LogInformation("Challan access suspension: booking {BookingId} {Action} on the machines.",
                            change.BookingId, change.ShouldBlock ? "blocked" : "unblocked");
                    }
                    else
                    {
                        _logger.LogWarning("Challan access suspension: booking {BookingId} not fully applied; will retry next cycle.", change.BookingId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Challan access suspension failed for booking {BookingId}; will retry next cycle.", change.BookingId);
                }
            }
            return applied;
        }

        public async Task<HikAccessSuspensionDto> GetAccessSuspensionAsync(int bookingDetailId)
        {
            var row = await _db.GetHikAccessSuspensionByBookingDetailDbAsync(bookingDetailId);
            if (row == null) return new HikAccessSuspensionDto { Open = false };

            var overrideUntil = row["OverrideUntil"] as DateTime?;
            var extended = overrideUntil.HasValue && overrideUntil.Value.Date >= DateTime.Today;
            return new HikAccessSuspensionDto
            {
                Open = true,
                Suspended = !extended,
                Extended = extended,
                SuspensionId = row["SuspensionId"] != null ? Convert.ToInt32(row["SuspensionId"]) : null,
                BookingId = row["BookingId"] != null ? Convert.ToInt32(row["BookingId"]) : null,
                Reason = Convert.ToString(row["Reason"]),
                SuspendedAt = (row["SuspendedAt"] as DateTime?)?.ToString("yyyy-MM-ddTHH:mm:ss"),
                InvoiceNumber = Convert.ToString(row["InvoiceNumber"]),
                DueOn = (row["DueOn"] as DateTime?)?.ToString("yyyy-MM-dd"),
                GrandTotal = row["GrandTotal"] != null ? Convert.ToDecimal(row["GrandTotal"]) : null,
                BalanceDue = row["BalanceDue"] != null ? Convert.ToDecimal(row["BalanceDue"]) : null,
                OverrideUntil = overrideUntil?.ToString("yyyy-MM-dd"),
                OverrideByEmail = Convert.ToString(row["OverrideByEmail"]),
                OverrideReason = Convert.ToString(row["OverrideReason"])
            };
        }

        public async Task<HikBookingChallansDto> GetBookingChallansAsync(int bookingDetailId)
        {
            var sets = await _db.GetBookingChallansByBookingDetailDbAsync(bookingDetailId);
            var booking = sets.Count > 0 ? sets[0].FirstOrDefault() : null;
            var result = new HikBookingChallansDto();
            if (booking == null) return result;

            result.BookingId = Convert.ToInt32(booking["BookingId"]);
            result.ChallanNumber = Convert.ToString(booking["ChallanNumber"]);
            result.ChallanValidUntil = (booking["ValidityDate"] as DateTime?)?.ToString("yyyy-MM-dd");

            var today = DateTime.Today;
            foreach (var r in sets.Count > 1 ? sets[1] : new List<IDictionary<string, object?>>())
            {
                var statusId = r["StatusId"] != null ? Convert.ToInt32(r["StatusId"]) : 0;
                var due = r["DueOn"] as DateTime?;
                var total = Convert.ToDecimal(r["GrandTotal"]);
                var paid = Convert.ToDecimal(r["PaidTotal"]);
                // Same rule as WN_HIK_AccessSuspension_Run: Unpaid (1) / Overdue (4) past the due date.
                var overdue = (statusId == 1 || statusId == 4) && due.HasValue && due.Value.Date < today;
                var typeId = r["InvoiceTypeId"] != null ? Convert.ToInt32(r["InvoiceTypeId"]) : 0;

                result.Challans.Add(new HikChallanDto
                {
                    Id = Convert.ToInt32(r["Id"]),
                    InvoiceNumber = Convert.ToString(r["InvoiceNumber"]),
                    Type = typeId switch { 1 => "Standard", 2 => "Advance", 3 => "Recurring", 4 => "Custom", 5 => "Surcharge", _ => "Invoice" },
                    IssuedOn = (r["IssuedOn"] as DateTime?)?.ToString("yyyy-MM-dd"),
                    DueOn = due?.ToString("yyyy-MM-dd"),
                    PeriodStart = (r["BillingPeriodStart"] as DateTime?)?.ToString("yyyy-MM-dd"),
                    PeriodEnd = (r["BillingPeriodEnd"] as DateTime?)?.ToString("yyyy-MM-dd"),
                    GrandTotal = total,
                    PaidTotal = paid,
                    BalanceDue = Math.Max(0, total - paid),
                    Status = overdue ? "Overdue" : statusId switch { 2 => "Paid", 3 => "Partial", 4 => "Overdue", _ => "Unpaid" },
                    IsOverdue = overdue
                });
            }
            // Booking challans (WN_Challans): the voucher issued at booking time, paid via WN_Payments.
            var bookingChallans = new List<HikChallanDto>();
            foreach (var r in sets.Count > 2 ? sets[2] : new List<IDictionary<string, object?>>())
            {
                decimal Dec(string k) => r.TryGetValue(k, out var v) && v != null ? Convert.ToDecimal(v) : 0m;
                var challanStatus = r["ChallanStatusId"] != null ? Convert.ToInt32(r["ChallanStatusId"]) : 1;
                var paymentStatus = r["PaymentStatusId"] != null ? Convert.ToInt32(r["PaymentStatusId"]) : 1;
                var validUntil = r["ValidUntil"] as DateTime?;
                var total = Dec("TotalContractAmount") > 0 ? Dec("TotalContractAmount") : Dec("VoucherAmount");
                var paid = Dec("TotalPaidAmount");
                var balance = r["BalanceLeft"] != null ? Math.Max(0, Dec("BalanceLeft")) : Math.Max(0, total - paid);

                string status;
                if (challanStatus is 3 or 4 || paymentStatus == 5) status = "Cancelled";
                else if (paymentStatus == 2 || (total > 0 && balance == 0)) status = "Paid";
                else if (paymentStatus == 3 || paid > 0) status = "Partial";
                else if (validUntil.HasValue && validUntil.Value.Date < today) status = "Expired";
                else status = "Unpaid";

                bookingChallans.Add(new HikChallanDto
                {
                    Id = Convert.ToInt32(r["Id"]),
                    InvoiceNumber = Convert.ToString(r["ChallanNumber"]),
                    Type = "Booking challan",
                    IssuedOn = (r["IssuedOn"] as DateTime?)?.ToString("yyyy-MM-dd"),
                    DueOn = validUntil?.ToString("yyyy-MM-dd"),
                    GrandTotal = total,
                    PaidTotal = paid,
                    BalanceDue = status is "Paid" or "Cancelled" ? 0 : balance,
                    Status = status,
                    IsOverdue = status == "Expired" // same rule as WN_HIK_AccessSuspension_Run (v2)
                });
            }
            result.Challans.InsertRange(0, bookingChallans);

            // The booking challan's balance is the whole booking's balance, which already covers its invoices —
            // count invoices when there are any, otherwise the booking challan (never both).
            var invoices = result.Challans.Where(c => c.Type != "Booking challan").ToList();
            var balanceSource = invoices.Count > 0 ? invoices : bookingChallans;
            result.TotalBalanceDue = balanceSource.Where(c => c.Status is not ("Paid" or "Cancelled")).Sum(c => c.BalanceDue);
            result.OverdueCount = result.Challans.Count(c => c.IsOverdue);
            return result;
        }

        /// <summary>
        /// Manual extension: access is allowed through the chosen date even though the challan is unpaid.
        /// Unblocks the booking's people right away; the hourly cycle re-suspends after that date if still unpaid.
        /// </summary>
        public async Task<HikAccessSyncResultDto> ExtendAccessAsync(int bookingDetailId, HikAccessExtendRequest request, string? createdByEmail)
        {
            var result = new HikAccessSyncResultDto { Enabled = true };
            var reason = (request?.Reason ?? "").Trim();
            if (!DateTime.TryParse(request?.OverrideUntil, out var until))
                return Error(result, "Choose the date access is extended until.");
            if (until.Date < DateTime.Today)
                return Error(result, "The extension date cannot be in the past.");
            if (reason.Length < 3 || reason.Length > 500)
                return Error(result, "Enter a reason for the extension (3–500 characters).");

            var (suspensionId, bookingId) = await _db.ExtendHikAccessSuspensionDbAsync(bookingDetailId, until.Date, reason, null, createdByEmail);
            if (suspensionId == null || bookingId == null)
                return Error(result, "Access for this booking is not suspended.");

            // Apply now instead of waiting for the hourly cycle.
            if (await ApplyBookingSuspensionAsync(bookingId.Value, shouldBlock: false, result))
                await _db.SetHikAccessSuspensionAppliedDbAsync(suspensionId.Value, false);
            return result;
        }

        /// <summary>
        /// Blocks (or restores) every enrolled person on every booked space of the booking.
        /// Restoring respects each person's own Access Status tick. Returns true when every machine was
        /// updated or queued (so the state can be recorded as applied).
        /// </summary>
        private async Task<bool> ApplyBookingSuspensionAsync(int bookingId, bool shouldBlock, HikAccessSyncResultDto? report = null)
        {
            var allApplied = true;
            foreach (var bookingDetailId in await _db.GetBookingDetailIdsForBookingDbAsync(bookingId))
            {
                var people = (await _db.GetHikAttendantContextsDbAsync(bookingDetailId, null)).ToList();
                var enrolled = people.Where(p => !string.IsNullOrWhiteSpace(p.EmployeeNo)).ToList();
                if (report != null)
                {
                    report.People += enrolled.Count;
                    report.NotEnrolled += people.Count - enrolled.Count;
                }
                if (enrolled.Count == 0) continue;

                var lines = await ApplyMachineAccessAsync(enrolled, p => !shouldBlock && p.IsEnabled);
                if (lines.Any(l => !l.Ok && !l.Queued)) allApplied = false;
                report?.Devices.AddRange(lines);
            }
            return allApplied;
        }

        private static HikAccessSyncResultDto Error(HikAccessSyncResultDto result, string message)
        {
            result.Error = message;
            return result;
        }
    }
}
