using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.EmailOutbox;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Pending emails: dbo.WN_EmailOutbox_* (script SS_02_email_sending). Own connection (singleton; used by EmailService,
    /// the outbox sender and the CMS). SQL errors 2812 / 208 (procedure / table missing) mean SS_02 has not run:
    /// <see cref="EmailOutboxMissingException"/>.
    /// </summary>
    public class EmailOutboxRepository : IEmailOutbox
    {
        private const int CommandTimeoutSeconds = 30;

        private readonly string _connectionString;

        public EmailOutboxRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        public async Task<int> AddAsync(string kind, string toEmail, string subject, string messageJson, CancellationToken ct = default)
        {
            return await RunAsync("dbo.WN_EmailOutbox_Add", p =>
            {
                p.Add("@Kind", SqlDbType.NVarChar, 100).Value = Cut(kind, 100);
                p.Add("@ToEmail", SqlDbType.NVarChar, 1000).Value = Cut(toEmail, 1000);
                p.Add("@Subject", SqlDbType.NVarChar, 500).Value = Cut(subject, 500);
                p.Add("@MessageJson", SqlDbType.NVarChar, -1).Value = messageJson;
            }, async r => await r.ReadAsync(ct) ? Convert.ToInt32(r["Id"]) : 0, ct);
        }

        public async Task<(EmailOutboxCounts Counts, List<EmailOutboxItem> Items)> GetListAsync(string? status, int top, CancellationToken ct = default)
        {
            return await RunAsync("dbo.WN_EmailOutbox_GetList", p =>
            {
                p.Add("@Status", SqlDbType.NVarChar, 20).Value = (object?)status ?? DBNull.Value;
                p.Add("@Top", SqlDbType.Int).Value = top;
            }, async r =>
            {
                var counts = new EmailOutboxCounts();
                if (await r.ReadAsync(ct))
                {
                    counts.Pending = IntOf(r["Pending"]);
                    counts.Sending = IntOf(r["Sending"]);
                    counts.Sent = IntOf(r["Sent"]);
                    counts.Failed = IntOf(r["Failed"]);
                    counts.Discarded = IntOf(r["Discarded"]);
                }
                var items = new List<EmailOutboxItem>();
                if (await r.NextResultAsync(ct))
                    while (await r.ReadAsync(ct))
                        items.Add(new EmailOutboxItem
                        {
                            Id = Convert.ToInt32(r["Id"]),
                            Kind = Convert.ToString(r["Kind"]) ?? "",
                            ToEmail = Convert.ToString(r["ToEmail"]) ?? "",
                            Subject = Convert.ToString(r["Subject"]) ?? "",
                            Status = Convert.ToString(r["Status"]) ?? "",
                            Attempts = IntOf(r["Attempts"]),
                            LastError = r["LastError"] is DBNull ? null : Convert.ToString(r["LastError"]),
                            CreatedOn = Utc(r["CreatedOn"]) ?? default,
                            SentOn = Utc(r["SentOn"]),
                            DiscardedOn = Utc(r["DiscardedOn"]),
                            DiscardedBy = r["DiscardedBy"] is DBNull ? null : Convert.ToString(r["DiscardedBy"])
                        });
                return (counts, items);
            }, ct);
        }

        public async Task<List<ClaimedEmail>> ClaimAsync(int? id, int top, CancellationToken ct = default)
        {
            return await RunAsync("dbo.WN_EmailOutbox_Claim", p =>
            {
                p.Add("@Id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
                p.Add("@Top", SqlDbType.Int).Value = top;
            }, async r =>
            {
                var list = new List<ClaimedEmail>();
                while (await r.ReadAsync(ct))
                    list.Add(new ClaimedEmail
                    {
                        Id = Convert.ToInt32(r["Id"]),
                        Kind = Convert.ToString(r["Kind"]) ?? "",
                        ToEmail = Convert.ToString(r["ToEmail"]) ?? "",
                        Subject = Convert.ToString(r["Subject"]) ?? "",
                        MessageJson = Convert.ToString(r["MessageJson"]) ?? "",
                        Attempts = IntOf(r["Attempts"])
                    });
                return list;
            }, ct);
        }

        public async Task FinishAsync(int id, bool sent, string? error, CancellationToken ct = default)
        {
            await RunAsync("dbo.WN_EmailOutbox_Finish", p =>
            {
                p.Add("@Id", SqlDbType.Int).Value = id;
                p.Add("@Sent", SqlDbType.Bit).Value = sent;
                p.Add("@Error", SqlDbType.NVarChar, 1000).Value = (object?)Cut(error, 1000) ?? DBNull.Value;
            }, _ => Task.FromResult(0), ct);
        }

        public async Task<int> DiscardAsync(int? id, string? by, CancellationToken ct = default)
        {
            return await RunAsync("dbo.WN_EmailOutbox_Discard", p =>
            {
                p.Add("@Id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
                p.Add("@By", SqlDbType.NVarChar, 256).Value = (object?)Cut(by, 256) ?? DBNull.Value;
            }, async r => await r.ReadAsync(ct) ? IntOf(r["Discarded"]) : 0, ct);
        }

        private async Task<T> RunAsync<T>(string procedure, Action<SqlParameterCollection> bind, Func<SqlDataReader, Task<T>> read, CancellationToken ct)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync(ct);
                await using var cmd = new SqlCommand(procedure, c) { CommandType = CommandType.StoredProcedure, CommandTimeout = CommandTimeoutSeconds };
                bind(cmd.Parameters);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                return await read(r);
            }
            catch (SqlException ex) when (ex.Number is 2812 or 208) // procedure / table missing: SS_02 not run
            {
                throw new EmailOutboxMissingException(ex);
            }
        }

        private static int IntOf(object v) => v is DBNull ? 0 : Convert.ToInt32(v);

        // DATETIME2 written with SYSUTCDATETIME(): UTC (JSON "Z").
        private static DateTime? Utc(object v) => v is DBNull ? null : DateTime.SpecifyKind(Convert.ToDateTime(v), DateTimeKind.Utc);

        private static string? Cut(string? value, int max) => value == null ? null : value.Length <= max ? value : value[..max];
    }
}
