using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.InvoiceContent;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// dbo.WN_InvoiceContentVersions / dbo.WN_InvoiceContentSettings / WN_Invoices.InvoiceContentVersionId of script
    /// INVC_01_location_invoice_content. Parameterised SQL. SQL error 207 / 208 (column / table missing) = the script has not run:
    /// <see cref="InvoiceContentMissingException"/> (the PDFs then print today's wording, the admin endpoints answer 503).
    /// </summary>
    public sealed class InvoiceContentRepository : IInvoiceContentRepository
    {
        private const int CommandTimeoutSeconds = 15;
        private readonly string _connectionString;

        public InvoiceContentRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private const string Columns = "Id, LocationId, VersionNo, Status, ContentJson, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, PublishedAt, PublishedBy";

        private async Task<T> Run<T>(Func<SqlConnection, Task<T>> action)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                return await action(c);
            }
            catch (SqlException ex) when (ex.Number is 207 or 208 or 2812) { throw new InvoiceContentMissingException(ex); }
        }

        private static SqlCommand Cmd(SqlConnection c, string sql, CancellationToken ct, params (string, SqlDbType, object?)[] ps)
        {
            var cmd = new SqlCommand(sql, c) { CommandTimeout = CommandTimeoutSeconds };
            foreach (var (n, t, v) in ps) cmd.Parameters.Add(n, t).Value = v ?? DBNull.Value;
            return cmd;
        }

        private static string? Str(IDataRecord r, string col) => r[col] is string s ? s : null;
        private static DateTime? Dt(IDataRecord r, string col) => r[col] is DateTime d ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified) : null;

        private static InvoiceContentVersionRow Map(IDataRecord r) => new(
            (int)r["Id"], (int)r["LocationId"], r["VersionNo"] is int n ? n : null, (string)r["Status"], (string)r["ContentJson"],
            Dt(r, "CreatedAt")!.Value, Str(r, "CreatedBy"), Dt(r, "UpdatedAt"), Str(r, "UpdatedBy"), Dt(r, "PublishedAt"), Str(r, "PublishedBy"));

        private static async Task<List<InvoiceContentVersionRow>> Rows(SqlCommand cmd, CancellationToken ct)
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var list = new List<InvoiceContentVersionRow>();
            while (await r.ReadAsync(ct)) list.Add(Map(r));
            return list;
        }

        public Task<IReadOnlyList<InvoiceContentLocationRow>> GetLocationsAsync(CancellationToken ct = default) => Run<IReadOnlyList<InvoiceContentLocationRow>>(async c =>
        {
            // Known locations (1, 2, 3: Location 3 may have no WN_Locations row yet) plus every real location; HasContent = has any version row.
            const string sql = @"
                SELECT ids.LocationId, l.Name AS LocationName,
                       CASE WHEN EXISTS (SELECT 1 FROM dbo.WN_InvoiceContentVersions v WHERE v.LocationId = ids.LocationId) THEN 1 ELSE 0 END AS HasContent
                  FROM (SELECT LocationId FROM (VALUES (1), (2), (3)) k(LocationId)
                        UNION SELECT Id FROM dbo.WN_Locations) ids
                  LEFT JOIN dbo.WN_Locations l ON l.Id = ids.LocationId
                 ORDER BY ids.LocationId;";
            await using var cmd = Cmd(c, sql, ct);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var list = new List<InvoiceContentLocationRow>();
            while (await r.ReadAsync(ct)) list.Add(new InvoiceContentLocationRow((int)r["LocationId"], Str(r, "LocationName"), (int)r["HasContent"] == 1));
            return list;
        });

        public Task<IReadOnlyList<InvoiceContentVersionRow>> GetVersionsAsync(int locationId, CancellationToken ct = default) => Run<IReadOnlyList<InvoiceContentVersionRow>>(async c =>
        {
            await using var cmd = Cmd(c, $"SELECT {Columns} FROM dbo.WN_InvoiceContentVersions WHERE LocationId = @loc ORDER BY ISNULL(VersionNo, 2147483647), Id;", ct, ("@loc", SqlDbType.Int, locationId));
            return await Rows(cmd, ct);
        });

        public Task<InvoiceContentVersionRow?> GetVersionAsync(int versionId, CancellationToken ct = default) => Run<InvoiceContentVersionRow?>(async c =>
        {
            await using var cmd = Cmd(c, $"SELECT {Columns} FROM dbo.WN_InvoiceContentVersions WHERE Id = @id;", ct, ("@id", SqlDbType.Int, versionId));
            return (await Rows(cmd, ct)).FirstOrDefault();
        });

        public Task<InvoiceContentVersionRow?> GetVersionByNoAsync(int locationId, int versionNo, CancellationToken ct = default) => Run<InvoiceContentVersionRow?>(async c =>
        {
            await using var cmd = Cmd(c, $"SELECT {Columns} FROM dbo.WN_InvoiceContentVersions WHERE LocationId = @loc AND VersionNo = @no;", ct,
                ("@loc", SqlDbType.Int, locationId), ("@no", SqlDbType.Int, versionNo));
            return (await Rows(cmd, ct)).FirstOrDefault();
        });

        public Task<InvoiceContentVersionRow?> GetPublishedAsync(int locationId, CancellationToken ct = default) => Run<InvoiceContentVersionRow?>(async c =>
        {
            await using var cmd = Cmd(c, $"SELECT {Columns} FROM dbo.WN_InvoiceContentVersions WHERE LocationId = @loc AND Status = 'Published';", ct, ("@loc", SqlDbType.Int, locationId));
            return (await Rows(cmd, ct)).FirstOrDefault();
        });

        public Task<bool> EnsureSeedAsync(int locationId, string contentJson, string? by, CancellationToken ct = default) => Run(async c =>
        {
            const string sql = @"
                INSERT INTO dbo.WN_InvoiceContentVersions (LocationId, VersionNo, Status, ContentJson, CreatedAt, CreatedBy, PublishedAt, PublishedBy)
                SELECT @loc, 1, 'Published', @json, DATEADD(HOUR, 5, SYSUTCDATETIME()), @by, DATEADD(HOUR, 5, SYSUTCDATETIME()), @by
                 WHERE NOT EXISTS (SELECT 1 FROM dbo.WN_InvoiceContentVersions WITH (UPDLOCK, HOLDLOCK) WHERE LocationId = @loc);";
            try
            {
                await using var cmd = Cmd(c, sql, ct, ("@loc", SqlDbType.Int, locationId), ("@json", SqlDbType.NVarChar, contentJson), ("@by", SqlDbType.NVarChar, by));
                return await cmd.ExecuteNonQueryAsync(ct) > 0;
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) { return false; }   // seeded in parallel
        });

        public Task<InvoiceContentVersionRow> SaveDraftAsync(int locationId, string contentJson, string? by, CancellationToken ct = default) => Run(async c =>
        {
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                UPDATE dbo.WN_InvoiceContentVersions SET ContentJson = @json, UpdatedAt = DATEADD(HOUR, 5, SYSUTCDATETIME()), UpdatedBy = @by
                 WHERE LocationId = @loc AND Status = 'Draft';
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.WN_InvoiceContentVersions (LocationId, VersionNo, Status, ContentJson, CreatedAt, CreatedBy)
                    VALUES (@loc, NULL, 'Draft', @json, DATEADD(HOUR, 5, SYSUTCDATETIME()), @by);
                COMMIT TRANSACTION;
                SELECT " + Columns + " FROM dbo.WN_InvoiceContentVersions WHERE LocationId = @loc AND Status = 'Draft';";
            try
            {
                await using var cmd = Cmd(c, sql, ct, ("@loc", SqlDbType.Int, locationId), ("@json", SqlDbType.NVarChar, contentJson), ("@by", SqlDbType.NVarChar, by));
                return (await Rows(cmd, ct)).First();
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw new InvoiceContentConflictException("Someone else saved a draft at the same moment. Reload the page and try again."); }
        });

        public Task<bool> DeleteDraftAsync(int locationId, CancellationToken ct = default) => Run(async c =>
        {
            await using var cmd = Cmd(c, "DELETE FROM dbo.WN_InvoiceContentVersions WHERE LocationId = @loc AND Status = 'Draft';", ct, ("@loc", SqlDbType.Int, locationId));
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        });

        public Task<InvoiceContentVersionRow?> PublishDraftAsync(int locationId, string? by, CancellationToken ct = default) => Run<InvoiceContentVersionRow?>(async c =>
        {
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                DECLARE @draft INT = (SELECT Id FROM dbo.WN_InvoiceContentVersions WITH (UPDLOCK, HOLDLOCK) WHERE LocationId = @loc AND Status = 'Draft');
                IF @draft IS NULL
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT CAST(NULL AS INT) AS Id WHERE 1 = 0;
                END
                ELSE
                BEGIN
                    DECLARE @next INT = ISNULL((SELECT MAX(VersionNo) FROM dbo.WN_InvoiceContentVersions WITH (UPDLOCK, HOLDLOCK) WHERE LocationId = @loc), 0) + 1;
                    DECLARE @now DATETIME2(0) = DATEADD(HOUR, 5, SYSUTCDATETIME());
                    UPDATE dbo.WN_InvoiceContentVersions SET Status = 'Superseded' WHERE LocationId = @loc AND Status = 'Published';
                    UPDATE dbo.WN_InvoiceContentVersions SET Status = 'Published', VersionNo = @next, PublishedAt = @now, PublishedBy = @by WHERE Id = @draft;
                    COMMIT TRANSACTION;
                    SELECT " + Columns + @" FROM dbo.WN_InvoiceContentVersions WHERE Id = @draft;
                END";
            await using var cmd = Cmd(c, sql, ct, ("@loc", SqlDbType.Int, locationId), ("@by", SqlDbType.NVarChar, by));
            return (await Rows(cmd, ct)).FirstOrDefault();
        });

        public Task<InvoiceContentInvoiceRef?> GetInvoiceRefAsync(int invoiceId, CancellationToken ct = default) => Run<InvoiceContentInvoiceRef?>(async c =>
        {
            const string sql = @"
                SELECT i.Id, i.LocationId, i.InvoiceContentVersionId, ISNULL((SELECT s.ActivatedFromInvoiceId FROM dbo.WN_InvoiceContentSettings s WHERE s.Id = 1), 0) AS ActivatedFromInvoiceId
                  FROM dbo.WN_Invoices i WHERE i.Id = @id;";
            await using var cmd = Cmd(c, sql, ct, ("@id", SqlDbType.Int, invoiceId));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            return new InvoiceContentInvoiceRef((int)r["Id"], r["LocationId"] is int l ? l : null, r["InvoiceContentVersionId"] is int v ? v : null, Convert.ToInt32(r["ActivatedFromInvoiceId"]));
        });

        public Task<int?> StampInvoiceAsync(int invoiceId, int versionId, CancellationToken ct = default) => Run<int?>(async c =>
        {
            const string sql = @"
                UPDATE dbo.WN_Invoices SET InvoiceContentVersionId = @v WHERE Id = @id AND InvoiceContentVersionId IS NULL;
                SELECT InvoiceContentVersionId FROM dbo.WN_Invoices WHERE Id = @id;";
            await using var cmd = Cmd(c, sql, ct, ("@id", SqlDbType.Int, invoiceId), ("@v", SqlDbType.Int, versionId));
            return await cmd.ExecuteScalarAsync(ct) is int stamped ? stamped : null;
        });
    }
}
