using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Discount caps = dbo.WN_OfferingType.DiscountCap. Raw SqlClient. UpdatedBy / UpdatedAt are used only when the columns exist
    /// (script DISC_01_discount_caps), so the page works before the script runs. There is no cache: quotations and bookings read the
    /// table on every request (DbRepository.GetOfferingTypesAsync), so a saved cap applies at once.
    /// </summary>
    public sealed class DiscountCapsRepository : IDiscountCapsRepository
    {
        private const int CommandTimeoutSeconds = 15;
        private readonly string _connectionString;

        public DiscountCapsRepository(IConfiguration configuration)
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

        private static async Task<bool> HasAuditColumnsAsync(SqlConnection c, CancellationToken ct)
        {
            await using var cmd = new SqlCommand(
                "SELECT CASE WHEN COL_LENGTH(N'dbo.WN_OfferingType', N'UpdatedBy') IS NOT NULL AND COL_LENGTH(N'dbo.WN_OfferingType', N'UpdatedAt') IS NOT NULL THEN 1 ELSE 0 END", c)
            { CommandTimeout = CommandTimeoutSeconds };
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
        }

        private static string SelectSql(bool audit) => $@"
            SELECT o.Id, o.Description, o.DiscountCap, ISNULL(o.Status, 1) AS Status, o.LocationId, l.Name AS LocationName,
                   {(audit ? "o.UpdatedBy" : "CAST(NULL AS NVARCHAR(256))")} AS UpdatedBy,
                   {(audit ? "o.UpdatedAt" : "CAST(NULL AS DATETIME2)")} AS UpdatedAt
            FROM dbo.WN_OfferingType o WITH (NOLOCK)
            LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = o.LocationId";

        private static DiscountCapRow Map(SqlDataReader r) => new(
            r.GetInt32(r.GetOrdinal("Id")),
            r.IsDBNull(r.GetOrdinal("Description")) ? "" : r.GetString(r.GetOrdinal("Description")),
            r.IsDBNull(r.GetOrdinal("DiscountCap")) ? 0m : Convert.ToDecimal(r.GetValue(r.GetOrdinal("DiscountCap"))),
            Convert.ToBoolean(r.GetValue(r.GetOrdinal("Status"))),
            r.IsDBNull(r.GetOrdinal("LocationId")) ? null : Convert.ToInt32(r.GetValue(r.GetOrdinal("LocationId"))),
            r.IsDBNull(r.GetOrdinal("LocationName")) ? null : r.GetString(r.GetOrdinal("LocationName")),
            r.IsDBNull(r.GetOrdinal("UpdatedBy")) ? null : r.GetString(r.GetOrdinal("UpdatedBy")),
            r.IsDBNull(r.GetOrdinal("UpdatedAt")) ? null : DateTime.SpecifyKind(r.GetDateTime(r.GetOrdinal("UpdatedAt")), DateTimeKind.Utc));

        public async Task<IReadOnlyList<DiscountCapRow>> GetAllAsync(CancellationToken ct = default)
        {
            try
            {
                await using var c = await OpenAsync(ct);
                return await ReadAsync(c, null, ct);
            }
            catch (SqlException ex) when (ex.Number is 208 or 2812) { throw new DiscountCapsMissingException(ex); }
        }

        public async Task<IReadOnlyList<(int Id, string Name)>> GetLocationsAsync(CancellationToken ct = default)
        {
            try
            {
                await using var c = await OpenAsync(ct);
                await using var cmd = new SqlCommand("SELECT Id, Name FROM dbo.WN_Locations WITH (NOLOCK) WHERE ISNULL(IsActive, 1) = 1 ORDER BY Id", c) { CommandTimeout = CommandTimeoutSeconds };
                await using var r = await cmd.ExecuteReaderAsync(ct);
                var list = new List<(int, string)>();
                while (await r.ReadAsync(ct)) list.Add((r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1)));
                return list;
            }
            catch (SqlException) { return Array.Empty<(int, string)>(); }   // the caps still show without the location list
        }

        private static async Task<IReadOnlyList<DiscountCapRow>> ReadAsync(SqlConnection c, int? id, CancellationToken ct)
        {
            var audit = await HasAuditColumnsAsync(c, ct);
            await using var cmd = new SqlCommand(SelectSql(audit) + (id.HasValue ? " WHERE o.Id = @Id" : "") + " ORDER BY o.LocationId, o.Id", c) { CommandTimeout = CommandTimeoutSeconds };
            if (id.HasValue) cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id.Value;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var list = new List<DiscountCapRow>();
            while (await r.ReadAsync(ct)) list.Add(Map(r));
            return list;
        }

        /// <summary>The audit identity (WN_AuditLog triggers read SESSION_CONTEXT), as OffersRepository sets it. Never blocks the change.</summary>
        private static async Task SetAuditContextAsync(SqlConnection c, string? userEmail, CancellationToken ct)
        {
            try
            {
                await using var cmd = new SqlCommand(@"
                    SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
                    DECLARE @ResolvedId INT = NULL;
                    IF @UserEmail IS NOT NULL SELECT TOP 1 @ResolvedId = Id FROM dbo.WN_Users WITH (NOLOCK) WHERE Email = @UserEmail;
                    IF @ResolvedId IS NOT NULL EXEC sp_set_session_context @key = N'AppUserId', @value = @ResolvedId;
                    IF @UserEmail IS NOT NULL EXEC sp_set_session_context @key = N'AppUserEmail', @value = @UserEmail;
                    EXEC sp_set_session_context @key = N'SourceApp', @value = N'WebAPI';", c);
                cmd.Parameters.Add("@UserEmail", SqlDbType.NVarChar, 256).Value = (object?)userEmail ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }

        public async Task<DiscountCapRow?> UpdateAsync(int id, decimal discountCap, string? userEmail, CancellationToken ct = default)
        {
            try
            {
                await using var c = await OpenAsync(ct);
                if ((await ReadAsync(c, id, ct)).Count == 0) return null;
                await SetAuditContextAsync(c, userEmail, ct);
                var audit = await HasAuditColumnsAsync(c, ct);
                await using (var cmd = new SqlCommand(
                    "UPDATE dbo.WN_OfferingType SET DiscountCap = @Cap" + (audit ? ", UpdatedBy = @By, UpdatedAt = SYSUTCDATETIME()" : "") + " WHERE Id = @Id", c)
                { CommandTimeout = CommandTimeoutSeconds })
                {
                    var p = cmd.Parameters.Add("@Cap", SqlDbType.Decimal); p.Precision = 5; p.Scale = 2; p.Value = discountCap;
                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    if (audit) cmd.Parameters.Add("@By", SqlDbType.NVarChar, 256).Value = (object?)userEmail ?? DBNull.Value;
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                return (await ReadAsync(c, id, ct)).FirstOrDefault();
            }
            catch (SqlException ex) when (ex.Number is 208 or 2812) { throw new DiscountCapsMissingException(ex); }
        }
    }
}
