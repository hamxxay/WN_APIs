using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Offers;
using WorkNest.Application.DTOs.Quotation;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Quotation discount approval (script QAPR_01_quotation_approval): seven columns on dbo.WN_Quotations and dbo.WN_QuotationHistory,
    /// plain SQL. <see cref="IsSetUpAsync"/> asks the catalog once (a yes is kept, a no is re-checked every minute), so every quotation
    /// path keeps its old behaviour on a server where the script has not run.
    /// </summary>
    public sealed class QuotationApprovalRepository : IQuotationApprovalRepository
    {
        private const int CommandTimeoutSeconds = 15;
        private static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(1);
        private static volatile bool _setUp;
        private static long _lastCheckTicks;

        private readonly string _connectionString;

        public QuotationApprovalRepository(IConfiguration configuration)
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

        private static DateTime? Utc(object? v) => v is DateTime d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
        private static string? Str(object? v) => v is null or DBNull ? null : v.ToString();
        private static int? Int(object? v) => v is null or DBNull ? null : Convert.ToInt32(v);

        public async Task<bool> IsSetUpAsync(CancellationToken ct = default)
        {
            if (_setUp) return true;
            long last = Interlocked.Read(ref _lastCheckTicks);
            if (last != 0 && DateTime.UtcNow.Ticks - last < RecheckEvery.Ticks) return false;
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(@"SELECT CASE WHEN COL_LENGTH(N'dbo.WN_Quotations', N'DiscountReason') IS NOT NULL
                AND COL_LENGTH(N'dbo.WN_Quotations', N'ApprovalStatus') IS NOT NULL
                AND COL_LENGTH(N'dbo.WN_Quotations', N'ApprovalManagerUserId') IS NOT NULL
                AND COL_LENGTH(N'dbo.WN_Quotations', N'ApprovalSubmittedAt') IS NOT NULL
                AND COL_LENGTH(N'dbo.WN_Quotations', N'ApprovalDecidedAt') IS NOT NULL
                AND COL_LENGTH(N'dbo.WN_Quotations', N'ApprovalDecidedByUserId') IS NOT NULL
                AND COL_LENGTH(N'dbo.WN_Quotations', N'ApprovalNote') IS NOT NULL
                AND COL_LENGTH(N'dbo.WN_Quotations', N'LastReminderAt') IS NOT NULL
                AND OBJECT_ID(N'dbo.WN_QuotationHistory', N'U') IS NOT NULL THEN 1 ELSE 0 END", c) { CommandTimeout = CommandTimeoutSeconds };
            bool ok = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
            if (ok) _setUp = true; else Interlocked.Exchange(ref _lastCheckTicks, DateTime.UtcNow.Ticks);
            return ok;
        }

        private const string InfoSelect = @"SELECT q.Id, q.DiscountReason, q.ApprovalStatus, q.ApprovalManagerUserId, mu.Name AS ManagerName,
                q.ApprovalSubmittedAt, q.ApprovalDecidedAt, q.ApprovalDecidedByUserId, q.ApprovalNote, q.LastReminderAt, q.CreatedById,
                cu.Name AS ExecutiveName, s.LocationId,
                (SELECT TOP 1 h.ByUserId FROM dbo.WN_QuotationHistory h WITH (NOLOCK)
                   WHERE h.QuotationId = q.Id AND h.Action IN ('submitted', 'resubmitted') ORDER BY h.Id DESC) AS SubmittedByUserId
            FROM dbo.WN_Quotations q WITH (NOLOCK)
            LEFT JOIN dbo.WN_Users mu WITH (NOLOCK) ON mu.Id = q.ApprovalManagerUserId
            LEFT JOIN dbo.WN_Users cu WITH (NOLOCK) ON cu.Id = q.CreatedById
            LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = q.SpaceId";

        private static QuotationApprovalInfo MapInfo(SqlDataReader r) => new()
        {
            QuotationId = r.GetInt32(r.GetOrdinal("Id")),
            DiscountReason = Str(r["DiscountReason"]),
            ApprovalStatus = Str(r["ApprovalStatus"]),
            ManagerUserId = Int(r["ApprovalManagerUserId"]),
            ManagerName = Str(r["ManagerName"]),
            SubmittedAt = Utc(r["ApprovalSubmittedAt"] is DBNull ? null : r["ApprovalSubmittedAt"]),
            DecidedAt = Utc(r["ApprovalDecidedAt"] is DBNull ? null : r["ApprovalDecidedAt"]),
            DecidedByUserId = Int(r["ApprovalDecidedByUserId"]),
            Note = Str(r["ApprovalNote"]),
            LastReminderAt = Utc(r["LastReminderAt"] is DBNull ? null : r["LastReminderAt"]),
            CreatedByUserId = Int(r["CreatedById"]),
            SubmittedByUserId = Int(r["SubmittedByUserId"]),
            ExecutiveName = Str(r["ExecutiveName"]),
            LocationId = Int(r["LocationId"])
        };

        public async Task<QuotationApprovalInfo?> GetInfoAsync(int quotationId, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(InfoSelect + " WHERE q.Id = @Id", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = quotationId;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            return await r.ReadAsync(ct) ? MapInfo(r) : null;
        }

        public async Task SetDiscountReasonAsync(int quotationId, string? reason, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand("UPDATE dbo.WN_Quotations SET DiscountReason = @R WHERE Id = @Id", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@R", SqlDbType.NVarChar, 1000).Value = (object?)reason ?? DBNull.Value;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = quotationId;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<bool> SubmitAsync(int quotationId, int managerUserId, DateTime nowUtc, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(@"UPDATE dbo.WN_Quotations SET ApprovalStatus = 'PendingManager', ApprovalManagerUserId = @M,
                    ApprovalSubmittedAt = @Now, ApprovalDecidedAt = NULL, ApprovalDecidedByUserId = NULL, ApprovalNote = NULL, LastReminderAt = NULL
                WHERE Id = @Id AND ISNULL(ApprovalStatus, '') <> 'PendingManager'", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@M", SqlDbType.Int).Value = managerUserId;
            cmd.Parameters.Add("@Now", SqlDbType.DateTime2).Value = nowUtc;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = quotationId;
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }

        public async Task<bool> DecideAsync(int quotationId, string status, int decidedByUserId, string? note, DateTime nowUtc, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(@"UPDATE dbo.WN_Quotations SET ApprovalStatus = @S, ApprovalDecidedAt = @Now,
                    ApprovalDecidedByUserId = @By, ApprovalNote = @Note
                WHERE Id = @Id AND ApprovalStatus = 'PendingManager'", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@S", SqlDbType.VarChar, 20).Value = status;
            cmd.Parameters.Add("@Now", SqlDbType.DateTime2).Value = nowUtc;
            cmd.Parameters.Add("@By", SqlDbType.Int).Value = decidedByUserId;
            cmd.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = (object?)note ?? DBNull.Value;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = quotationId;
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }

        public async Task AddHistoryAsync(QuotationHistoryWrite e, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(@"INSERT INTO dbo.WN_QuotationHistory (QuotationId, At, ByUserId, ByName, Action, FromDiscount, ToDiscount, Note)
                VALUES (@Q, @At, @By, @ByName, @Action, @From, @To, @Note)", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@Q", SqlDbType.Int).Value = e.QuotationId;
            cmd.Parameters.Add("@At", SqlDbType.DateTime2).Value = e.At ?? DateTime.UtcNow;
            cmd.Parameters.Add("@By", SqlDbType.Int).Value = (object?)e.ByUserId ?? DBNull.Value;
            cmd.Parameters.Add("@ByName", SqlDbType.NVarChar, 200).Value = (object?)Trunc(e.ByName, 200) ?? DBNull.Value;
            cmd.Parameters.Add("@Action", SqlDbType.VarChar, 30).Value = e.Action;
            cmd.Parameters.Add("@From", SqlDbType.NVarChar, 100).Value = (object?)Trunc(e.FromDiscount, 100) ?? DBNull.Value;
            cmd.Parameters.Add("@To", SqlDbType.NVarChar, 100).Value = (object?)Trunc(e.ToDiscount, 100) ?? DBNull.Value;
            cmd.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = (object?)Trunc(e.Note, 1000) ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static string? Trunc(string? s, int max) => s is null ? null : (s.Length <= max ? s : s[..max]);

        public async Task<IReadOnlyList<QuotationHistoryEntryDto>> GetHistoryAsync(int quotationId, CancellationToken ct = default)
        {
            var list = new List<QuotationHistoryEntryDto>();
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(@"SELECT At, ByName, Action, FromDiscount, ToDiscount, Note FROM dbo.WN_QuotationHistory WITH (NOLOCK)
                WHERE QuotationId = @Q ORDER BY At, Id", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@Q", SqlDbType.Int).Value = quotationId;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                list.Add(new QuotationHistoryEntryDto
                {
                    At = DateTime.SpecifyKind(r.GetDateTime(0), DateTimeKind.Utc),
                    ByName = Str(r[1]), Action = r.GetString(2), FromDiscount = Str(r[3]), ToDiscount = Str(r[4]), Note = Str(r[5])
                });
            return list;
        }

        public async Task<IReadOnlyList<QuotationApprovalInfo>> ListAsync(int? managerUserId, bool pendingOnly, IReadOnlyCollection<int>? locationIds, CancellationToken ct = default)
        {
            var where = new List<string> { "q.ApprovalStatus IS NOT NULL" };
            if (pendingOnly) where.Add("q.ApprovalStatus = 'PendingManager'");
            if (managerUserId.HasValue) where.Add("q.ApprovalManagerUserId = @M");
            // location ids are ints from the caller's claims, never text
            if (locationIds != null) where.Add(locationIds.Count == 0 ? "1 = 0" : $"s.LocationId IN ({string.Join(",", locationIds)})");
            return await QueryAsync(" WHERE " + string.Join(" AND ", where) + " ORDER BY ISNULL(q.ApprovalSubmittedAt, q.CreatedDate) DESC",
                cmd => { if (managerUserId.HasValue) cmd.Parameters.Add("@M", SqlDbType.Int).Value = managerUserId.Value; }, ct);
        }

        public Task<IReadOnlyList<QuotationApprovalInfo>> GetDueRemindersAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
            QueryAsync(" WHERE q.ApprovalStatus = 'PendingManager' AND ISNULL(q.LastReminderAt, q.ApprovalSubmittedAt) <= @Cut ORDER BY q.ApprovalSubmittedAt",
                cmd => cmd.Parameters.Add("@Cut", SqlDbType.DateTime2).Value = cutoffUtc, ct);

        private async Task<IReadOnlyList<QuotationApprovalInfo>> QueryAsync(string tail, Action<SqlCommand> parameters, CancellationToken ct)
        {
            var list = new List<QuotationApprovalInfo>();
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(InfoSelect + tail, c) { CommandTimeout = CommandTimeoutSeconds };
            parameters(cmd);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) list.Add(MapInfo(r));
            return list;
        }

        public async Task MarkRemindedAsync(int quotationId, DateTime nowUtc, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand("UPDATE dbo.WN_Quotations SET LastReminderAt = @Now WHERE Id = @Id", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@Now", SqlDbType.DateTime2).Value = nowUtc;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = quotationId;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task CopyReasonToNewVersionAsync(int sourceQuotationId, int newQuotationId, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = new SqlCommand(@"UPDATE n SET n.DiscountReason = q.DiscountReason
                FROM dbo.WN_Quotations n JOIN dbo.WN_Quotations q ON q.Id = @Src WHERE n.Id = @New", c) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@Src", SqlDbType.Int).Value = sourceQuotationId;
            cmd.Parameters.Add("@New", SqlDbType.Int).Value = newQuotationId;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task UpdateDiscountAsync(QuotationDiscountUpdate u, CancellationToken ct = default)
        {
            await using var c = await OpenAsync(ct);
            await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
            await using (var cmd = new SqlCommand(@"UPDATE dbo.WN_Quotations SET DiscountType = @T, DiscountPercentage = @P, DiscountAmount = @A,
                    TotalAmount = @Total, SecurityDeposit = @Dep, UpdatedDate = SYSUTCDATETIME(), UpdatedById = @By WHERE Id = @Id", c, tx) { CommandTimeout = CommandTimeoutSeconds })
            {
                cmd.Parameters.Add("@T", SqlDbType.NVarChar, 20).Value = u.DiscountType;
                AddMoney(cmd, "@P", u.DiscountPercentage, 5, 2);
                AddMoney(cmd, "@A", u.DiscountAmount);
                AddMoney(cmd, "@Total", u.TotalAmount);
                AddMoney(cmd, "@Dep", u.SecurityDeposit);
                cmd.Parameters.Add("@By", SqlDbType.Int).Value = (object?)u.ByUserId ?? DBNull.Value;
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = u.QuotationId;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            // the deposit line of the quotation follows the deposit (a quotation without one has no such line)
            await using (var cmd = new SqlCommand(@"UPDATE dbo.WN_QuotationDetails SET Amount = @Dep, UnitPrice = ROUND(@Dep / NULLIF(Quantity, 0), 2)
                WHERE QuotationId = @Id AND FeeType = 'SecurityDeposit'", c, tx) { CommandTimeout = CommandTimeoutSeconds })
            {
                AddMoney(cmd, "@Dep", u.SecurityDeposit);
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = u.QuotationId;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            // the offer snapshot (OFFER_01) keeps its offer; only the manual part and the final figures change. Defensive before OFFER_01.
            if (u.Snapshot is { HasOffer: true })
                await OfferSnapshotStore.WriteAsync(c, OfferEntity.Quotation, u.QuotationId, u.Snapshot, ct);
        }

        private static void AddMoney(SqlCommand cmd, string name, decimal value, byte precision = 18, byte scale = 2)
        {
            var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
            p.Precision = precision; p.Scale = scale; p.Value = value;
        }
    }
}
