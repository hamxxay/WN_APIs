using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Keeps Day Pass bookings (WN_Bookings.BookingKind = 'DayPass') out of the recurring paths. The column exists only after DP_01_day_pass,
    /// so a query that names it must not run before: <see cref="ExcludeAsync"/> gives the condition only when the column is there.
    /// </summary>
    public static class DayPassSql
    {
        private static volatile bool _has;
        private static DateTime _checkedAt = DateTime.MinValue;

        public static async Task<bool> HasColumnAsync(SqlConnection c, CancellationToken ct = default)
        {
            if (_has) return true;
            if ((DateTime.UtcNow - _checkedAt).TotalSeconds < 60) return false;
            _checkedAt = DateTime.UtcNow;
            await using var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH(N'dbo.WN_Bookings', N'BookingKind') IS NULL THEN 0 ELSE 1 END", c);
            _has = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
            return _has;
        }

        /// <summary>" AND ISNULL(alias.BookingKind, N'') &lt;&gt; N'DayPass'" once DP_01 has run, else an empty string.</summary>
        public static async Task<string> ExcludeAsync(SqlConnection c, string alias, CancellationToken ct = default) =>
            await HasColumnAsync(c, ct) ? $" AND ISNULL({alias}.BookingKind, N'') <> N'DayPass'" : "";

        /// <summary>True when the booking is a day pass (false before DP_01).</summary>
        public static async Task<bool> IsDayPassAsync(SqlConnection c, int bookingId, SqlTransaction? tx = null, CancellationToken ct = default)
        {
            if (!await HasColumnAsync(c, ct)) return false;
            await using var cmd = new SqlCommand("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.WN_Bookings WITH (NOLOCK) WHERE Id = @b AND BookingKind = N'DayPass') THEN 1 ELSE 0 END", c, tx);
            cmd.Parameters.AddWithValue("@b", bookingId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
        }
    }
}
