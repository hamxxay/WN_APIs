using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>dbo.WN_EmailSenders (script EMAIL_01). Raw SqlClient; a missing table is reported as <see cref="EmailSendersMissingException"/>.</summary>
    public sealed class EmailSenderRepository : IEmailSenderRepository
    {
        private const int CommandTimeoutSeconds = 15;
        private const string SelectSql = "SELECT TypeKey, Mailbox, FromName, UpdatedBy, UpdatedAt FROM dbo.WN_EmailSenders WITH (NOLOCK)";
        private readonly string _connectionString;

        public EmailSenderRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private static EmailSenderRow Map(SqlDataReader r) => new(
            r.GetString(0), r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc));

        public async Task<IReadOnlyList<EmailSenderRow>> GetAllAsync(CancellationToken ct = default)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync(ct);
                await using var cmd = new SqlCommand(SelectSql, c) { CommandTimeout = CommandTimeoutSeconds };
                await using var r = await cmd.ExecuteReaderAsync(ct);
                var list = new List<EmailSenderRow>();
                while (await r.ReadAsync(ct)) list.Add(Map(r));
                return list;
            }
            catch (SqlException ex) when (ex.Number is 208 or 2812) { throw new EmailSendersMissingException(ex); }
        }

        public async Task<EmailSenderRow> SaveAsync(string typeKey, string mailbox, string? fromName, string? userEmail, CancellationToken ct = default)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync(ct);
                await using (var cmd = new SqlCommand(@"
                    UPDATE dbo.WN_EmailSenders SET Mailbox = @Mailbox, FromName = @FromName, UpdatedBy = @By, UpdatedAt = SYSUTCDATETIME() WHERE TypeKey = @Key;
                    IF @@ROWCOUNT = 0
                        INSERT INTO dbo.WN_EmailSenders (TypeKey, Mailbox, FromName, UpdatedBy, UpdatedAt) VALUES (@Key, @Mailbox, @FromName, @By, SYSUTCDATETIME());", c)
                { CommandTimeout = CommandTimeoutSeconds })
                {
                    cmd.Parameters.Add("@Key", SqlDbType.VarChar, 40).Value = typeKey;
                    cmd.Parameters.Add("@Mailbox", SqlDbType.VarChar, 20).Value = mailbox;
                    cmd.Parameters.Add("@FromName", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(fromName) ? DBNull.Value : fromName.Trim();
                    cmd.Parameters.Add("@By", SqlDbType.NVarChar, 256).Value = (object?)userEmail ?? DBNull.Value;
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                await using var sel = new SqlCommand(SelectSql + " WHERE TypeKey = @Key", c) { CommandTimeout = CommandTimeoutSeconds };
                sel.Parameters.Add("@Key", SqlDbType.VarChar, 40).Value = typeKey;
                await using var r = await sel.ExecuteReaderAsync(ct);
                await r.ReadAsync(ct);
                return Map(r);
            }
            catch (SqlException ex) when (ex.Number is 208 or 2812) { throw new EmailSendersMissingException(ex); }
        }
    }

    /// <summary>Caches the saved rows for 60 seconds. Without the table (or when it cannot be read) the defaults apply and the failure is not cached for long.</summary>
    public sealed class EmailSenderResolver : IEmailSenderResolver
    {
        public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
        private readonly IEmailSenderRepository _repo;
        private readonly Func<DateTime> _now;
        private readonly object _gate = new();
        private IReadOnlyDictionary<string, EmailSenderRow>? _rows;
        private bool _loaded;
        private DateTime _expires;

        public EmailSenderResolver(IEmailSenderRepository repo, Func<DateTime>? now = null) { _repo = repo; _now = now ?? (() => DateTime.UtcNow); }

        public async Task<IReadOnlyDictionary<string, EmailSenderRow>?> GetRowsAsync(CancellationToken ct = default)
        {
            lock (_gate) { if (_loaded && _now() < _expires) return _rows; }
            IReadOnlyDictionary<string, EmailSenderRow>? rows = null;
            try { rows = (await _repo.GetAllAsync(ct)).ToDictionary(r => r.TypeKey, StringComparer.OrdinalIgnoreCase); }
            catch (EmailSendersMissingException) { }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* database unreachable: defaults */ }
            lock (_gate) { _rows = rows; _loaded = true; _expires = _now() + Ttl; }
            return rows;
        }

        public async Task<(string Mailbox, string? FromName)> ResolveAsync(string typeKey, CancellationToken ct = default) =>
            EmailSenderRules.Resolve(typeKey, await GetRowsAsync(ct));

        public void Invalidate() { lock (_gate) { _loaded = false; _rows = null; } }
    }
}
