using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// TRF_02 transfer credit in the database: WN_Bookings.TransferCreditBalance, its movement log WN_TransferCreditMovements and the row's
    /// WN_BookingSchedule.TransferCredit. Everything is guarded by <see cref="IsAvailableAsync"/> (a yes is kept, a no is re-checked every
    /// minute) so billing works unchanged before script TRF_02 has run. All writes run on the caller's connection / transaction.
    /// </summary>
    public static class TransferCreditStore
    {
        private static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(1);
        private static volatile bool _available;
        private static long _lastCheckTicks;

        public static async Task<bool> IsAvailableAsync(SqlConnection c, SqlTransaction? tx, CancellationToken ct = default)
        {
            if (_available) return true;
            long last = Interlocked.Read(ref _lastCheckTicks);
            if (last != 0 && DateTime.UtcNow.Ticks - last < RecheckEvery.Ticks) return false;
            await using var cmd = new SqlCommand(@"SELECT CASE WHEN COL_LENGTH(N'dbo.WN_Bookings', N'TransferCreditBalance') IS NOT NULL
                AND OBJECT_ID(N'dbo.WN_TransferCreditMovements', N'U') IS NOT NULL THEN 1 ELSE 0 END", c, tx) { CommandTimeout = 30 };
            bool ok = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
            if (ok) _available = true; else Interlocked.Exchange(ref _lastCheckTicks, DateTime.UtcNow.Ticks);
            return ok;
        }

        /// <summary>The booking's credit balance (0 when none / not set up). Only call after <see cref="IsAvailableAsync"/> said yes.</summary>
        public static async Task<decimal> GetBalanceAsync(SqlConnection c, SqlTransaction? tx, int bookingId, CancellationToken ct = default)
        {
            await using var cmd = new SqlCommand("SELECT ISNULL(TransferCreditBalance, 0) FROM dbo.WN_Bookings WITH (NOLOCK) WHERE Id = @b", c, tx) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@b", bookingId);
            var v = await cmd.ExecuteScalarAsync(ct);
            return v is null or DBNull ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Takes <paramref name="amount"/> off the balance (guarded: only while the balance still covers it) and logs the movement. Returns the
        /// movement id, or null when the balance no longer covers the amount (the caller rolls back / stops). <paramref name="invoiceId"/> may be
        /// null when the invoice does not exist yet (reserve first, then <see cref="LinkMovementAsync"/>).
        /// </summary>
        public static async Task<int?> ConsumeAsync(SqlConnection c, SqlTransaction? tx, int bookingId, decimal amount, int? invoiceId, string reason, CancellationToken ct = default)
        {
            if (amount <= 0m) return null;
            await using var cmd = new SqlCommand(@"
                UPDATE dbo.WN_Bookings SET TransferCreditBalance = TransferCreditBalance - @c WHERE Id = @b AND TransferCreditBalance >= @c;
                IF @@ROWCOUNT = 0 SELECT CAST(NULL AS INT);
                ELSE
                BEGIN
                    INSERT INTO dbo.WN_TransferCreditMovements (BookingId, InvoiceId, Amount, Reason) VALUES (@b, @i, -@c, @r);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                END", c, tx) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@b", bookingId);
            cmd.Parameters.Add("@c", SqlDbType.Decimal).Value = amount;
            cmd.Parameters.Add("@i", SqlDbType.Int).Value = (object?)invoiceId ?? DBNull.Value;
            cmd.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = reason.Length > 200 ? reason[..200] : reason;
            var v = await cmd.ExecuteScalarAsync(ct);
            return v is null or DBNull ? null : Convert.ToInt32(v, CultureInfo.InvariantCulture);
        }

        /// <summary>Undoes a <see cref="ConsumeAsync"/> whose invoice was not created: gives the amount back and removes the movement.</summary>
        public static async Task ReleaseAsync(SqlConnection c, SqlTransaction? tx, int bookingId, decimal amount, int movementId, CancellationToken ct = default)
        {
            await using var cmd = new SqlCommand(@"
                DELETE FROM dbo.WN_TransferCreditMovements WHERE Id = @m AND BookingId = @b AND InvoiceId IS NULL;
                IF @@ROWCOUNT = 1 UPDATE dbo.WN_Bookings SET TransferCreditBalance = ISNULL(TransferCreditBalance, 0) + @c WHERE Id = @b;", c, tx) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@m", movementId);
            cmd.Parameters.AddWithValue("@b", bookingId);
            cmd.Parameters.Add("@c", SqlDbType.Decimal).Value = amount;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>Stores the invoice on a reserved movement.</summary>
        public static async Task LinkMovementAsync(SqlConnection c, SqlTransaction? tx, int movementId, int invoiceId, CancellationToken ct = default)
        {
            await using var cmd = new SqlCommand("UPDATE dbo.WN_TransferCreditMovements SET InvoiceId = @i WHERE Id = @m", c, tx) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@i", invoiceId);
            cmd.Parameters.AddWithValue("@m", movementId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>Writes the credit each row took (WN_BookingSchedule.TransferCredit); a no-op before the TRF_02 columns exist.</summary>
        public static async Task SetRowCreditsAsync(SqlConnection c, SqlTransaction? tx, int bookingId, IReadOnlyDictionary<int, decimal> perRow, CancellationToken ct = default)
        {
            if (perRow.Count == 0 || !await RentScheduleBookingRepository.HasTransferColumnsAsync(c, tx, ct)) return;
            foreach (var (rowNo, credit) in perRow)
            {
                await using var cmd = new SqlCommand("UPDATE dbo.WN_BookingSchedule SET TransferCredit = @c WHERE BookingId = @b AND RowNo = @r", c, tx) { CommandTimeout = 30 };
                cmd.Parameters.Add("@c", SqlDbType.Decimal).Value = credit;
                cmd.Parameters.AddWithValue("@b", bookingId);
                cmd.Parameters.AddWithValue("@r", rowNo);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
    }
}
