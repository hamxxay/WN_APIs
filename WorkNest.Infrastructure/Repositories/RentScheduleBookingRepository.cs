using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.RentSchedule;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services.BookingSchedules;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// A booking's rent table (script CQ_01_rent_schedule): dbo.WN_BookingSchedule and dbo.WN_BookingScheduleChanges, plain SQL on its own
    /// connection. <see cref="IsSetUpAsync"/> asks OBJECT_ID once (a yes is kept, a no is re-checked every minute) so every booking keeps its
    /// old behaviour on a server where CQ_01 has not run. A row is "invoiced" while its InvoiceId points at a live invoice (StatusId not 5 / 40 / 86).
    /// </summary>
    public sealed class RentScheduleBookingRepository : IRentScheduleBookingRepository
    {
        private const int CommandTimeoutSeconds = 30;
        private static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(1);
        private static volatile bool _setUp;
        private static long _lastCheckTicks;

        /// <summary>An invoice that no longer counts (cancelled / deleted): the row it was stored on is billable again.</summary>
        private const string LiveInvoice = "ISNULL(i.StatusId, 0) NOT IN (5, 40, 86)";

        private readonly string _connectionString;

        public RentScheduleBookingRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private async Task<SqlConnection> OpenAsync(CancellationToken ct)
        {
            var c = new SqlConnection(_connectionString);
            await c.OpenAsync(ct);
            return c;
        }

        private static string? Str(object? v) => v is null or DBNull ? null : v.ToString();
        private static int? Int(object? v) => v is null or DBNull ? null : Convert.ToInt32(v, CultureInfo.InvariantCulture);
        private static decimal Dec(object? v) => v is null or DBNull ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
        private static DateTime? Utc(object? v) => v is DateTime d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;

        // ---- set-up check ------------------------------------------------------------------------------------------------------

        public async Task<bool> IsSetUpAsync(CancellationToken ct = default)
        {
            if (_setUp) return true;
            long last = Interlocked.Read(ref _lastCheckTicks);
            if (last != 0 && DateTime.UtcNow.Ticks - last < RecheckEvery.Ticks) return false;
            await using var c = await OpenAsync(ct);
            bool ok = await TablesExistAsync(c, null, ct);
            if (ok) _setUp = true; else Interlocked.Exchange(ref _lastCheckTicks, DateTime.UtcNow.Ticks);
            return ok;
        }

        private static async Task<bool> TablesExistAsync(SqlConnection c, SqlTransaction? tx, CancellationToken ct)
        {
            await using var cmd = new SqlCommand(@"SELECT CASE WHEN OBJECT_ID(N'dbo.WN_QuotationSchedule', N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.WN_BookingSchedule', N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.WN_BookingScheduleChanges', N'U') IS NOT NULL THEN 1 ELSE 0 END", c, tx) { CommandTimeout = CommandTimeoutSeconds };
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
        }

        // ---- TRF_02 columns (space transfer) ------------------------------------------------------------------------------------

        private static volatile bool _transferCols;
        private static long _lastTransferCheckTicks;

        /// <summary>True when script TRF_02 has added PaidRent / PaidServiceCharge / PaidTax / TransferCredit / TransferSettled to WN_BookingSchedule.
        /// A yes is kept for the process, a no is re-checked every minute, so the code works before and after the script runs.</summary>
        public static async Task<bool> HasTransferColumnsAsync(SqlConnection c, SqlTransaction? tx, CancellationToken ct = default)
        {
            if (_transferCols) return true;
            long last = Interlocked.Read(ref _lastTransferCheckTicks);
            if (last != 0 && DateTime.UtcNow.Ticks - last < RecheckEvery.Ticks) return false;
            await using var cmd = new SqlCommand(@"SELECT CASE WHEN COL_LENGTH(N'dbo.WN_BookingSchedule', N'PaidRent') IS NOT NULL
                AND COL_LENGTH(N'dbo.WN_BookingSchedule', N'PaidServiceCharge') IS NOT NULL AND COL_LENGTH(N'dbo.WN_BookingSchedule', N'PaidTax') IS NOT NULL
                AND COL_LENGTH(N'dbo.WN_BookingSchedule', N'TransferCredit') IS NOT NULL AND COL_LENGTH(N'dbo.WN_BookingSchedule', N'TransferSettled') IS NOT NULL
                THEN 1 ELSE 0 END", c, tx) { CommandTimeout = CommandTimeoutSeconds };
            bool ok = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
            if (ok) _transferCols = true; else Interlocked.Exchange(ref _lastTransferCheckTicks, DateTime.UtcNow.Ticks);
            return ok;
        }

        private static decimal? DecN(object? v) => v is null or DBNull ? null : Convert.ToDecimal(v, CultureInfo.InvariantCulture);

        // ---- booking facts -----------------------------------------------------------------------------------------------------

        public async Task<BookingBillingInfo?> GetBillingInfoAsync(int bookingId, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            BookingBillingInfo? info = null;
            await using (var cmd = new SqlCommand("dbo.WN_GetBookingInvoiceDetails", c) { CommandType = CommandType.StoredProcedure, CommandTimeout = CommandTimeoutSeconds })
            {
                cmd.Parameters.AddWithValue("@BookingId", bookingId);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                {
                    var cols = new HashSet<string>(Enumerable.Range(0, r.FieldCount).Select(r.GetName), StringComparer.OrdinalIgnoreCase);
                    object? V(string n) => cols.Contains(n) ? r[n] : null;
                    info = new BookingBillingInfo
                    {
                        BookingId = bookingId,
                        UserId = Int(V("UserId")) ?? 0,
                        StartOn = V("StartOn") is DateTime so ? so : null,
                        EndOn = V("EndOn") is DateTime eo ? eo : null,
                        MonthlyRent = Dec(V("MonthlyRent")),
                        SubtotalAmount = Dec(V("SubtotalAmount")),
                        TotalAmount = Dec(V("TotalAmount")),
                        DiscountAmount = Dec(V("DiscountAmount")),
                        DiscountPercentage = Dec(V("DiscountPercentage")),
                        BillingPeriodMonths = Int(V("BillingPeriodMonths")) is > 0 and var bpm ? bpm : 3,
                        SecurityDepositRequired = Dec(V("SecurityDepositRequired")),
                        ResolvedSecurityDeposit = Dec(V("ResolvedSecurityDeposit")),
                        Seats = Int(V("SpaceCapacity")) is > 0 and var seats ? seats : 1,
                        SupportRate = Dec(V("PerSeatSupportRate")) is > 0m and var rate ? rate : 2000m,
                        TaxPercent = V("AppliedTaxPercentage") is null or DBNull ? 16m : Dec(V("AppliedTaxPercentage")),
                        SpaceName = Str(V("SpaceName")),
                        SpaceTypeName = Str(V("SpaceTypeName")),
                        CategoryCode = Str(V("CategoryCode")),
                        CustomerName = Str(V("CustomerName")),
                        RentAccountId = Int(V("RentAccountId")), SecurityReceivedId = Int(V("SecurityReceivedId")),
                        ServicesIncomeId = Int(V("ServicesIncomeId")), SalesTaxId = Int(V("SalesTaxId")), AccountReceivableId = Int(V("AccountReceivableId"))
                    };
                }
            }
            if (info == null) return null;
            await using (var cmd = new SqlCommand(@"SELECT s.LocationId, l.Name FROM dbo.WN_Bookings b WITH (NOLOCK)
                LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = s.LocationId WHERE b.Id = @b", c) { CommandTimeout = CommandTimeoutSeconds })
            {
                cmd.Parameters.AddWithValue("@b", bookingId);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct)) { info.LocationId = Int(r[0]); info.LocationName = Str(r[1]); }
            }
            if (await TransferCreditStore.IsAvailableAsync(c, null, ct))
                info.TransferCreditBalance = await TransferCreditStore.GetBalanceAsync(c, null, bookingId, ct);
            return info;
        }

        public async Task<IReadOnlyList<InvoicedPeriod>> GetInvoicedPeriodsAsync(int bookingId, CancellationToken ct = default)
        {
            const string sql = @"
                DECLARE @Cancelled TINYINT = ISNULL((SELECT TOP 1 Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) = 'Cancelled' ORDER BY Id), 5);
                SELECT i.Id, i.InvoiceNumber, i.BillingPeriodStart, i.BillingPeriodEnd, i.SubTotal, i.DiscountTotal, i.TaxTotal,
                       ISNULL(i.ServiceCharges, 0) AS ServiceCharges, ISNULL(i.SecurityDepositAmount, 0) AS SecurityDepositAmount, i.GrandTotal
                FROM dbo.WN_Invoices i WITH (NOLOCK)
                WHERE i.BookingId = @b AND i.BillingPeriodStart IS NOT NULL AND i.BillingPeriodEnd IS NOT NULL
                  AND ISNULL(i.InvoiceTypeId, 1) NOT IN (5, 7) AND ISNULL(i.IsCustom, 0) = 0 AND ISNULL(i.StatusId, 0) <> 40
                  AND (ISNULL(i.StatusId, 0) NOT IN (5, 86, @Cancelled)
                       OR EXISTS (SELECT 1 FROM dbo.WN_CustomInvoices ci WITH (NOLOCK) WHERE ci.ReplacedInvoiceId = i.Id))
                ORDER BY i.BillingPeriodStart, i.Id";
            var list = new List<InvoicedPeriod>();
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.AddWithValue("@b", bookingId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                list.Add(new InvoicedPeriod
                {
                    InvoiceId = Convert.ToInt32(r["Id"]), InvoiceNumber = Str(r["InvoiceNumber"]),
                    Start = ((DateTime)r["BillingPeriodStart"]).Date, End = ((DateTime)r["BillingPeriodEnd"]).Date,
                    SubTotal = Dec(r["SubTotal"]), DiscountTotal = Dec(r["DiscountTotal"]), TaxTotal = Dec(r["TaxTotal"]),
                    ServiceCharges = Dec(r["ServiceCharges"]), SecurityDeposit = Dec(r["SecurityDepositAmount"]), GrandTotal = Dec(r["GrandTotal"]),
                    Months = 0m
                });
            return list;
        }

        // ---- the booking's rows ------------------------------------------------------------------------------------------------

        public async Task<IReadOnlyList<BookingScheduleRow>> GetRowsAsync(int bookingId, CancellationToken ct = default)
        {
            var list = new List<BookingScheduleRow>();
            await using var c = await OpenAsync(ct);
            bool trf = await HasTransferColumnsAsync(c, null, ct);
            string sql = $@"SELECT s.RowNo, s.PeriodStart, s.PeriodEnd, s.MonthFraction, s.MonthlyRent, s.ServiceCharge, s.RoomRent, s.DiscountType,
                       s.DiscountValue, s.DiscountAmount, s.RentAfterDiscount, s.Tax, s.SecurityDeposit, s.Total, i.Id AS LiveInvoiceId, i.InvoiceNumber
                       {(trf ? ", s.PaidRent, s.PaidServiceCharge, s.PaidTax, s.TransferCredit, s.TransferSettled" : "")}
                FROM dbo.WN_BookingSchedule s WITH (NOLOCK)
                LEFT JOIN dbo.WN_Invoices i WITH (NOLOCK) ON i.Id = s.InvoiceId AND {LiveInvoice}
                WHERE s.BookingId = @b ORDER BY s.RowNo";
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.AddWithValue("@b", bookingId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var row = new BookingScheduleRow
                {
                    RowNo = Convert.ToInt32(r["RowNo"]),
                    PeriodStart = ((DateTime)r["PeriodStart"]).Date, PeriodEnd = ((DateTime)r["PeriodEnd"]).Date,
                    MonthFraction = Dec(r["MonthFraction"]), MonthlyRent = Dec(r["MonthlyRent"]), ServiceCharge = Dec(r["ServiceCharge"]),
                    RoomRent = Dec(r["RoomRent"]), DiscountType = Str(r["DiscountType"]), DiscountValue = Dec(r["DiscountValue"]),
                    DiscountAmount = Dec(r["DiscountAmount"]), RentAfterDiscount = Dec(r["RentAfterDiscount"]), Tax = Dec(r["Tax"]),
                    SecurityDeposit = r["SecurityDeposit"] is DBNull ? null : Dec(r["SecurityDeposit"]), Total = Dec(r["Total"]),
                    InvoiceId = Int(r["LiveInvoiceId"]), InvoiceNumber = Str(r["InvoiceNumber"])
                };
                if (trf)
                {
                    row.PaidRent = DecN(r["PaidRent"]); row.PaidServiceCharge = DecN(r["PaidServiceCharge"]); row.PaidTax = DecN(r["PaidTax"]);
                    row.TransferCredit = DecN(r["TransferCredit"]);
                    row.TransferSettled = r["TransferSettled"] is bool ts && ts;
                }
                row.Locked = row.InvoiceId != null || row.TransferSettled;
                row.IsShort = row.MonthFraction < 1m;
                row.Label = BookingScheduleMath.Label(row.PeriodStart, row.PeriodEnd);
                list.Add(row);
            }
            return list;
        }

        public async Task<int?> FindLiveInvoiceForPeriodAsync(int bookingId, DateTime start, DateTime end, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            return await FindLiveInvoiceForPeriodAsync(c, bookingId, start, end, ct);
        }

        /// <summary>Invoices made by CreateCustomInvoice have IsCustom = 0 (WN_Invoices_Insert never sets it), so the same filter as the billing job's duplicate check finds them.</summary>
        public static async Task<int?> FindLiveInvoiceForPeriodAsync(SqlConnection c, int bookingId, DateTime start, DateTime end, CancellationToken ct = default)
        {
            await using var cmd = new SqlCommand(@"SELECT TOP 1 i.Id FROM dbo.WN_Invoices i WITH (NOLOCK)
                WHERE i.BookingId = @b AND CAST(i.BillingPeriodStart AS DATE) = @s AND CAST(i.BillingPeriodEnd AS DATE) = @e
                  AND ISNULL(i.InvoiceTypeId, 1) NOT IN (5, 7) AND ISNULL(i.IsCustom, 0) = 0 AND ISNULL(i.StatusId, 0) NOT IN (5, 40, 86)
                ORDER BY i.Id DESC", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.AddWithValue("@b", bookingId);
            cmd.Parameters.Add("@s", SqlDbType.Date).Value = start.Date;
            cmd.Parameters.Add("@e", SqlDbType.Date).Value = end.Date;
            var v = await cmd.ExecuteScalarAsync(ct);
            return v is null or DBNull ? null : Convert.ToInt32(v);
        }

        public async Task<int> EnsureBookingScheduleFromQuotationAsync(int bookingId, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            int quotationId;
            await using (var cmd = new SqlCommand(@"SELECT TOP 1 q.Id FROM dbo.WN_Quotations q WITH (NOLOCK)
                WHERE q.BookingId = @b AND EXISTS (SELECT 1 FROM dbo.WN_QuotationSchedule s WITH (NOLOCK) WHERE s.QuotationId = q.Id)
                ORDER BY q.Id DESC", c) { CommandTimeout = CommandTimeoutSeconds })
            {
                cmd.Parameters.AddWithValue("@b", bookingId);
                var v = await cmd.ExecuteScalarAsync(ct);
                if (v is null or DBNull) return 0;
                quotationId = Convert.ToInt32(v);
            }
            return await CopyQuotationScheduleToBookingAsync(c, quotationId, bookingId, ct);
        }

        public async Task<bool> HasRowsAsync(int bookingId, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.WN_BookingSchedule WITH (NOLOCK) WHERE BookingId = @b) THEN 1 ELSE 0 END", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.AddWithValue("@b", bookingId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
        }

        public async Task SaveRowsAsync(int bookingId, IReadOnlyList<BookingScheduleRow> rows, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            bool trf = await HasTransferColumnsAsync(c, null, ct);
            // A transfer-settled row is never touched (nobody can unlock it); the transfer columns of an existing row are never overwritten by the UPDATE.
            string sql = $@"
                UPDATE s SET DiscountType = @DiscountType, DiscountValue = @DiscountValue, DiscountAmount = @DiscountAmount,
                             RentAfterDiscount = @RentAfterDiscount, Tax = @Tax, SecurityDeposit = @SecurityDeposit, Total = @Total, InvoiceId = NULL
                FROM dbo.WN_BookingSchedule s
                WHERE s.BookingId = @BookingId AND s.RowNo = @RowNo
                  {(trf ? "AND ISNULL(s.TransferSettled, 0) = 0" : "")}
                  AND (s.InvoiceId IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.WN_Invoices i WITH (NOLOCK) WHERE i.Id = s.InvoiceId AND {LiveInvoice}));
                IF @@ROWCOUNT = 0 AND NOT EXISTS (SELECT 1 FROM dbo.WN_BookingSchedule WHERE BookingId = @BookingId AND RowNo = @RowNo)
                    INSERT INTO dbo.WN_BookingSchedule (BookingId, RowNo, PeriodStart, PeriodEnd, MonthFraction, MonthlyRent, ServiceCharge, RoomRent,
                        DiscountType, DiscountValue, DiscountAmount, RentAfterDiscount, Tax, SecurityDeposit, Total, InvoiceId
                        {(trf ? ", PaidRent, PaidServiceCharge, PaidTax, TransferCredit, TransferSettled" : "")})
                    VALUES (@BookingId, @RowNo, @PeriodStart, @PeriodEnd, @MonthFraction, @MonthlyRent, @ServiceCharge, @RoomRent,
                        @DiscountType, @DiscountValue, @DiscountAmount, @RentAfterDiscount, @Tax, @SecurityDeposit, @Total, @InvoiceId
                        {(trf ? ", @PaidRent, @PaidServiceCharge, @PaidTax, @TransferCredit, @TransferSettled" : "")});";
            await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
            try
            {
                foreach (var row in rows.OrderBy(r => r.RowNo))
                {
                    await using var cmd = new SqlCommand(sql, c, tx) { CommandTimeout = CommandTimeoutSeconds };
                    cmd.Parameters.AddWithValue("@BookingId", bookingId);
                    cmd.Parameters.AddWithValue("@RowNo", row.RowNo);
                    cmd.Parameters.Add("@PeriodStart", SqlDbType.Date).Value = row.PeriodStart.Date;
                    cmd.Parameters.Add("@PeriodEnd", SqlDbType.Date).Value = row.PeriodEnd.Date;
                    cmd.Parameters.Add("@MonthFraction", SqlDbType.Decimal).Value = row.MonthFraction;
                    cmd.Parameters.Add("@MonthlyRent", SqlDbType.Decimal).Value = row.MonthlyRent;
                    cmd.Parameters.Add("@ServiceCharge", SqlDbType.Decimal).Value = row.ServiceCharge;
                    cmd.Parameters.Add("@RoomRent", SqlDbType.Decimal).Value = row.RoomRent;
                    cmd.Parameters.Add("@DiscountType", SqlDbType.NVarChar, 20).Value = row.DiscountType ?? BookingScheduleMath.Amount;
                    cmd.Parameters.Add("@DiscountValue", SqlDbType.Decimal).Value = row.DiscountValue;
                    cmd.Parameters.Add("@DiscountAmount", SqlDbType.Decimal).Value = row.DiscountAmount;
                    cmd.Parameters.Add("@RentAfterDiscount", SqlDbType.Decimal).Value = row.RentAfterDiscount;
                    cmd.Parameters.Add("@Tax", SqlDbType.Decimal).Value = row.Tax;
                    cmd.Parameters.Add("@SecurityDeposit", SqlDbType.Decimal).Value = (object?)row.SecurityDeposit ?? DBNull.Value;
                    cmd.Parameters.Add("@Total", SqlDbType.Decimal).Value = row.Total;
                    cmd.Parameters.Add("@InvoiceId", SqlDbType.Int).Value = (object?)row.InvoiceId ?? DBNull.Value;
                    if (trf)
                    {
                        cmd.Parameters.Add("@PaidRent", SqlDbType.Decimal).Value = (object?)row.PaidRent ?? DBNull.Value;
                        cmd.Parameters.Add("@PaidServiceCharge", SqlDbType.Decimal).Value = (object?)row.PaidServiceCharge ?? DBNull.Value;
                        cmd.Parameters.Add("@PaidTax", SqlDbType.Decimal).Value = (object?)row.PaidTax ?? DBNull.Value;
                        cmd.Parameters.Add("@TransferCredit", SqlDbType.Decimal).Value = (object?)row.TransferCredit ?? DBNull.Value;
                        cmd.Parameters.Add("@TransferSettled", SqlDbType.Bit).Value = row.TransferSettled ? 1 : (object)DBNull.Value;
                    }
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
            }
            catch { try { await tx.RollbackAsync(CancellationToken.None); } catch { } throw; }
        }

        public async Task<bool> MarkRowsInvoicedAsync(int bookingId, IReadOnlyCollection<int> rowNos, int invoiceId, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
            try
            {
                bool ok = await MarkRowsInvoicedAsync(c, tx, bookingId, rowNos, invoiceId, ct);
                if (ok) await tx.CommitAsync(ct); else await tx.RollbackAsync(ct);
                return ok;
            }
            catch { try { await tx.RollbackAsync(CancellationToken.None); } catch { } throw; }
        }

        /// <summary>
        /// The same on a connection / transaction the caller owns (the billing job stores the rows in the transaction that creates the invoice).
        /// Only rows that are not invoiced take the invoice; false when fewer rows than asked for could be marked (the caller rolls back).
        /// </summary>
        public static async Task<bool> MarkRowsInvoicedAsync(SqlConnection c, SqlTransaction? tx, int bookingId, IReadOnlyCollection<int> rowNos, int invoiceId, CancellationToken ct = default)
        {
            if (rowNos.Count == 0) return true;
            string ids = string.Join(",", rowNos.Select(n => n.ToString(CultureInfo.InvariantCulture)));   // ints only
            string sql = $@"UPDATE s SET InvoiceId = @InvoiceId
                FROM dbo.WN_BookingSchedule s
                WHERE s.BookingId = @BookingId AND s.RowNo IN ({ids})
                  AND (s.InvoiceId IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.WN_Invoices i WITH (NOLOCK) WHERE i.Id = s.InvoiceId AND {LiveInvoice}));
                SELECT @@ROWCOUNT;";
            await using var cmd = new SqlCommand(sql, c, tx) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            cmd.Parameters.AddWithValue("@InvoiceId", invoiceId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == rowNos.Count;
        }

        // ---- quotation -> booking ----------------------------------------------------------------------------------------------

        public async Task<int> CopyQuotationScheduleToBookingAsync(int quotationId, int bookingId, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            return await CopyQuotationScheduleToBookingAsync(c, quotationId, bookingId, ct);
        }

        /// <summary>Static form on an open connection, called by the quotation conversion right after the booking exists.</summary>
        public static async Task<int> CopyQuotationScheduleToBookingAsync(SqlConnection c, int quotationId, int bookingId, CancellationToken ct = default)
        {
            if (quotationId <= 0 || bookingId <= 0) return 0;
            if (!_setUp)
            {
                if (!await TablesExistAsync(c, null, ct)) return 0;
                _setUp = true;
            }
            const string sql = @"
                IF EXISTS (SELECT 1 FROM dbo.WN_QuotationSchedule WITH (NOLOCK) WHERE QuotationId = @q)
                   AND NOT EXISTS (SELECT 1 FROM dbo.WN_BookingSchedule WITH (NOLOCK) WHERE BookingId = @b)
                BEGIN
                    INSERT INTO dbo.WN_BookingSchedule (BookingId, RowNo, PeriodStart, PeriodEnd, MonthFraction, MonthlyRent, ServiceCharge, RoomRent,
                        DiscountType, DiscountValue, DiscountAmount, RentAfterDiscount, Tax, SecurityDeposit, Total, InvoiceId)
                    SELECT @b, RowNo, PeriodStart, PeriodEnd, MonthFraction, MonthlyRent, ServiceCharge, RoomRent,
                        DiscountType, DiscountValue, DiscountAmount, RentAfterDiscount, Tax, SecurityDeposit, Total, NULL
                    FROM dbo.WN_QuotationSchedule WHERE QuotationId = @q ORDER BY RowNo;
                    SELECT @@ROWCOUNT;
                END
                ELSE SELECT 0;";
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.AddWithValue("@q", quotationId);
            cmd.Parameters.AddWithValue("@b", bookingId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }

        // ---- changes -----------------------------------------------------------------------------------------------------------

        private const string ChangeSelect = @"
            SELECT c.Id, c.BookingId, c.RequestedById, ru.Name AS RequestedByName, ru.ManagerId AS RequestedByManagerId, c.RequestedAt, c.Status, c.RowsJson,
                   c.DecidedById, du.Name AS DecidedByName, c.DecidedAt, c.Note,
                   cust.CustomerName, s.Name AS SpaceName, s.LocationId, l.Name AS LocationName
            FROM dbo.WN_BookingScheduleChanges c WITH (NOLOCK)
            JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = c.BookingId
            LEFT JOIN dbo.WN_Users ru WITH (NOLOCK) ON ru.Id = c.RequestedById
            LEFT JOIN dbo.WN_Users du WITH (NOLOCK) ON du.Id = c.DecidedById
            LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
            LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = s.LocationId
            OUTER APPLY (
                SELECT TOP 1 ISNULL(NULLIF(cu.Company, ''), NULLIF(LTRIM(RTRIM(ISNULL(cu.FirstName, '') + ' ' + ISNULL(cu.LastName, ''))), '')) AS CustomerName
                FROM dbo.WN_Customers cu WITH (NOLOCK)
                WHERE (b.CustomerCode IS NOT NULL AND b.CustomerCode <> '' AND cu.Code = b.CustomerCode) OR cu.UserId = b.UserId
                ORDER BY CASE WHEN b.CustomerCode IS NOT NULL AND cu.Code = b.CustomerCode THEN 0 ELSE 1 END, cu.Id
            ) cust";

        private static BookingScheduleChange MapChange(SqlDataReader r) => new()
        {
            Id = Convert.ToInt32(r["Id"]), BookingId = Convert.ToInt32(r["BookingId"]), RequestedById = Int(r["RequestedById"]) ?? 0,
            RequestedByName = Str(r["RequestedByName"]), RequestedByManagerId = Int(r["RequestedByManagerId"]),
            RequestedAt = Utc(r["RequestedAt"]) ?? DateTime.UtcNow, Status = Str(r["Status"]) ?? "Pending", RowsJson = Str(r["RowsJson"]) ?? "[]",
            DecidedById = Int(r["DecidedById"]), DecidedByName = Str(r["DecidedByName"]), DecidedAt = Utc(r["DecidedAt"]), Note = Str(r["Note"]),
            CustomerName = Str(r["CustomerName"]), SpaceName = Str(r["SpaceName"]), LocationId = Int(r["LocationId"]), LocationName = Str(r["LocationName"])
        };

        public async Task<BookingScheduleChange?> GetPendingChangeAsync(int bookingId, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(ChangeSelect + " WHERE c.BookingId = @b AND c.Status = 'Pending' ORDER BY c.Id DESC", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.AddWithValue("@b", bookingId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            return await r.ReadAsync(ct) ? MapChange(r) : null;
        }

        public async Task<BookingScheduleChange?> GetChangeAsync(int changeId, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(ChangeSelect + " WHERE c.Id = @id", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.AddWithValue("@id", changeId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            return await r.ReadAsync(ct) ? MapChange(r) : null;
        }

        public async Task<IReadOnlyList<BookingScheduleChange>> ListChangesAsync(bool pendingOnly, int? managerUserId, IReadOnlyCollection<int>? locationIds, CancellationToken ct = default)
        {
            var where = new List<string>();
            if (pendingOnly) where.Add("c.Status = 'Pending'");
            if (managerUserId != null)
            {
                string locs = locationIds is { Count: > 0 } ? " OR s.LocationId IN (" + string.Join(",", locationIds.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")" : "";
                where.Add("(ru.ManagerId = @mgr" + locs + ")");
            }
            string sql = ChangeSelect + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") + " ORDER BY CASE WHEN c.Status = 'Pending' THEN 0 ELSE 1 END, c.RequestedAt DESC";
            var list = new List<BookingScheduleChange>();
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = CommandTimeoutSeconds };
            if (managerUserId != null) cmd.Parameters.AddWithValue("@mgr", managerUserId.Value);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) list.Add(MapChange(r));
            return list;
        }

        public async Task<IReadOnlyList<BookingScheduleListItem>> ListBookingsAsync(string? search, IReadOnlyCollection<int>? locationIds, bool activeOnly, CancellationToken ct = default)
        {
            var list = new List<BookingScheduleListItem>();
            if (locationIds is { Count: 0 }) return list;
            var where = new List<string>
            {
                "ISNULL(b.IsDeleted, 0) = 0",
                "b.StartOn IS NOT NULL AND b.EndOn IS NOT NULL",
                "ISNULL(LTRIM(RTRIM(os.Description)), '') NOT IN ('Rejected', 'Cancelled')",
                "ISNULL(st.Name, '') NOT LIKE '%Meeting%' AND ISNULL(st.Name, '') NOT LIKE '%Conference%'",
                "ISNULL(sp.Name, '') NOT LIKE '%Meeting%' AND ISNULL(sp.Name, '') NOT LIKE '%Conference%'"
            };
            if (activeOnly) where.Add("CAST(b.EndOn AS DATE) >= CAST(GETDATE() AS DATE)");
            if (locationIds != null)
                where.Add("sp.LocationId IN (" + string.Join(",", locationIds.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")");
            if (search != null)
                where.Add("(cust.CustomerName LIKE @q OR b.CustomerCode LIKE @q OR sp.Name LIKE @q OR l.Name LIKE @q OR CAST(b.Id AS VARCHAR(12)) = @id)");
            string sql = @"
                SELECT TOP 500 b.Id, cust.CustomerName, b.CustomerCode, sp.Name AS SpaceName, st.Name AS SpaceTypeName, sp.LocationId, l.Name AS LocationName,
                       b.StartOn, b.EndOn, LTRIM(RTRIM(os.Description)) AS StatusName,
                       CASE WHEN EXISTS (SELECT 1 FROM dbo.WN_BookingSchedule bs WITH (NOLOCK) WHERE bs.BookingId = b.Id) THEN 1 ELSE 0 END AS HasSchedule,
                       CASE WHEN EXISTS (SELECT 1 FROM dbo.WN_BookingScheduleChanges ch WITH (NOLOCK) WHERE ch.BookingId = b.Id AND ch.Status = 'Pending') THEN 1 ELSE 0 END AS HasPending
                FROM dbo.WN_Bookings b WITH (NOLOCK)
                JOIN dbo.WN_Spaces sp WITH (NOLOCK) ON sp.Id = b.SpaceId
                LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = sp.SpaceTypeId
                LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = sp.LocationId
                LEFT JOIN dbo.OrderStatus os WITH (NOLOCK) ON os.Id = b.BookingStatusId
                OUTER APPLY (
                    SELECT TOP 1 ISNULL(NULLIF(cu.Company, ''), NULLIF(LTRIM(RTRIM(ISNULL(cu.FirstName, '') + ' ' + ISNULL(cu.LastName, ''))), '')) AS CustomerName
                    FROM dbo.WN_Customers cu WITH (NOLOCK)
                    WHERE (b.CustomerCode IS NOT NULL AND b.CustomerCode <> '' AND cu.Code = b.CustomerCode) OR cu.UserId = b.UserId
                    ORDER BY CASE WHEN b.CustomerCode IS NOT NULL AND cu.Code = b.CustomerCode THEN 0 ELSE 1 END, cu.Id
                ) cust
                WHERE " + string.Join(" AND ", where) + @"
                ORDER BY CASE WHEN CAST(b.EndOn AS DATE) >= CAST(GETDATE() AS DATE) THEN 0 ELSE 1 END, b.StartOn DESC, b.Id DESC";
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = CommandTimeoutSeconds };
            if (search != null)
            {
                cmd.Parameters.Add("@q", SqlDbType.NVarChar, 120).Value = "%" + search.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
                cmd.Parameters.Add("@id", SqlDbType.VarChar, 12).Value = search.Length <= 12 ? search : "";
            }
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new BookingScheduleListItem
                {
                    BookingId = Convert.ToInt32(r["Id"]), CustomerName = Str(r["CustomerName"]), CustomerCode = Str(r["CustomerCode"]),
                    SpaceName = Str(r["SpaceName"]), SpaceTypeName = Str(r["SpaceTypeName"]), LocationId = Int(r["LocationId"]), LocationName = Str(r["LocationName"]),
                    StartOn = r["StartOn"] is DateTime so ? so : null, EndOn = r["EndOn"] is DateTime eo ? eo : null, Status = Str(r["StatusName"]),
                    HasSchedule = Convert.ToInt32(r["HasSchedule"]) == 1, HasPendingChange = Convert.ToInt32(r["HasPending"]) == 1
                });
            }
            return list;
        }

        public async Task<int?> InsertChangeAsync(int bookingId, int requestedById, string rowsJson, CancellationToken ct = default)
        {
            const string sql = @"
                IF EXISTS (SELECT 1 FROM dbo.WN_BookingScheduleChanges WITH (UPDLOCK, HOLDLOCK) WHERE BookingId = @b AND Status = 'Pending')
                    SELECT CAST(NULL AS INT);
                ELSE
                BEGIN
                    INSERT INTO dbo.WN_BookingScheduleChanges (BookingId, RequestedById, RequestedAt, Status, RowsJson)
                    VALUES (@b, @u, @at, 'Pending', @rows);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                END";
            await using var c = await OpenAsync(ct);
            await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = new SqlCommand(sql, c, tx) { CommandTimeout = CommandTimeoutSeconds };
                cmd.Parameters.AddWithValue("@b", bookingId);
                cmd.Parameters.AddWithValue("@u", requestedById);
                cmd.Parameters.Add("@at", SqlDbType.DateTime2).Value = DateTime.UtcNow;
                cmd.Parameters.Add("@rows", SqlDbType.NVarChar, -1).Value = rowsJson;
                var v = await cmd.ExecuteScalarAsync(ct);
                await tx.CommitAsync(ct);
                return v is null or DBNull ? null : Convert.ToInt32(v);
            }
            catch { try { await tx.RollbackAsync(CancellationToken.None); } catch { } throw; }
        }

        public async Task<bool> DecideChangeAsync(int changeId, string status, int decidedById, string? note, string? rowsJson, CancellationToken ct = default)
        {
            const string sql = @"UPDATE dbo.WN_BookingScheduleChanges
                SET Status = @s, DecidedById = @u, DecidedAt = @at, Note = @n, RowsJson = ISNULL(@rows, RowsJson)
                WHERE Id = @id AND Status = 'Pending'";
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.AddWithValue("@id", changeId);
            cmd.Parameters.AddWithValue("@s", status);
            cmd.Parameters.AddWithValue("@u", decidedById);
            cmd.Parameters.Add("@at", SqlDbType.DateTime2).Value = DateTime.UtcNow;
            cmd.Parameters.Add("@n", SqlDbType.NVarChar, -1).Value = (object?)note ?? DBNull.Value;
            cmd.Parameters.Add("@rows", SqlDbType.NVarChar, -1).Value = (object?)rowsJson ?? DBNull.Value;
            return await cmd.ExecuteNonQueryAsync(ct) == 1;
        }
    }
}
