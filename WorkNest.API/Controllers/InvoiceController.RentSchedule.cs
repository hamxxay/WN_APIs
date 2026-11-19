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

        /// <summary>Overwrites the engine's figures with the rows' sums, so the preview, the lines and the invoice all follow the table.</summary>
        private static void ApplyScheduleToCalc(InvoiceCalculationResult calc, ScheduleInvoiceSource src)
        {
            var f = src.Figures;
            calc.IsProrated = false;
            calc.ProratedDays = 0;
            calc.BillingPeriodStart = f.PeriodStart;
            calc.BillingPeriodEnd = f.PeriodEnd;
            calc.BillingPeriodMonths = f.Months;
            calc.Rent = f.Rent;
            calc.Discount = f.Discount;
            calc.ManualDiscount = f.Discount;
            calc.OfferDiscount = 0m;
            calc.ServiceCharge = f.ServiceCharge;
            calc.Tax = f.Tax;
            calc.DepositBase = f.Deposit;
            calc.DepositDiscount = 0m;
            calc.DepositAfterDiscount = f.Deposit;
            calc.DepositInstallments = 1;
            calc.DepositFirstInstallment = f.Deposit;
            calc.GrandTotal = f.GrandTotal;
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
            var result = await CreateCustomInvoice(dto);
            await MarkScheduleRowsAsync(svc, bookingId, src, result);
            return result;
        }

        private static async Task MarkScheduleRowsAsync(IBookingScheduleService svc, int bookingId, ScheduleInvoiceSource src, IActionResult result)
        {
            var invoiceId = RentScheduleInvoiceShape.InvoiceIdOf(result);
            if (invoiceId == null) return;
            if (!await svc.MarkInvoicedAsync(bookingId, src.Figures.RowNos, invoiceId.Value))
                Console.WriteLine($"[RentSchedule] invoice {invoiceId} for booking {bookingId} was created but rows {string.Join(",", src.Figures.RowNos)} could not be linked to it.");
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
            var result = await CreateCustomInvoice(dto);
            await MarkScheduleRowsAsync(svc, bookingId, next, result);
            return result;
        }
    }
}
