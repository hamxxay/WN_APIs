using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Rent schedule storage (script CQ_01_rent_schedule): dbo.WN_QuotationSchedule, dbo.WN_BookingSchedule, dbo.WN_BookingScheduleChanges, plain SQL.
    /// <see cref="IsSetUpAsync"/> asks the catalog once (a yes is kept, a no is re-checked every minute).
    /// </summary>
    public sealed class RentScheduleRepository : IRentScheduleRepository
    {
        private const int CommandTimeoutSeconds = 30;
        private static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(1);
        private static volatile bool _setUp;
        private static long _lastCheckTicks;

        private readonly string _connectionString;

        public RentScheduleRepository(IConfiguration configuration)
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

        public async Task<bool> IsSetUpAsync(CancellationToken ct = default)
        {
            if (_setUp) return true;
            long last = Interlocked.Read(ref _lastCheckTicks);
            if (last != 0 && DateTime.UtcNow.Ticks - last < RecheckEvery.Ticks) return false;
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(@"SELECT CASE WHEN OBJECT_ID(N'dbo.WN_QuotationSchedule', N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.WN_BookingSchedule', N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.WN_BookingScheduleChanges', N'U') IS NOT NULL
                AND COL_LENGTH(N'dbo.WN_Quotations', N'ApprovalStatus') IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_WN_Quotations_ApprovalStatus'
                                AND parent_object_id = OBJECT_ID(N'dbo.WN_Quotations') AND definition NOT LIKE N'%NotSubmitted%')
                THEN 1 ELSE 0 END", c) { CommandTimeout = CommandTimeoutSeconds };
            bool ok = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
            if (ok) _setUp = true; else Interlocked.Exchange(ref _lastCheckTicks, DateTime.UtcNow.Ticks);
            return ok;
        }

        private const string Cols = "RowNo, PeriodStart, PeriodEnd, MonthFraction, MonthlyRent, ServiceCharge, RoomRent, DiscountType, DiscountValue, DiscountAmount, RentAfterDiscount, Tax, SecurityDeposit, Total";

        public async Task<IReadOnlyList<StoredScheduleRow>> GetQuotationRowsAsync(int quotationId, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand($"SELECT {Cols} FROM dbo.WN_QuotationSchedule WITH (NOLOCK) WHERE QuotationId = @Id ORDER BY RowNo", c)
                { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = quotationId;
            var list = new List<StoredScheduleRow>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                list.Add(new StoredScheduleRow
                {
                    RowNo = r.GetInt32(0), PeriodStart = r.GetDateTime(1), PeriodEnd = r.GetDateTime(2), MonthFraction = r.GetDecimal(3),
                    MonthlyRent = r.GetDecimal(4), ServiceCharge = r.GetDecimal(5), RoomRent = r.GetDecimal(6),
                    DiscountType = r.GetString(7), DiscountValue = r.GetDecimal(8), DiscountAmount = r.GetDecimal(9),
                    RentAfterDiscount = r.GetDecimal(10), Tax = r.GetDecimal(11),
                    SecurityDeposit = r.IsDBNull(12) ? null : r.GetDecimal(12), Total = r.GetDecimal(13)
                });
            return list;
        }

        public async Task ReplaceQuotationRowsAsync(int quotationId, IReadOnlyList<StoredScheduleRow> rows, QuotationScheduleHeader? header, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
            await using (var del = new SqlCommand("DELETE FROM dbo.WN_QuotationSchedule WHERE QuotationId = @Id", c, tx) { CommandTimeout = CommandTimeoutSeconds })
            {
                del.Parameters.Add("@Id", SqlDbType.Int).Value = quotationId;
                await del.ExecuteNonQueryAsync(ct);
            }
            foreach (var r in rows)
            {
                await using var ins = new SqlCommand(@"INSERT INTO dbo.WN_QuotationSchedule (QuotationId, " + Cols + @")
                    VALUES (@Q, @RowNo, @PS, @PE, @F, @MR, @SC, @RR, @DT, @DV, @DA, @RAD, @Tax, @SD, @Tot)", c, tx) { CommandTimeout = CommandTimeoutSeconds };
                ins.Parameters.Add("@Q", SqlDbType.Int).Value = quotationId;
                ins.Parameters.Add("@RowNo", SqlDbType.Int).Value = r.RowNo;
                ins.Parameters.Add("@PS", SqlDbType.Date).Value = r.PeriodStart.Date;
                ins.Parameters.Add("@PE", SqlDbType.Date).Value = r.PeriodEnd.Date;
                ins.Parameters.Add("@F", SqlDbType.Decimal).Value = r.MonthFraction;
                ins.Parameters.Add("@MR", SqlDbType.Decimal).Value = r.MonthlyRent;
                ins.Parameters.Add("@SC", SqlDbType.Decimal).Value = r.ServiceCharge;
                ins.Parameters.Add("@RR", SqlDbType.Decimal).Value = r.RoomRent;
                ins.Parameters.Add("@DT", SqlDbType.NVarChar, 20).Value = r.DiscountType;
                ins.Parameters.Add("@DV", SqlDbType.Decimal).Value = r.DiscountValue;
                ins.Parameters.Add("@DA", SqlDbType.Decimal).Value = r.DiscountAmount;
                ins.Parameters.Add("@RAD", SqlDbType.Decimal).Value = r.RentAfterDiscount;
                ins.Parameters.Add("@Tax", SqlDbType.Decimal).Value = r.Tax;
                ins.Parameters.Add("@SD", SqlDbType.Decimal).Value = (object?)r.SecurityDeposit ?? DBNull.Value;
                ins.Parameters.Add("@Tot", SqlDbType.Decimal).Value = r.Total;
                await ins.ExecuteNonQueryAsync(ct);
            }
            if (header != null)
            {
                await using var upd = new SqlCommand(@"UPDATE dbo.WN_Quotations SET SubtotalAmount = @Sub, DiscountType = N'Amount', DiscountPercentage = 0,
                        DiscountAmount = @Disc, SecurityDeposit = @Dep, TotalAmount = @Tot WHERE Id = @Id", c, tx) { CommandTimeout = CommandTimeoutSeconds };
                upd.Parameters.Add("@Sub", SqlDbType.Decimal).Value = header.Subtotal;
                upd.Parameters.Add("@Disc", SqlDbType.Decimal).Value = header.DiscountAmount;
                upd.Parameters.Add("@Dep", SqlDbType.Decimal).Value = header.SecurityDeposit;
                upd.Parameters.Add("@Tot", SqlDbType.Decimal).Value = header.TotalAmount;
                upd.Parameters.Add("@Id", SqlDbType.Int).Value = quotationId;
                await upd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }

        public async Task VoidQuotationAsync(int quotationId, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand("UPDATE dbo.WN_Quotations SET IsActive = 0, Status = N'Inactive' WHERE Id = @Id", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = quotationId;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task CopyQuotationRowsAsync(int sourceQuotationId, int newQuotationId, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand($@"INSERT INTO dbo.WN_QuotationSchedule (QuotationId, {Cols})
                SELECT @New, {Cols} FROM dbo.WN_QuotationSchedule WHERE QuotationId = @Src", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@New", SqlDbType.Int).Value = newQuotationId;
            cmd.Parameters.Add("@Src", SqlDbType.Int).Value = sourceQuotationId;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
