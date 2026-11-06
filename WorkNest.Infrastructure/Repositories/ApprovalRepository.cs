using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Approvals;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Approvals tables and procedures of script APPROVAL_01_approvals (dbo.WN_Approval_*). Raw SqlClient, one procedure
    /// per call. SQL error 208 / 2812 (table / procedure missing) = the script has not run: <see cref="ApprovalsMissingException"/>.
    /// </summary>
    public sealed class ApprovalRepository : IApprovalRepository
    {
        private const int CommandTimeoutSeconds = 15;
        private readonly string _connectionString;

        public ApprovalRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private async Task<T> Run<T>(string procedure, Func<SqlCommand, CancellationToken, Task<T>> read, CancellationToken ct,
            params (string Name, SqlDbType Type, object? Value)[] ps)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync(ct);
                await using var cmd = new SqlCommand(procedure, c) { CommandType = CommandType.StoredProcedure, CommandTimeout = CommandTimeoutSeconds };
                foreach (var (name, type, value) in ps) cmd.Parameters.Add(name, type).Value = value ?? DBNull.Value;
                return await read(cmd, ct);
            }
            catch (SqlException ex) when (ex.Number is 208 or 2812) { throw new ApprovalsMissingException(ex); }
        }

        private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
        private static DateTime? Utc(IDataRecord r, string col) => r[col] is DateTime d ? Utc(d) : null;
        private static string? Str(IDataRecord r, string col) => r[col] is string s ? s : null;
        private static int? Int(IDataRecord r, string col) => r[col] is int i ? i : null;

        private static async Task<List<T>> Rows<T>(SqlCommand cmd, Func<IDataRecord, T> map, CancellationToken ct)
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var list = new List<T>();
            while (await r.ReadAsync(ct)) list.Add(map(r));
            return list;
        }

        public async Task<ApprovalSettings?> GetSettingsAsync(CancellationToken ct = default) =>
            (await Run("dbo.WN_Approval_Settings_Get",
                (c, t) => Rows(c, r => new ApprovalSettings(Str(r, "ApproverEmail"), (int)r["LinkExpiryDays"], Utc((DateTime)r["UpdatedOn"]), Str(r, "UpdatedBy")), t), ct))
            .FirstOrDefault();

        public Task SaveSettingsAsync(string? approverEmail, string? updatedBy, CancellationToken ct = default) =>
            Run("dbo.WN_Approval_Settings_Save", async (c, t) => await c.ExecuteNonQueryAsync(t), ct,
                ("@ApproverEmail", SqlDbType.NVarChar, approverEmail ?? string.Empty), ("@UpdatedBy", SqlDbType.NVarChar, updatedBy));

        public async Task<IReadOnlyList<ApprovalCategory>> GetCategoriesAsync(bool activeOnly, CancellationToken ct = default) =>
            await Run("dbo.WN_Approval_Categories_Get",
                (c, t) => Rows(c, r => new ApprovalCategory((int)r["Id"], (string)r["Name"], (bool)r["IsActive"]), t), ct,
                ("@ActiveOnly", SqlDbType.Bit, activeOnly));

        public async Task<int> SaveCategoryAsync(int? id, string name, bool isActive, string? by, CancellationToken ct = default)
        {
            try
            {
                return (await Run("dbo.WN_Approval_Category_Save", (c, t) => Rows(c, r => (int)r["Id"], t), ct,
                    ("@Id", SqlDbType.Int, id), ("@Name", SqlDbType.NVarChar, name), ("@IsActive", SqlDbType.Bit, isActive), ("@By", SqlDbType.NVarChar, by))).First();
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw new ApprovalCategoryException("A category with this name already exists."); }
        }

        public async Task<IReadOnlyList<ApprovalAssignee>> GetAssigneesAsync(int? locationId, CancellationToken ct = default) =>
            await Run("dbo.WN_Approval_Assignees_Get",
                (c, t) => Rows(c, r => new ApprovalAssignee((int)r["Id"], (int)r["LocationId"], Str(r, "LocationName"), (int)r["CategoryId"], (string)r["CategoryName"],
                    (int)r["UserId"], Str(r, "UserName"), Str(r, "Email"), r["UserIsActive"] as bool?), t),
                ct, ("@LocationId", SqlDbType.Int, locationId));

        public Task AddAssigneeAsync(int locationId, int categoryId, int userId, string? by, CancellationToken ct = default) =>
            Run("dbo.WN_Approval_Assignee_Add", async (c, t) => await c.ExecuteNonQueryAsync(t), ct,
                ("@LocationId", SqlDbType.Int, locationId), ("@CategoryId", SqlDbType.Int, categoryId), ("@UserId", SqlDbType.Int, userId), ("@By", SqlDbType.NVarChar, by));

        public Task DeleteAssigneeAsync(int id, CancellationToken ct = default) =>
            Run("dbo.WN_Approval_Assignee_Delete", async (c, t) => await c.ExecuteNonQueryAsync(t), ct, ("@Id", SqlDbType.Int, id));

        public async Task<int> CreateRequestAsync(ApprovalNewRequest q, CancellationToken ct = default) =>
            (await Run("dbo.WN_Approval_Request_Create", (c, t) => Rows(c, r => (int)r["Id"], t), ct,
                ("@LocationId", SqlDbType.Int, q.LocationId), ("@CategoryId", SqlDbType.Int, q.CategoryId), ("@Title", SqlDbType.NVarChar, q.Title),
                ("@Details", SqlDbType.NVarChar, q.Details), ("@Reason", SqlDbType.NVarChar, q.Reason), ("@RequestedByUserId", SqlDbType.Int, q.RequestedByUserId),
                ("@ApproverEmail", SqlDbType.NVarChar, q.ApproverEmail), ("@TokenHash", SqlDbType.Binary, q.TokenHash),
                ("@TokenExpiresAt", SqlDbType.DateTime2, q.TokenExpiresAtUtc))).First();

        public Task<ApprovalRequestDetail?> GetRequestAsync(int id, CancellationToken ct = default) =>
            Run("dbo.WN_Approval_Request_Get", async (c, t) =>
            {
                await using var r = await c.ExecuteReaderAsync(t);
                if (!await r.ReadAsync(t)) return null;
                var head = new
                {
                    Id = (int)r["Id"], LocationId = (int)r["LocationId"], LocationName = Str(r, "LocationName"), CategoryId = (int)r["CategoryId"],
                    CategoryName = (string)r["CategoryName"], Title = (string)r["Title"], Details = (string)r["Details"], Reason = Str(r, "Reason"),
                    Status = (string)r["Status"], RequestedByUserId = (int)r["RequestedByUserId"], RequestedByName = Str(r, "RequestedByName"),
                    RequestedByEmail = Str(r, "RequestedByEmail"), RequestedAt = Utc((DateTime)r["RequestedAt"]), TokenExpiresAt = Utc((DateTime)r["TokenExpiresAt"]),
                    DecidedAt = Utc(r, "DecidedAt"), DecisionNote = Str(r, "DecisionNote"), DoneByUserId = Int(r, "DoneByUserId"), DoneByName = Str(r, "DoneByName"),
                    DoneAt = Utc(r, "DoneAt"), DoneNote = Str(r, "DoneNote")
                };
                var people = new List<ApprovalRequestAssignee>();
                if (await r.NextResultAsync(t))
                    while (await r.ReadAsync(t)) people.Add(new ApprovalRequestAssignee((int)r["UserId"], Str(r, "Name"), Str(r, "Email")));
                return (ApprovalRequestDetail?)new ApprovalRequestDetail(head.Id, head.LocationId, head.LocationName, head.CategoryId, head.CategoryName, head.Title,
                    head.Details, head.Reason, head.Status, head.RequestedByUserId, head.RequestedByName, head.RequestedByEmail, head.RequestedAt,
                    head.TokenExpiresAt, head.DecidedAt, head.DecisionNote, head.DoneByUserId, head.DoneByName, head.DoneAt, head.DoneNote, people);
            }, ct, ("@Id", SqlDbType.Int, id));

        public async Task<ApprovalRequestPage> GetRequestsAsync(IReadOnlyCollection<int>? locationIds, int? assigneeUserId, string? status, int? categoryId,
            int page, int pageSize, CancellationToken ct = default)
        {
            var rows = await Run("dbo.WN_Approval_Requests_Get",
                (c, t) => Rows(c, r => new ApprovalRequestRow((int)r["Id"], (int)r["LocationId"], Str(r, "LocationName"), (int)r["CategoryId"], (string)r["CategoryName"],
                    (string)r["Title"], (string)r["Status"], (int)r["RequestedByUserId"], Str(r, "RequestedByName"), Utc((DateTime)r["RequestedAt"]),
                    Utc(r, "DecidedAt"), Utc(r, "DoneAt"), (int)r["TotalCount"]), t),
                ct, ("@LocationIds", SqlDbType.NVarChar, locationIds == null ? null : string.Join(',', locationIds)), ("@AssigneeUserId", SqlDbType.Int, assigneeUserId),
                ("@Status", SqlDbType.VarChar, status), ("@CategoryId", SqlDbType.Int, categoryId), ("@Page", SqlDbType.Int, page), ("@PageSize", SqlDbType.Int, pageSize));
            return new ApprovalRequestPage(rows, rows.Count > 0 ? rows[0].TotalCount : 0);
        }

        public async Task<ApprovalPublicRequest?> GetRequestByTokenAsync(byte[] tokenHash, CancellationToken ct = default) =>
            (await Run("dbo.WN_Approval_Request_GetByToken",
                (c, t) => Rows(c, r => new ApprovalPublicRequest((int)r["Id"], Str(r, "LocationName"), (string)r["CategoryName"], (string)r["Title"], (string)r["Details"],
                    Str(r, "Reason"), (string)r["Status"], Str(r, "RequestedByName"), Utc((DateTime)r["RequestedAt"]), Utc((DateTime)r["TokenExpiresAt"]),
                    Utc(r, "DecidedAt"), Str(r, "DecisionNote")), t),
                ct, ("@TokenHash", SqlDbType.Binary, tokenHash))).FirstOrDefault();

        public async Task<ApprovalDecided?> DecideAsync(byte[] tokenHash, string decision, string? note, CancellationToken ct = default) =>
            (await Run("dbo.WN_Approval_Request_Decide", (c, t) => Rows(c, r => new ApprovalDecided((int)r["Id"], (string)r["Status"]), t), ct,
                ("@TokenHash", SqlDbType.Binary, tokenHash), ("@Decision", SqlDbType.VarChar, decision), ("@Note", SqlDbType.NVarChar, note))).FirstOrDefault();

        public async Task<bool> MarkDoneAsync(int id, int doneByUserId, string? note, CancellationToken ct = default) =>
            await Run("dbo.WN_Approval_Request_MarkDone", async (c, t) => Convert.ToInt32(await c.ExecuteScalarAsync(t)) > 0, ct,
                ("@Id", SqlDbType.Int, id), ("@DoneByUserId", SqlDbType.Int, doneByUserId), ("@Note", SqlDbType.NVarChar, note));

        public async Task<bool> ResendRequestAsync(int id, string approverEmail, byte[] tokenHash, DateTime tokenExpiresAtUtc, CancellationToken ct = default) =>
            await Run("dbo.WN_Approval_Request_Resend", async (c, t) => Convert.ToInt32(await c.ExecuteScalarAsync(t)) > 0, ct,
                ("@Id", SqlDbType.Int, id), ("@ApproverEmail", SqlDbType.NVarChar, approverEmail), ("@TokenHash", SqlDbType.Binary, tokenHash),
                ("@TokenExpiresAt", SqlDbType.DateTime2, tokenExpiresAtUtc));

        public async Task<IReadOnlyList<int>> ExpireDueAsync(CancellationToken ct = default) =>
            await Run("dbo.WN_Approval_Requests_ExpireDue", (c, t) => Rows(c, r => (int)r["Id"], t), ct);
    }
}
