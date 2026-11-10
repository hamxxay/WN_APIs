using System.Data;
using Microsoft.Data.SqlClient;
using WorkNest.Application.DTOs.Offers;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Reads and writes the 10 offer snapshot columns (script OFFER_01_offers) of dbo.WN_Quotations / WN_Bookings / WN_Invoices
    /// with plain SQL on a connection the caller already has. Defensive: SQL error 207 (a column is missing) or 208 (a table is
    /// missing) means the script has not run on this server, so a read answers "no offer" and a write is skipped (returns false).
    /// </summary>
    public static class OfferSnapshotStore
    {
        public const string Columns =
            "OfferId, OfferName, OfferDiscountType, OfferDiscountValue, OfferApplyTo, OfferApplyToDeposit, OfferDiscountAmount, ManualDiscountAmount, FinalMonthlyRent, FinalSecurityDeposit";

        public static string TableOf(OfferEntity e) => e switch
        {
            OfferEntity.Quotation => "dbo.WN_Quotations",
            OfferEntity.Booking => "dbo.WN_Bookings",
            _ => "dbo.WN_Invoices"
        };

        private static bool ColumnsMissing(SqlException ex) => ex.Number is 207 or 208 or 2812;

        private static decimal? Dec(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));

        /// <summary>The snapshots of these rows that carry an offer (OfferId not null). Empty when the columns do not exist.</summary>
        public static async Task<Dictionary<int, OfferSnapshot>> ReadAsync(SqlConnection c, OfferEntity entity, IEnumerable<int> ids, CancellationToken ct = default)
        {
            var result = new Dictionary<int, OfferSnapshot>();
            var list = ids.Where(i => i > 0).Distinct().ToList();
            if (list.Count == 0) return result;
            try
            {
                // ids are ints (never text), so the inline list is safe.
                string sql = $"SELECT Id, {Columns} FROM {TableOf(entity)} WITH (NOLOCK) WHERE OfferId IS NOT NULL AND Id IN ({string.Join(",", list)})";
                await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 15 };
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    result[r.GetInt32(0)] = new OfferSnapshot
                    {
                        OfferId = r.IsDBNull(1) ? null : r.GetInt32(1),
                        OfferName = r.IsDBNull(2) ? null : r.GetString(2),
                        OfferDiscountType = r.IsDBNull(3) ? null : r.GetString(3),
                        OfferDiscountValue = Dec(r, 4),
                        OfferApplyTo = r.IsDBNull(5) ? null : r.GetString(5),
                        OfferApplyToDeposit = r.IsDBNull(6) ? null : r.GetBoolean(6),
                        OfferDiscountAmount = Dec(r, 7),
                        ManualDiscountAmount = Dec(r, 8),
                        FinalMonthlyRent = Dec(r, 9),
                        FinalSecurityDeposit = Dec(r, 10)
                    };
                }
            }
            catch (SqlException ex) when (ColumnsMissing(ex)) { result.Clear(); }
            return result;
        }

        /// <summary>One row's snapshot, or null (no offer / columns missing).</summary>
        public static async Task<OfferSnapshot?> ReadOneAsync(SqlConnection c, OfferEntity entity, int id, CancellationToken ct = default) =>
            (await ReadAsync(c, entity, new[] { id }, ct)).TryGetValue(id, out var s) ? s : null;

        private static void AddParams(SqlCommand cmd, OfferSnapshot s)
        {
            cmd.Parameters.Add("@OfferId", SqlDbType.Int).Value = (object?)s.OfferId ?? DBNull.Value;
            cmd.Parameters.Add("@OfferName", SqlDbType.NVarChar, 100).Value = (object?)s.OfferName ?? DBNull.Value;
            cmd.Parameters.Add("@OfferDiscountType", SqlDbType.VarChar, 10).Value = (object?)s.OfferDiscountType ?? DBNull.Value;
            cmd.Parameters.Add("@OfferDiscountValue", SqlDbType.Decimal).Value = (object?)s.OfferDiscountValue ?? DBNull.Value;
            cmd.Parameters.Add("@OfferApplyTo", SqlDbType.VarChar, 20).Value = (object?)s.OfferApplyTo ?? DBNull.Value;
            cmd.Parameters.Add("@OfferApplyToDeposit", SqlDbType.Bit).Value = (object?)s.OfferApplyToDeposit ?? DBNull.Value;
            cmd.Parameters.Add("@OfferDiscountAmount", SqlDbType.Decimal).Value = (object?)s.OfferDiscountAmount ?? DBNull.Value;
            cmd.Parameters.Add("@ManualDiscountAmount", SqlDbType.Decimal).Value = (object?)s.ManualDiscountAmount ?? DBNull.Value;
            cmd.Parameters.Add("@FinalMonthlyRent", SqlDbType.Decimal).Value = (object?)s.FinalMonthlyRent ?? DBNull.Value;
            cmd.Parameters.Add("@FinalSecurityDeposit", SqlDbType.Decimal).Value = (object?)s.FinalSecurityDeposit ?? DBNull.Value;
            foreach (SqlParameter p in cmd.Parameters)
                if (p.SqlDbType == SqlDbType.Decimal) { p.Precision = 18; p.Scale = 2; }
        }

        /// <summary>Writes the 10 columns of one row. False when the columns do not exist yet (nothing is written).</summary>
        public static async Task<bool> WriteAsync(SqlConnection c, OfferEntity entity, int id, OfferSnapshot snapshot, CancellationToken ct = default)
        {
            try
            {
                string sql = $@"UPDATE {TableOf(entity)} SET OfferId = @OfferId, OfferName = @OfferName, OfferDiscountType = @OfferDiscountType,
                    OfferDiscountValue = @OfferDiscountValue, OfferApplyTo = @OfferApplyTo, OfferApplyToDeposit = @OfferApplyToDeposit,
                    OfferDiscountAmount = @OfferDiscountAmount, ManualDiscountAmount = @ManualDiscountAmount,
                    FinalMonthlyRent = @FinalMonthlyRent, FinalSecurityDeposit = @FinalSecurityDeposit WHERE Id = @Id";
                await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 15 };
                AddParams(cmd, snapshot);
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                await cmd.ExecuteNonQueryAsync(ct);
                return true;
            }
            catch (SqlException ex) when (ColumnsMissing(ex)) { return false; }
        }

        /// <summary>Copies the 10 columns of a quotation onto a booking (the booking never re-evaluates WN_Offers). False when the columns do not exist.</summary>
        public static async Task<bool> CopyQuotationToBookingAsync(SqlConnection c, int quotationId, int bookingId, CancellationToken ct = default)
        {
            try
            {
                const string sql = @"UPDATE b SET b.OfferId = q.OfferId, b.OfferName = q.OfferName, b.OfferDiscountType = q.OfferDiscountType,
                    b.OfferDiscountValue = q.OfferDiscountValue, b.OfferApplyTo = q.OfferApplyTo, b.OfferApplyToDeposit = q.OfferApplyToDeposit,
                    b.OfferDiscountAmount = q.OfferDiscountAmount, b.ManualDiscountAmount = q.ManualDiscountAmount,
                    b.FinalMonthlyRent = q.FinalMonthlyRent, b.FinalSecurityDeposit = q.FinalSecurityDeposit
                    FROM dbo.WN_Bookings b JOIN dbo.WN_Quotations q ON q.Id = @QuotationId WHERE b.Id = @BookingId";
                await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 15 };
                cmd.Parameters.Add("@QuotationId", SqlDbType.Int).Value = quotationId;
                cmd.Parameters.Add("@BookingId", SqlDbType.Int).Value = bookingId;
                await cmd.ExecuteNonQueryAsync(ct);
                return true;
            }
            catch (SqlException ex) when (ColumnsMissing(ex)) { return false; }
        }

        /// <summary>Copies the 10 columns of a quotation onto another quotation (a new version keeps the offer snapshot).</summary>
        public static async Task<bool> CopyQuotationToQuotationAsync(SqlConnection c, int sourceQuotationId, int newQuotationId, CancellationToken ct = default)
        {
            try
            {
                const string sql = @"UPDATE n SET n.OfferId = q.OfferId, n.OfferName = q.OfferName, n.OfferDiscountType = q.OfferDiscountType,
                    n.OfferDiscountValue = q.OfferDiscountValue, n.OfferApplyTo = q.OfferApplyTo, n.OfferApplyToDeposit = q.OfferApplyToDeposit,
                    n.OfferDiscountAmount = q.OfferDiscountAmount, n.ManualDiscountAmount = q.ManualDiscountAmount,
                    n.FinalMonthlyRent = q.FinalMonthlyRent, n.FinalSecurityDeposit = q.FinalSecurityDeposit
                    FROM dbo.WN_Quotations n JOIN dbo.WN_Quotations q ON q.Id = @SourceId WHERE n.Id = @NewId";
                await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 15 };
                cmd.Parameters.Add("@SourceId", SqlDbType.Int).Value = sourceQuotationId;
                cmd.Parameters.Add("@NewId", SqlDbType.Int).Value = newQuotationId;
                await cmd.ExecuteNonQueryAsync(ct);
                return true;
            }
            catch (SqlException ex) when (ColumnsMissing(ex)) { return false; }
        }

        /// <summary>Clears the 10 columns of a booking (a booking that must carry no offer).</summary>
        public static async Task<bool> ClearAsync(SqlConnection c, OfferEntity entity, int id, CancellationToken ct = default)
        {
            try
            {
                string sql = $@"UPDATE {TableOf(entity)} SET OfferId = NULL, OfferName = NULL, OfferDiscountType = NULL, OfferDiscountValue = NULL,
                    OfferApplyTo = NULL, OfferApplyToDeposit = NULL, OfferDiscountAmount = NULL, ManualDiscountAmount = NULL,
                    FinalMonthlyRent = NULL, FinalSecurityDeposit = NULL WHERE Id = @Id";
                await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 15 };
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                await cmd.ExecuteNonQueryAsync(ct);
                return true;
            }
            catch (SqlException ex) when (ColumnsMissing(ex)) { return false; }
        }
    }
}
