using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Services;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Rent schedule (CQ_01): a booking that has WN_BookingSchedule rows is invoiced from them. The first invoice takes the rows of the first-invoice
    /// period, "generate next invoice" the next un-invoiced row; both through the existing CreateCustomInvoice, and the invoice is stored on the rows
    /// under a per-booking lock (<see cref="RentScheduleLock"/>) so a row is never billed twice. A booking without rows (or before CQ_01) takes the old code, untouched.
    /// </summary>
    public partial class InvoiceController
    {
        private IBookingScheduleService? ScheduleService => HttpContext?.RequestServices.GetService<IBookingScheduleService>();

        /// <summary>The rows of the first invoice, or null (no table / meeting room / nothing to take): the old calculation stands.</summary>
        private async Task<ScheduleInvoiceSource?> TryScheduleFirstInvoiceAsync(int bookingId, bool isMeetingRoom)
        {
            if (isMeetingRoom) return null;
            var svc = ScheduleService;
            return svc == null ? null : await svc.GetFirstInvoiceSourceAsync(bookingId);
        }

        /// <summary>CreateCustomInvoice for a first invoice made from rows: under the lock, after checking the rows are still the ones it was figured from, then the rows get the invoice.</summary>
        private async Task<IActionResult> CreateFirstInvoiceFromScheduleAsync(CreateCustomInvoiceDto dto, int bookingId, ScheduleInvoiceSource src)
        {
            var svc = ScheduleService!;
            await using var guard = await RentScheduleLock.AcquireAsync(GetConnectionString(), bookingId);
            if (guard == null)
                return Conflict(new { isSuccessful = false, message = "This booking is already being invoiced - try again in a moment." });
            var fresh = await svc.GetFirstInvoiceSourceAsync(bookingId);
            if (fresh == null || !fresh.Figures.RowNos.SequenceEqual(src.Figures.RowNos) || fresh.Figures.GrandTotal != src.Figures.GrandTotal)
                return Conflict(new { isSuccessful = false, message = "The rent schedule changed while the invoice was being prepared. Open the first invoice again." });
            return await CreateAndLinkAsync(svc, dto, bookingId, src);
        }

        /// <summary>
        /// Creates the invoice and stores it on the rows. If the period already has a live invoice (an earlier create whose link failed) that invoice is
        /// linked instead and nothing is created. If the link fails after the create it is retried through the period lookup; if that fails too the
        /// answer is an error (500), never a 200 that would let the next call bill the month again.
        /// </summary>
        private async Task<IActionResult> CreateAndLinkAsync(IBookingScheduleService svc, CreateCustomInvoiceDto dto, int bookingId, ScheduleInvoiceSource src)
        {
            var f = src.Figures;
            var existing = await svc.FindLiveInvoiceForPeriodAsync(bookingId, f.PeriodStart, f.PeriodEnd);
            if (existing != null)
            {
                if (!await svc.MarkInvoicedAsync(bookingId, f.RowNos, existing.Value))
                    return StatusCode(500, new { isSuccessful = false, message = $"Invoice {existing} already covers this period but could not be linked to the rent schedule. Nothing new was created." });
                return Ok(new { isSuccessful = true, message = "This period was already invoiced; the invoice is now linked to the rent schedule.", invoiceId = existing.Value });
            }

            var result = await CreateCustomInvoice(dto);
            var invoiceId = RentScheduleInvoiceShape.InvoiceIdOf(result);
            if (invoiceId == null) return result;
            bool linked = false;
            try { linked = await svc.MarkInvoicedAsync(bookingId, f.RowNos, invoiceId.Value); } catch (Exception ex) { Console.WriteLine($"[RentSchedule] link failed: {ex.Message}"); }
            if (!linked)
            {
                try
                {
                    var found = await svc.FindLiveInvoiceForPeriodAsync(bookingId, f.PeriodStart, f.PeriodEnd) ?? invoiceId;
                    linked = await svc.MarkInvoicedAsync(bookingId, f.RowNos, found.Value);
                }
                catch (Exception ex) { Console.WriteLine($"[RentSchedule] link retry failed: {ex.Message}"); }
            }
            if (!linked)
                return StatusCode(500, new { isSuccessful = false, invoiceId = invoiceId.Value,
                    message = $"Invoice {invoiceId} was created but could not be linked to the rent schedule rows. Do not create it again: calling generate next invoice again links it." });
            return result;
        }

        /// <summary>
        /// "Generate next invoice" for a booking with a table: the next un-invoiced row. Null = the booking has no table (the old code runs);
        /// otherwise the answer of CreateCustomInvoice, or a 400 when there is nothing to bill.
        /// </summary>
        private async Task<IActionResult?> TryGenerateNextFromScheduleAsync(int bookingId, int userId, string spaceName, int secMonths)
        {
            var svc = ScheduleService;
            if (svc == null) return null;
            if (!(await svc.GetNextInvoiceSourceAsync(bookingId)).HasSchedule) return null;   // no lock for the many bookings without a table
            await using var guard = await RentScheduleLock.AcquireAsync(GetConnectionString(), bookingId);
            if (guard == null)
                return Conflict(new { isSuccessful = false, message = "The next invoice for this booking is already being created - try again in a moment." });
            var (has, next) = await svc.GetNextInvoiceSourceAsync(bookingId);
            if (!has) return null;
            if (next == null)
                return BadRequest(new { isSuccessful = false, message = "Every month of this booking's rent schedule is already invoiced; there is no further period to invoice." });
            if (!next.AnyRowInvoiced)
                return BadRequest(new { isSuccessful = false, message = "Send the first invoice first: the rent schedule has no invoiced month yet." });
            var dto = RentScheduleInvoiceShape.NextInvoiceDto(next, bookingId, userId, spaceName, secMonths, _clock.Today, InvoiceDueDays);
            return await CreateAndLinkAsync(svc, dto, bookingId, next);
        }
    }
}
