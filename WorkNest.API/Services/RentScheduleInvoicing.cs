using System.Data;
using Microsoft.Data.SqlClient;
using WorkNest.API.Controllers;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Application.Services.BookingSchedules;
using WorkNest.Infrastructure.Repositories;

namespace WorkNest.API.Services
{
    /// <summary>
    /// A session application lock per booking that every rent-schedule billing path takes (first invoice, "generate next invoice", the
    /// Billing Automation job), so a row is never billed twice even when two different paths run together. Dispose releases it.
    /// </summary>
    public sealed class RentScheduleLock : IAsyncDisposable
    {
        private readonly SqlConnection _conn;
        private readonly string _resource;

        private RentScheduleLock(SqlConnection conn, string resource) { _conn = conn; _resource = resource; }

        public static string ResourceFor(int bookingId) => $"wn-rent-schedule-{bookingId}";

        /// <summary>Null when the lock could not be had within the timeout (another path is billing this booking).</summary>
        public static async Task<RentScheduleLock?> AcquireAsync(string connectionString, int bookingId, int timeoutMs = 20000, CancellationToken ct = default)
        {
            var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            try
            {
                var resource = ResourceFor(bookingId);
                using var cmd = new SqlCommand("sp_getapplock", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = Math.Max(30, timeoutMs / 1000 + 10) };
                cmd.Parameters.AddWithValue("@Resource", resource);
                cmd.Parameters.AddWithValue("@LockMode", "Exclusive");
                cmd.Parameters.AddWithValue("@LockOwner", "Session");
                cmd.Parameters.AddWithValue("@LockTimeout", timeoutMs);
                var ret = cmd.Parameters.Add("@ReturnValue", SqlDbType.Int);
                ret.Direction = ParameterDirection.ReturnValue;
                await cmd.ExecuteNonQueryAsync(ct);
                if (ret.Value is int code && code >= 0) return new RentScheduleLock(conn, resource);
                await conn.DisposeAsync();
                return null;
            }
            catch
            {
                await conn.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                using var cmd = new SqlCommand("sp_releaseapplock", _conn) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@Resource", _resource);
                cmd.Parameters.AddWithValue("@LockOwner", "Session");
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                SqlConnection.ClearPool(_conn);   // end the physical session, and the lock it owns
            }
            await _conn.DisposeAsync();
        }
    }

    /// <summary><see cref="IRentScheduleGate"/> over <see cref="RentScheduleLock"/>.</summary>
    public sealed class RentScheduleGate : IRentScheduleGate
    {
        private readonly string _connectionString;
        public RentScheduleGate(Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection string is missing.");
        }

        public async Task<IAsyncDisposable> AcquireAsync(int bookingId, CancellationToken ct = default) =>
            await RentScheduleLock.AcquireAsync(_connectionString, bookingId, 20000, ct)
            ?? throw new BookingScheduleConflictException("This booking is being invoiced right now - try again in a moment.");
    }

    /// <summary>Shapes an invoice from schedule rows the way the existing invoice paths shape theirs.</summary>
    public static class RentScheduleInvoiceShape
    {
        /// <summary>
        /// The CreateCustomInvoice request of "generate next invoice" for one schedule month: rent line net of the discount (DiscountTotal sent
        /// separately), a security deposit line when the row has one, service charge and tax in the header fields.
        /// </summary>
        public static CreateCustomInvoiceDto NextInvoiceDto(ScheduleInvoiceSource src, int bookingId, int userId, string spaceName, int secMonths,
            DateTime today, int dueDays)
        {
            var f = src.Figures;
            var b = src.Booking;
            var dto = new CreateCustomInvoiceDto
            {
                BookingId = bookingId,
                UserId = userId,
                IssuedOn = today,
                DueOn = today.AddDays(dueDays),
                Notes = $"Cycle Billing Invoice for Booking #{bookingId} - {spaceName}",
                SendEmail = true,
                BillingPeriodStart = f.PeriodStart,
                BillingPeriodEnd = f.PeriodEnd,
                AdvanceRentMonths = f.Months,
                SecurityDepositMonths = f.Deposit > 0 ? secMonths : 0,
                SecurityDepositAmount = f.Deposit,
                RoomRentExclTax = f.RoomRentExclTax,
                ServiceCharges = f.ServiceCharge,
                TaxOnServiceCharges = f.Tax,
                SubTotal = f.Rent,
                DiscountTotal = f.Discount,
                TaxTotal = f.Tax,
                GrandTotal = f.GrandTotal,
                RentAccountId = b.RentAccountId,
                SecurityReceivedId = b.SecurityReceivedId,
                ServicesIncomeId = b.ServicesIncomeId,
                SalesTaxId = b.SalesTaxId,
                AccountReceivableId = b.AccountReceivableId,
                AccountsCoaId = b.RentAccountId,
                Lines = new List<CreateCustomInvoiceLineDto>()
            };
            if (f.RentAfterDiscount > 0)
                dto.Lines.Add(new CreateCustomInvoiceLineDto
                {
                    Description = $"Rent for {f.PeriodStart:MMM d, yyyy} to {f.PeriodEnd:MMM d, yyyy}",
                    Quantity = 1, UnitPrice = f.RentAfterDiscount, DiscountAmount = 0m, TaxRate = 0m, ChargeTypeId = 1
                });
            if (f.Deposit > 0)
                dto.Lines.Add(new CreateCustomInvoiceLineDto
                {
                    Description = $"Security Deposit (Refundable - {spaceName})",
                    Quantity = 1, UnitPrice = f.Deposit, DiscountAmount = 0m, TaxRate = 0m, ChargeTypeId = 2
                });
            if (dto.Lines.Count == 0)
                dto.Lines.Add(new CreateCustomInvoiceLineDto
                {
                    Description = $"Rent for {f.PeriodStart:MMM d, yyyy} to {f.PeriodEnd:MMM d, yyyy}",
                    Quantity = 1, UnitPrice = 0m, DiscountAmount = 0m, TaxRate = 0m, ChargeTypeId = 1
                });
            return dto;
        }

        /// <summary>Overwrites the engine's figures with the rows' sums, so the preview, the lines and the invoice all follow the table.</summary>
        public static void ApplyToCalc(InvoiceCalculationResult calc, ScheduleInvoiceSource src)
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

        /// <summary>The invoice id of a CreateCustomInvoice answer (200 with { invoiceId }); null for any failure.</summary>
        public static int? InvoiceIdOf(Microsoft.AspNetCore.Mvc.IActionResult? result)
        {
            if (result is not Microsoft.AspNetCore.Mvc.ObjectResult { StatusCode: null or 200, Value: { } value }) return null;
            var prop = value.GetType().GetProperty("invoiceId");
            return prop?.GetValue(value) is int id && id > 0 ? id : null;
        }
    }

    public enum RentScheduleJobOutcome
    {
        /// <summary>The booking has no table (or CQ_01 has not run): the caller bills it as it always did.</summary>
        NotScheduled,
        Billed,
        /// <summary>Nothing billed now (every month invoiced, another path holds the lock, or the period already had an invoice that was linked).</summary>
        Skipped
    }

    /// <summary>
    /// Billing Automation for a booking that has rent schedule rows: bills the next un-invoiced row instead of dbo.WN_Invoice_CreateRecurring.
    /// Same invoice as that procedure makes (InvoiceTypeId 2, PendingApproval, "INV-REC-" number, service charge + tax header fields, ST invoice) but the
    /// amounts come from the row, written the way CreateCustomInvoice writes an invoice (net rent line, WN_UpdateInvoiceBreakdown,
    /// WN_EnsureCustomerSTInvoice), and the row's InvoiceId is stored in the same transaction.
    /// </summary>
    public static class RentScheduleJobInvoicer
    {
        public static async Task<RentScheduleJobOutcome> TryBillNextRowAsync(SqlConnection conn, string connectionString, IBookingScheduleService schedule,
            int bookingId, DateTime today, Microsoft.Extensions.Logging.ILogger log, CancellationToken ct)
        {
            // Cheap look first (no lock for the many bookings without a table).
            if (!(await schedule.GetNextInvoiceSourceAsync(bookingId, ct)).HasSchedule) return RentScheduleJobOutcome.NotScheduled;

            await using var guard = await RentScheduleLock.AcquireAsync(connectionString, bookingId, 15000, ct);
            if (guard == null)
            {
                log.LogInformation("Booking {BookingId} is being billed from its rent schedule by another path. Skipping.", bookingId);
                return RentScheduleJobOutcome.Skipped;
            }

            var (has, next) = await schedule.GetNextInvoiceSourceAsync(bookingId, ct);
            if (!has) return RentScheduleJobOutcome.NotScheduled;
            if (next == null)
            {
                log.LogInformation("Booking {BookingId}: every month of the rent schedule is invoiced.", bookingId);
                return RentScheduleJobOutcome.Skipped;
            }

            var f = next.Figures;
            var b = next.Booking;

            // The period may already carry an invoice (a crash between the invoice and the row, an invoice made by hand): link it, never bill twice.
            using (var checkCmd = new SqlCommand("dbo.WN_Invoice_CheckExistingBillingPeriod", conn) { CommandType = CommandType.StoredProcedure })
            {
                checkCmd.Parameters.AddWithValue("@BookingId", bookingId);
                checkCmd.Parameters.AddWithValue("@BillingPeriodStart", f.PeriodStart);
                checkCmd.Parameters.AddWithValue("@BillingPeriodEnd", f.PeriodEnd);
                if (Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct)) == 1)
                {
                    int? existing = await FindInvoiceForPeriodAsync(conn, bookingId, f.PeriodStart, f.PeriodEnd, ct);
                    if (existing != null)
                    {
                        await RentScheduleBookingRepository.MarkRowsInvoicedAsync(conn, null, bookingId, f.RowNos, existing.Value, ct);
                        log.LogInformation("Invoice {InvoiceId} already exists for booking {BookingId} period {Start:yyyy-MM-dd} to {End:yyyy-MM-dd}; linked to the rent schedule row.", existing, bookingId, f.PeriodStart, f.PeriodEnd);
                    }
                    return RentScheduleJobOutcome.Skipped;
                }
            }

            string baseNumber = $"INV-REC-{bookingId}-{f.PeriodStart:yyyyMMdd}";
            string number = baseNumber;
            for (int seq = 2; await InvoiceNumberExistsAsync(conn, number, ct); seq++) number = $"{baseNumber}-R{seq}";
            var due = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            string notes = $"Recurring Invoice for Period {f.PeriodStart:yyyy-MM-dd} to {f.PeriodEnd:yyyy-MM-dd}";

            using var tx = conn.BeginTransaction();
            try
            {
                int invoiceId;
                string generatedNumber;
                using (var cmd = new SqlCommand("dbo.WN_Invoices_Insert", conn, tx) { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@UserId", b.UserId);
                    cmd.Parameters.AddWithValue("@BookingId", bookingId);
                    cmd.Parameters.AddWithValue("@InvoiceNumber", number);
                    cmd.Parameters.AddWithValue("@IssuedOn", today);
                    cmd.Parameters.AddWithValue("@DueOn", due);
                    cmd.Parameters.AddWithValue("@SubTotal", f.Rent);
                    cmd.Parameters.AddWithValue("@DiscountTotal", f.Discount);
                    cmd.Parameters.AddWithValue("@TaxTotal", f.Tax);
                    cmd.Parameters.AddWithValue("@GrandTotal", f.GrandTotal);
                    cmd.Parameters.AddWithValue("@PaidTotal", 0m);
                    cmd.Parameters.AddWithValue("@CurrencyCode", "PKR");
                    cmd.Parameters.AddWithValue("@Notes", notes);
                    cmd.Parameters.AddWithValue("@InvoiceTypeId", 2);   // Recurring/Advance, as WN_Invoice_CreateRecurring
                    cmd.Parameters.AddWithValue("@AdvanceRentMonths", f.Months);
                    cmd.Parameters.AddWithValue("@SecurityDepositMonths", 0);
                    cmd.Parameters.AddWithValue("@SecurityDepositAmount", f.Deposit);
                    cmd.Parameters.AddWithValue("@AccountsCoaId", (object?)(b.RentAccountId ?? 2852) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@BillingPeriodMonths", f.Months);
                    cmd.Parameters.AddWithValue("@BillingPeriodStart", f.PeriodStart);
                    cmd.Parameters.AddWithValue("@BillingPeriodEnd", f.PeriodEnd);
                    cmd.Parameters.AddWithValue("@AccountReceivableId", (object?)b.AccountReceivableId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@RentAccountId", (object?)(b.RentAccountId ?? 2852) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ServicesIncomeId", (object?)b.ServicesIncomeId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SalesTaxId", (object?)b.SalesTaxId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SecurityReceivedId", (object?)b.SecurityReceivedId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SyncCustomerVendor", true);
                    cmd.Parameters.AddWithValue("@RoomRentExclTax", f.RoomRentExclTax);
                    cmd.Parameters.AddWithValue("@ServiceCharges", f.ServiceCharge);
                    cmd.Parameters.AddWithValue("@TaxOnServiceCharges", f.Tax);
                    var pId = new SqlParameter("@InvoiceId", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    var pNum = new SqlParameter("@GeneratedInvoiceNumber", SqlDbType.NVarChar, 50) { Direction = ParameterDirection.Output };
                    cmd.Parameters.Add(pId);
                    cmd.Parameters.Add(pNum);
                    var rc = cmd.Parameters.Add("@RetVal", SqlDbType.Int);
                    rc.Direction = ParameterDirection.ReturnValue;
                    await cmd.ExecuteNonQueryAsync(ct);
                    if (rc.Value is int r && r != 0 || pId.Value is DBNull or null || Convert.ToInt32(pId.Value) <= 0)
                        throw new InvalidOperationException($"WN_Invoices_Insert failed to create the rent schedule invoice for booking {bookingId}.");
                    invoiceId = Convert.ToInt32(pId.Value);
                    generatedNumber = pNum.Value is string s ? s : number;
                }

                using (var st = new SqlCommand("UPDATE dbo.WN_Invoices SET ApprovalStatus = 'PendingApproval' WHERE Id = @id", conn, tx))
                {
                    st.Parameters.AddWithValue("@id", invoiceId);
                    await st.ExecuteNonQueryAsync(ct);
                }

                short sort = 1;
                if (f.RentAfterDiscount > 0 || f.Deposit <= 0)
                    await InsertLineAsync(conn, tx, invoiceId, 1, $"Rent for {f.PeriodStart:MMM d, yyyy} to {f.PeriodEnd:MMM d, yyyy}", 1m, f.RentAfterDiscount, sort++, ct);
                if (f.Deposit > 0)
                    await InsertLineAsync(conn, tx, invoiceId, 2, $"Security Deposit (Refundable - {b.SpaceName ?? "Workspace"})", 1m, f.Deposit, sort++, ct);

                using (var upd = new SqlCommand("dbo.WN_UpdateInvoiceBreakdown", conn, tx) { CommandType = CommandType.StoredProcedure })
                {
                    upd.Parameters.AddWithValue("@InvoiceId", invoiceId);
                    upd.Parameters.AddWithValue("@SubTotal", f.Rent);
                    upd.Parameters.AddWithValue("@DiscountTotal", f.Discount);
                    upd.Parameters.AddWithValue("@TaxTotal", f.Tax);
                    upd.Parameters.AddWithValue("@GrandTotal", f.GrandTotal);
                    upd.Parameters.AddWithValue("@BillingPeriodStart", f.PeriodStart);
                    upd.Parameters.AddWithValue("@BillingPeriodEnd", f.PeriodEnd);
                    upd.Parameters.AddWithValue("@AdvanceRentMonths", f.Months);
                    upd.Parameters.AddWithValue("@SecurityDepositMonths", 0);
                    upd.Parameters.AddWithValue("@SecurityDepositAmount", f.Deposit);
                    upd.Parameters.AddWithValue("@RoomRentExclTax", f.RoomRentExclTax);
                    upd.Parameters.AddWithValue("@ServiceCharges", f.ServiceCharge);
                    upd.Parameters.AddWithValue("@TaxOnServiceCharges", f.Tax);
                    upd.Parameters.AddWithValue("@RentAccountId", (object?)(b.RentAccountId ?? 2852) ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@SecurityReceivedId", (object?)b.SecurityReceivedId ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@ServicesIncomeId", (object?)b.ServicesIncomeId ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@SalesTaxId", (object?)b.SalesTaxId ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@AccountReceivableId", (object?)b.AccountReceivableId ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@AccountsCoaId", (object?)(b.RentAccountId ?? 2852) ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@DueOn", due);
                    await upd.ExecuteNonQueryAsync(ct);
                }

                if (f.Tax > 0)
                {
                    using var ens = new SqlCommand("dbo.WN_EnsureCustomerSTInvoice", conn, tx) { CommandType = CommandType.StoredProcedure };
                    ens.Parameters.AddWithValue("@InvoiceId", invoiceId);
                    ens.Parameters.AddWithValue("@PublicId", DBNull.Value);
                    ens.Parameters.AddWithValue("@STInvoiceNumber", generatedNumber.Replace("INV-", "ST-INV-"));
                    ens.Parameters.AddWithValue("@RoomRentAmount", f.RoomRentExclTax);
                    ens.Parameters.AddWithValue("@ServiceChargeAmount", f.ServiceCharge);
                    ens.Parameters.AddWithValue("@STTaxRate", b.TaxPercent);
                    ens.Parameters.AddWithValue("@SubTotal", f.ServiceCharge);
                    ens.Parameters.AddWithValue("@TaxAmount", f.Tax);
                    ens.Parameters.AddWithValue("@GrandTotal", f.ServiceCharge + f.Tax);
                    await ens.ExecuteNonQueryAsync(ct);
                }

                if (!await RentScheduleBookingRepository.MarkRowsInvoicedAsync(conn, tx, bookingId, f.RowNos, invoiceId, ct))
                    throw new InvalidOperationException($"The rent schedule rows of booking {bookingId} were invoiced meanwhile; nothing was billed.");

                await tx.CommitAsync(ct);
                log.LogInformation("Generated rent schedule invoice {InvoiceNo} (ID: {InvoiceId}) for Booking {BookingId}, rows {Rows}. Total: PKR {Total}.", generatedNumber, invoiceId, bookingId, string.Join(",", f.RowNos), f.GrandTotal);
                return RentScheduleJobOutcome.Billed;
            }
            catch
            {
                try { await tx.RollbackAsync(CancellationToken.None); } catch { }
                throw;
            }
        }

        private static async Task InsertLineAsync(SqlConnection conn, SqlTransaction tx, int invoiceId, byte chargeTypeId, string description, decimal qty, decimal unitPrice, short sort, CancellationToken ct)
        {
            using var cmd = new SqlCommand("dbo.WN_InsertInvoiceLine", conn, tx) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@InvoiceId", invoiceId);
            cmd.Parameters.AddWithValue("@ChargeTypeId", chargeTypeId);
            cmd.Parameters.AddWithValue("@Description", description);
            cmd.Parameters.AddWithValue("@Quantity", qty);
            cmd.Parameters.AddWithValue("@UnitPrice", unitPrice);
            cmd.Parameters.AddWithValue("@DiscountAmount", 0m);
            cmd.Parameters.AddWithValue("@TaxRate", 0m);
            cmd.Parameters.AddWithValue("@SortOrder", sort);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task<bool> InvoiceNumberExistsAsync(SqlConnection conn, string number, CancellationToken ct)
        {
            using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.WN_Invoices WITH (NOLOCK) WHERE InvoiceNumber = @n", conn);
            cmd.Parameters.AddWithValue("@n", number);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
        }

        private static async Task<int?> FindInvoiceForPeriodAsync(SqlConnection conn, int bookingId, DateTime start, DateTime end, CancellationToken ct)
        {
            using var cmd = new SqlCommand(@"SELECT TOP 1 i.Id FROM dbo.WN_Invoices i WITH (NOLOCK)
                WHERE i.BookingId = @b AND CAST(i.BillingPeriodStart AS DATE) = @s AND CAST(i.BillingPeriodEnd AS DATE) = @e
                  AND ISNULL(i.InvoiceTypeId, 1) NOT IN (5, 7) AND ISNULL(i.IsCustom, 0) = 0 AND ISNULL(i.StatusId, 0) NOT IN (5, 40, 86)
                ORDER BY i.Id DESC", conn);
            cmd.Parameters.AddWithValue("@b", bookingId);
            cmd.Parameters.Add("@s", SqlDbType.Date).Value = start.Date;
            cmd.Parameters.Add("@e", SqlDbType.Date).Value = end.Date;
            var v = await cmd.ExecuteScalarAsync(ct);
            return v is null or DBNull ? null : Convert.ToInt32(v);
        }
    }
}
