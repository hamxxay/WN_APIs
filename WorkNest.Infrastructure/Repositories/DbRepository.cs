
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using WorkNest.Application.Interfaces;
using WorkNest.Application.DTOs.SpaceConfig;

namespace WorkNest.Infrastructure.Repositories
{
    public class DbRepository : IDbRepository
    {
        private readonly string _connectionString;

        public DbRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        private static object? N(object? v) => v is DBNull ? null : v;

        private static IDictionary<string, object?> ToDict(SqlDataReader r)
        {
            var d = new Dictionary<string, object?>(r.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = N(r.GetValue(i));
            return d;
        }

        private static async Task<List<IDictionary<string, object?>>> ReadAll(SqlDataReader r)
        {
            var list = new List<IDictionary<string, object?>>();
            while (await r.ReadAsync()) list.Add(ToDict(r));
            return list;
        }

        // Legacy aliases used by older code sections
        private static Task<List<IDictionary<string, object?>>> ReadAllRowsAsync(SqlDataReader r) => ReadAll(r);
        private static IDictionary<string, object?> RowToDictionary(SqlDataReader r) => ToDict(r);

        private SqlCommand SP(string name, SqlConnection c, SqlTransaction? tx = null)
        {
            var cmd = tx is null ? new SqlCommand(name, c) : new SqlCommand(name, c, tx);
            cmd.CommandType = CommandType.StoredProcedure;
            return cmd;
        }

        private async Task<SqlConnection> Open()
        {
            var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            return c;
        }

        // --- User ---

        public async Task<(int? Id, string? PublicId)> SyncUserAsync(string email, string? name, string? phone, string? passwordHash = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Users_GetByEmail", c);
            cmd.Parameters.AddWithValue("@Email", email);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                var id = row.TryGetValue("Id", out var eid) ? Convert.ToInt32(eid) : (int?)null;
                var pub = (row.TryGetValue("IdGUID", out var idg) && idg is not null) ? idg.ToString() : (row.TryGetValue("PublicId", out var eg) ? eg?.ToString() : null);
                await r.CloseAsync();
                await using var upd = SP("dbo.WN_Users_Update", c);
                upd.Parameters.AddWithValue("@Id", id);
                upd.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
                upd.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
                upd.Parameters.AddWithValue("@CompanyId", DBNull.Value);
                upd.Parameters.AddWithValue("@CityId", DBNull.Value);
                upd.Parameters.AddWithValue("@Address", DBNull.Value);
                upd.Parameters.AddWithValue("@CnicOrPassport", DBNull.Value);
                upd.Parameters.AddWithValue("@AvatarUrl", DBNull.Value);
                upd.Parameters.AddWithValue("@Notes", DBNull.Value);
                await upd.ExecuteNonQueryAsync();
                return (id, pub);
            }
            await r.CloseAsync();
            await using var ins = SP("dbo.WN_Users_Insert", c);
            ins.Parameters.AddWithValue("@Email", email);
            ins.Parameters.AddWithValue("@PasswordHash", (object?)passwordHash ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
            ins.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
            ins.Parameters.AddWithValue("@RoleId", DBNull.Value);
            ins.Parameters.AddWithValue("@CompanyId", DBNull.Value);
            ins.Parameters.AddWithValue("@CityId", DBNull.Value);
            ins.Parameters.AddWithValue("@Address", DBNull.Value);
            ins.Parameters.AddWithValue("@CnicOrPassport", DBNull.Value);
            ins.Parameters.AddWithValue("@AvatarUrl", DBNull.Value);
            ins.Parameters.AddWithValue("@Notes", DBNull.Value);
            ins.Parameters.AddWithValue("@CreatedById", DBNull.Value);
            await using var ir = await ins.ExecuteReaderAsync();
            if (await ir.ReadAsync())
            {
                var row = ToDict(ir);
                return (row.TryGetValue("Id", out var nid) ? Convert.ToInt32(nid) : (int?)null,
                        (row.TryGetValue("IdGUID", out var idg) && idg is not null) ? idg.ToString() : (row.TryGetValue("PublicId", out var ng) ? ng?.ToString() : null));
            }
            return (null, null);
        }
        public async Task<IDictionary<string, object?>> InsertQuotationAsync(
            string quotationNumber,
            DateTime validUntil,
            int? customerId,
            int? spaceId,
            DateTime startDateTime,
            DateTime endDateTime,
            decimal subtotalAmount,
            decimal discountPercentage,
            string? remarks = null,
            int? createdById = null,
            string discountType = "Percentage",
            decimal discountValue = 0,
            decimal? securityDepositOverride = null,
            int? floorId = null,
            int? billingPeriodMonths = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Quotations_Insert", c);

            cmd.Parameters.AddWithValue("@QuotationNumber", quotationNumber);
            cmd.Parameters.AddWithValue("@ValidUntil", validUntil);
            cmd.Parameters.AddWithValue("@CustomerId", (object?)customerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SpaceId", (object?)spaceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@StartDateTime", startDateTime);
            cmd.Parameters.AddWithValue("@EndDateTime", endDateTime);
            cmd.Parameters.AddWithValue("@SubtotalAmount", subtotalAmount);
            cmd.Parameters.AddWithValue("@DiscountPercentage", discountPercentage);
            cmd.Parameters.AddWithValue("@Remarks", (object?)remarks ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();
            IDictionary<string, object?> result = new Dictionary<string, object?>();
            if (await r.ReadAsync()) result = ToDict(r);
            await r.CloseAsync();

            // Persist new fields after insert
            if (result.TryGetValue("Id", out var qidObj) && qidObj is not null)
            {
                int quotationId = Convert.ToInt32(qidObj);

                decimal discountAmount = discountType == "Amount"
                    ? discountValue
                    : subtotalAmount * (discountValue / 100m);

                decimal secDeposit = securityDepositOverride ?? 0;

                var updateParts = new List<string> { "DiscountType = @DT", "SecurityDeposit = @SD" };
                if (floorId.HasValue) updateParts.Add("FloorId = @FID");

                var updateSql = $"UPDATE dbo.WN_Quotations SET {string.Join(", ", updateParts)} WHERE Id = @QID";
                await using var upd = new SqlCommand(updateSql, c);
                upd.Parameters.AddWithValue("@DT", discountType);
                upd.Parameters.AddWithValue("@SD", secDeposit);
                upd.Parameters.AddWithValue("@QID", quotationId);
                if (floorId.HasValue) upd.Parameters.AddWithValue("@FID", floorId.Value);
                await upd.ExecuteNonQueryAsync();

                result["DiscountType"] = discountType;
                result["SecurityDeposit"] = secDeposit;
            }

            return result;
        }
        public async Task<IEnumerable<IDictionary<string, object?>>> GetQuotationHistoryAsync(
            int quotationId,
            string? userEmail = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Quotations_GetHistory", c);
            cmd.Parameters.AddWithValue("@QuotationId", quotationId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetQuotationsByCustomerAsync(int customerId)
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand(
                "SELECT q.*, CONCAT(cu.FirstName, ' ', ISNULL(cu.LastName, '')) AS CustomerName, cu.Email AS CustomerEmail, cu.Company AS CustomerCompany, " +
                "s.Name AS SpaceName, s.Code AS SpaceCode, l.Name AS LocationName, st.Name AS SpaceTypeName " +
                "FROM dbo.WN_Quotations q " +
                "LEFT JOIN dbo.WN_Customers cu ON cu.Id = q.CustomerId " +
                "LEFT JOIN dbo.WN_Spaces s ON s.Id = q.SpaceId " +
                "LEFT JOIN dbo.WN_Locations l ON l.Id = s.LocationId " +
                "LEFT JOIN dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeId " +
                "WHERE q.CustomerId = @CustomerId " +
                "ORDER BY q.CreatedDate DESC", c);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IDictionary<string, object?>?> GetQuotationByIdAsync(
            int quotationId,
            string? userEmail = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Quotations_GetById", c);
            cmd.Parameters.AddWithValue("@Id", quotationId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetQuotationDetailsAsync(
            int quotationId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_QuotationDetails_GetByQuotation", c);
            cmd.Parameters.AddWithValue("@QuotationId", quotationId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetQuotationsAsync(
            int page,
            int limit,
            string? search)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Quotations_GetList", c);

            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();

            var rows = await ReadAll(r);
            int total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) ? Convert.ToInt32(t) : rows.Count;
            return (rows, total);
        }

        public async Task<IDictionary<string, object?>> ConvertQuotationToBookingAsync(
            int quotationId,
            int? createdById)
        {
            await using var c = await Open();
            await EnsureQuotationsConvertToBookingSpUpdatedAsync(c);
            await using var cmd = SP("dbo.WN_Quotations_ConvertToBooking", c);

            cmd.Parameters.AddWithValue("@QuotationId", quotationId);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();

            if (await r.ReadAsync())
                return ToDict(r);

            return new Dictionary<string, object?>();
        }

        private async Task EnsureQuotationTablesExistAsync(SqlConnection c)
        {
            try
            {
                string sql = @"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'WN_QuotationResponses')
BEGIN
    CREATE TABLE dbo.WN_QuotationResponses (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        IdGUID UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        QuotationId INT NOT NULL,
        Version INT NOT NULL DEFAULT 1,
        ResponseType NVARCHAR(20) NOT NULL,
        Note NVARCHAR(1000) NULL,
        RespondedByUserId INT NULL,
        RespondedByCustomerId INT NULL,
        RespondedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'WN_QuotationActivities')
BEGIN
    CREATE TABLE dbo.WN_QuotationActivities (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        IdGUID UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        QuotationId INT NOT NULL,
        Version INT NOT NULL DEFAULT 1,
        ActivityType NVARCHAR(50) NOT NULL,
        Message NVARCHAR(1000) NOT NULL,
        CustomerNote NVARCHAR(1000) NULL,
        CreatedByUserId INT NULL,
        CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
END";
                await using var cmd = new SqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        public async Task<IDictionary<string, object?>> AcceptQuotationAsync(int quotationId, int version, int customerId, string? note, int? userId)
        {
            await using var c = await Open();
            await EnsureQuotationTablesExistAsync(c);

            var q = await GetQuotationByIdAsync(quotationId);
            if (q == null) throw new InvalidOperationException("Quotation not found.");

            int ownerCustId = Convert.ToInt32(q["CustomerId"]);
            if (ownerCustId != customerId)
                throw new UnauthorizedAccessException("Unauthorized: Quotation does not belong to this customer.");

            string status = q["Status"]?.ToString() ?? "";
            if (!status.Equals("Sent", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Quotation is in '{status}' state and cannot be accepted (must be Sent).");

            string qNo = q["QuotationNumber"]?.ToString() ?? "";
            await ExecuteRawSqlAsync($"UPDATE dbo.WN_Quotations SET Status = 'Accepted', UpdatedDate = GETUTCDATE() WHERE Id = {quotationId}");

            string noteSql = string.IsNullOrWhiteSpace(note) ? "NULL" : $"'{note.Replace("'", "''")}'";
            string uIdSql = userId.HasValue ? userId.Value.ToString() : "NULL";
            await ExecuteRawSqlAsync($@"
INSERT INTO dbo.WN_QuotationResponses (QuotationId, Version, ResponseType, Note, RespondedByUserId, RespondedByCustomerId, RespondedDate)
VALUES ({quotationId}, {version}, 'Accepted', {noteSql}, {uIdSql}, {customerId}, GETUTCDATE())");

            string msg = $"Quotation {qNo} (Version {version}) was accepted by the customer.";
            await ExecuteRawSqlAsync($@"
INSERT INTO dbo.WN_QuotationActivities (QuotationId, Version, ActivityType, Message, CustomerNote, CreatedByUserId, CreatedDate)
VALUES ({quotationId}, {version}, 'Accepted', '{msg.Replace("'", "''")}', {noteSql}, {uIdSql}, GETUTCDATE())");

            return new Dictionary<string, object?>
            {
                { "QuotationId", quotationId },
                { "Version", version },
                { "Status", "Accepted" },
                { "Note", note }
            };
        }

        public async Task<IDictionary<string, object?>> DeclineQuotationAsync(int quotationId, int version, int customerId, string note, int? userId)
        {
            if (string.IsNullOrWhiteSpace(note))
                throw new ArgumentException("Decline reason note is mandatory.");

            await using var c = await Open();
            await EnsureQuotationTablesExistAsync(c);

            var q = await GetQuotationByIdAsync(quotationId);
            if (q == null) throw new InvalidOperationException("Quotation not found.");

            int ownerCustId = Convert.ToInt32(q["CustomerId"]);
            if (ownerCustId != customerId)
                throw new UnauthorizedAccessException("Unauthorized: Quotation does not belong to this customer.");

            string status = q["Status"]?.ToString() ?? "";
            if (!status.Equals("Sent", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Quotation is in '{status}' state and cannot be declined (must be Sent).");

            string qNo = q["QuotationNumber"]?.ToString() ?? "";
            await ExecuteRawSqlAsync($"UPDATE dbo.WN_Quotations SET Status = 'Declined', UpdatedDate = GETUTCDATE() WHERE Id = {quotationId}");

            string noteSql = $"'{note.Replace("'", "''")}'";
            string uIdSql = userId.HasValue ? userId.Value.ToString() : "NULL";
            await ExecuteRawSqlAsync($@"
INSERT INTO dbo.WN_QuotationResponses (QuotationId, Version, ResponseType, Note, RespondedByUserId, RespondedByCustomerId, RespondedDate)
VALUES ({quotationId}, {version}, 'Declined', {noteSql}, {uIdSql}, {customerId}, GETUTCDATE())");

            string msg = $"Quotation {qNo} (Version {version}) was declined by the customer.";
            await ExecuteRawSqlAsync($@"
INSERT INTO dbo.WN_QuotationActivities (QuotationId, Version, ActivityType, Message, CustomerNote, CreatedByUserId, CreatedDate)
VALUES ({quotationId}, {version}, 'Declined', '{msg.Replace("'", "''")}', {noteSql}, {uIdSql}, GETUTCDATE())");

            return new Dictionary<string, object?>
            {
                { "QuotationId", quotationId },
                { "Version", version },
                { "Status", "Declined" },
                { "Note", note }
            };
        }

        public async Task<IDictionary<string, object?>> CreateQuotationNewVersionAsync(int quotationId, int? createdById)
        {
            await using var c = await Open();
            await EnsureQuotationTablesExistAsync(c);

            var source = await GetQuotationByIdAsync(quotationId);
            if (source == null) throw new InvalidOperationException("Source quotation not found.");

            string qNo = source["QuotationNumber"]?.ToString() ?? "";
            int srcVersion = Convert.ToInt32(source["Version"]);

            var allRows = await GetQuotationsByCustomerAsync(Convert.ToInt32(source["CustomerId"]));
            int maxVersion = allRows.Where(r => r["QuotationNumber"]?.ToString() == qNo)
                                   .Select(r => Convert.ToInt32(r["Version"]))
                                   .DefaultIfEmpty(srcVersion)
                                   .Max();
            int newVersion = maxVersion + 1;

            string createdBySql = createdById.HasValue ? createdById.Value.ToString() : "NULL";
            string validUntilSql = Convert.ToDateTime(source["ValidUntil"]).ToString("yyyy-MM-dd HH:mm:ss");
            string startSql = Convert.ToDateTime(source["StartDateTime"]).ToString("yyyy-MM-dd HH:mm:ss");
            string endSql = Convert.ToDateTime(source["EndDateTime"]).ToString("yyyy-MM-dd HH:mm:ss");

            string insertSql = $@"
INSERT INTO dbo.WN_Quotations (
    QuotationNumber, ValidUntil, CustomerId, SpaceId, StartDateTime, EndDateTime,
    SubtotalAmount, DiscountPercentage, DiscountAmount, TotalAmount, Remarks, Status,
    Version, IsActive, CreatedById, DiscountType, SecurityDeposit, FloorId, BillingPeriodMonths
)
VALUES (
    '{qNo}', '{validUntilSql}', {source["CustomerId"]}, {source["SpaceId"]}, '{startSql}', '{endSql}',
    {source["SubtotalAmount"]}, {source["DiscountPercentage"]}, {source["DiscountAmount"]}, {source["TotalAmount"]},
    {(source["Remarks"] != null ? $"'{source["Remarks"]!.ToString()!.Replace("'", "''")}'" : "NULL")}, 'Draft',
    {newVersion}, 1, {createdBySql}, '{(source.TryGetValue("DiscountType", out var dt) ? dt : "Percentage")}',
    {(source.TryGetValue("SecurityDeposit", out var sd) ? sd : 0)}, {(source.TryGetValue("FloorId", out var fid) && fid != null ? fid : "NULL")},
    {(source.TryGetValue("BillingPeriodMonths", out var bpm) && bpm != null ? bpm : 3)}
);
SELECT SCOPE_IDENTITY() AS NewId;";

            await using var cmd = new SqlCommand(insertSql, c);
            var newIdObj = await cmd.ExecuteScalarAsync();
            int newQuotationId = Convert.ToInt32(newIdObj);

            var details = await GetQuotationDetailsAsync(quotationId);
            foreach (var d in details)
            {
                string desc = d["Description"]?.ToString()?.Replace("'", "''") ?? "";
                string detSql = $@"
INSERT INTO dbo.WN_QuotationDetails (QuotationId, FeeType, Description, Quantity, UnitPrice, Amount, CreatedById)
VALUES ({newQuotationId}, '{d["FeeType"]}', '{desc}', {d["Quantity"]}, {d["UnitPrice"]}, {d["Amount"]}, {createdBySql})";
                await ExecuteRawSqlAsync(detSql);
            }

            string msg = $"Version {newVersion} created from Version {srcVersion}.";
            await ExecuteRawSqlAsync($@"
INSERT INTO dbo.WN_QuotationActivities (QuotationId, Version, ActivityType, Message, CreatedByUserId, CreatedDate)
VALUES ({newQuotationId}, {newVersion}, 'VersionCreated', '{msg.Replace("'", "''")}', {createdBySql}, GETUTCDATE())");

            return new Dictionary<string, object?>
            {
                { "NewQuotationId", newQuotationId },
                { "NewVersion", newVersion },
                { "QuotationNumber", qNo },
                { "Status", "Draft" }
            };
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetQuotationVersionsAsync(int quotationId)
        {
            await using var c = await Open();
            var q = await GetQuotationByIdAsync(quotationId);
            if (q == null) return new List<IDictionary<string, object?>>();

            string qNo = q["QuotationNumber"]?.ToString() ?? "";
            var rows = await GetQuotationsByCustomerAsync(Convert.ToInt32(q["CustomerId"]));
            return rows.Where(r => r["QuotationNumber"]?.ToString() == qNo)
                       .OrderBy(r => Convert.ToInt32(r["Version"]))
                       .ToList();
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetQuotationActivitiesAsync(int? quotationId, int limit)
        {
            await using var c = await Open();
            await EnsureQuotationTablesExistAsync(c);

            string whereClause = quotationId.HasValue ? $"WHERE QuotationId = {quotationId.Value}" : "";
            string sql = $"SELECT TOP ({limit}) * FROM dbo.WN_QuotationActivities {whereClause} ORDER BY CreatedDate DESC";
            await using var cmd = new SqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task SendQuotationStatusAsync(int quotationId, string status, int? userId)
        {
            await using var c = await Open();
            await EnsureQuotationTablesExistAsync(c);

            var q = await GetQuotationByIdAsync(quotationId);
            if (q == null) throw new InvalidOperationException("Quotation not found.");

            string qNo = q["QuotationNumber"]?.ToString() ?? "";
            int ver = Convert.ToInt32(q["Version"]);

            await ExecuteRawSqlAsync($"UPDATE dbo.WN_Quotations SET Status = '{status}', UpdatedDate = GETUTCDATE() WHERE Id = {quotationId}");

            string uIdSql = userId.HasValue ? userId.Value.ToString() : "NULL";
            string msg = $"Quotation {qNo} (Version {ver}) status changed to {status}.";
            await ExecuteRawSqlAsync($@"
INSERT INTO dbo.WN_QuotationActivities (QuotationId, Version, ActivityType, Message, CreatedByUserId, CreatedDate)
VALUES ({quotationId}, {ver}, 'Sent', '{msg.Replace("'", "''")}', {uIdSql}, GETUTCDATE())");
        }

        public async Task<(int? Id, string? PublicId)> GetUserIdByEmailAsync(string email)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Users_GetByEmail", c);
            cmd.Parameters.AddWithValue("@Email", email);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return (row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null,
                        (row.TryGetValue("IdGUID", out var idg) && idg is not null) ? idg.ToString() : (row.TryGetValue("PublicId", out var g) ? g?.ToString() : null));
            }
            return (null, null);
        }

        public async Task<IDictionary<string, object?>?> GetUserByEmailAsync(string email)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Users_GetByEmail", c);
            cmd.Parameters.AddWithValue("@Email", email);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<IDictionary<string, object?>?> GetUserByIdAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Users_GetById", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<IDictionary<string, object?>?> GetUserByPublicIdAsync(Guid publicId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Users_GetByPublicId", c);
            cmd.Parameters.AddWithValue("@PublicId", publicId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetUsersAsync(int page, int limit, string? search)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Users_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            int total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) ? Convert.ToInt32(t) : rows.Count;
            return (rows, total);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetUserHistoryAsync(int userId)
        {
            await using var c = await Open();
            string sql = @"
                SELECT
                    b.Id            AS BookingId,
                    b.IdGUID        AS BookingPublicId,
                    s.Name          AS SpaceName,
                    s.Code          AS SpaceCode,
                    ISNULL(st.Name, st.Description) AS SpaceTypeName,
                    b.StartOn, b.EndOn,
                    bs.Label        AS BookingStatus,
                    bp.Label        AS BillingPeriod,
                    ISNULL(b.SubtotalAmount, b.TotalAmount) AS RoomPrice,
                    b.CreatedOn     AS BookedOn
                FROM dbo.WN_Bookings        b  WITH (NOLOCK)
                LEFT JOIN dbo.WN_Spaces          s  WITH (NOLOCK) ON s.Id  = b.SpaceId
                LEFT JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
                LEFT JOIN dbo.WN_SpacePricing    sp WITH (NOLOCK) ON sp.Id = b.PricingId
                LEFT JOIN dbo.WN_BillingPeriods  bp WITH (NOLOCK) ON bp.Id = sp.BillingPeriodId
                LEFT JOIN dbo.WN_BookingStatuses bs WITH (NOLOCK) ON bs.Id = b.BookingStatusId
                WHERE b.UserId    = @UserId
                  AND b.IsDeleted = 0
                ORDER BY b.CreatedOn DESC;";

            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@UserId", userId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<(int? Id, string? PublicId)> CreateUserAsync(string email, string? passwordHash, string? name, string? phone, int? roleId, int? companyId, int? cityId, string? address, string? cnic, string? avatarUrl, string? notes, int? createdById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Users_Insert", c);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@PasswordHash", (object?)passwordHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RoleId", (object?)roleId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CompanyId", (object?)companyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CityId", (object?)cityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CnicOrPassport", (object?)cnic ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AvatarUrl", (object?)avatarUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return (row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null,
                        (row.TryGetValue("IdGUID", out var idg) && idg is not null) ? idg.ToString() : (row.TryGetValue("PublicId", out var g) ? g?.ToString() : null));
            }
            return (null, null);
        }

        public async Task UpdateUserAsync(int id, string? name, string? phone, int? companyId, int? cityId, string? address, string? cnic, string? avatarUrl, string? notes)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Users_Update", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CompanyId", (object?)companyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CityId", (object?)cityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CnicOrPassport", (object?)cnic ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AvatarUrl", (object?)avatarUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteUserAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Users_Delete", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetUserStatusAsync(int id, bool isActive)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Users_SetStatus", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@IsActive", isActive ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetUserRoleAsync(int id, int roleId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Users_SetRole", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@RoleId", roleId);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- Space ---

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetSpacesAsync(int page, int limit, string? search)
        {
            await using var c = await Open();
            await EnsureSpacesGetListSpUpdatedAsync(c);
            await using var cmd = SP("dbo.WN_Spaces_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationId", DBNull.Value);
            cmd.Parameters.AddWithValue("@SpaceTypeId", DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            int total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) ? Convert.ToInt32(t) : rows.Count;
            return (rows, total);
        }

        public async Task<IDictionary<string, object?>?> GetSpaceSummaryAsync(int id)
        {
        // --- Space ---
            await using var c = await Open();
            await EnsureSpacesGetListSpUpdatedAsync(c);
            await using var cmd = SP("dbo.WN_Spaces_GetList", c);
            cmd.Parameters.AddWithValue("@Page", 1);
            cmd.Parameters.AddWithValue("@Limit", 10000);
            cmd.Parameters.AddWithValue("@Search", DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationId", DBNull.Value);
            cmd.Parameters.AddWithValue("@SpaceTypeId", DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            return rows.FirstOrDefault(row => row.TryGetValue("Id", out var rid) && Convert.ToInt32(rid) == id);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Spaces_GetAvailable", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesByTypeAsync(int spaceTypeId, DateTime startOn, DateTime endOn)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_GetAvailableSpaces", c);
            cmd.Parameters.AddWithValue("@SpaceTypeId", spaceTypeId);
            cmd.Parameters.AddWithValue("@StartOn", startOn);
            cmd.Parameters.AddWithValue("@EndOn", endOn);
            cmd.Parameters.AddWithValue("@Capacity", DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailabilityCountsAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Spaces_GetAvailabilityCounts", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<(int? Id, string? PublicId)> InsertSpaceAsync(string name, int locationId, int spaceTypeId, string? code, string? description, int? floorId, string? imageUrl, int capacity, int? createdById, decimal? price = null, byte? billingPeriodId = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Spaces_Insert", c);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Code", (object?)code ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationId", locationId);
            cmd.Parameters.AddWithValue("@FloorId", (object?)floorId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SpaceTypeId", spaceTypeId);
            cmd.Parameters.AddWithValue("@Capacity", (short)capacity);
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)imageUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Price", (object?)price ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BillingPeriodId", (object?)billingPeriodId ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return (row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null,
                        row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
            }
            return (null, null);
        }

        public async Task UpdateSpaceAsync(int id, string? name, int? locationId, int? spaceTypeId, string? code, string? description, int? floorId, string? imageUrl, int? capacity, int? updatedById, decimal? price = null, byte? billingPeriodId = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Spaces_Update", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FloorId", (object?)floorId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SpaceTypeId", (object?)spaceTypeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Capacity", capacity.HasValue ? (object)(short)capacity.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)imageUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Price", (object?)price ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BillingPeriodId", (object?)billingPeriodId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetBillingPeriodsAsync()
        {
            await using var c = await Open();
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT Id, Code, Label, DurationDays, SortOrder, IsActive FROM dbo.WN_BillingPeriods WHERE IsActive = 1 ORDER BY SortOrder, Id", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task DeleteSpaceAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Spaces_Delete", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- Booking ---

        private static bool _bookingColumnsChecked = false;
        private async Task EnsureBookingColumnsAndBackfillAsync(SqlConnection c)
        {
            if (_bookingColumnsChecked) return;
            try
            {
                string sql = @"
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'WN_Bookings')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Bookings') AND name = 'AdvanceRentMonths')
        ALTER TABLE dbo.WN_Bookings ADD AdvanceRentMonths INT NOT NULL DEFAULT 1;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Bookings') AND name = 'SecurityDepositMonths')
        ALTER TABLE dbo.WN_Bookings ADD SecurityDepositMonths INT NOT NULL DEFAULT 0;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Bookings') AND name = 'SecurityDepositRequired')
        ALTER TABLE dbo.WN_Bookings ADD SecurityDepositRequired DECIMAL(18,2) NOT NULL DEFAULT 0.00;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Bookings') AND name = 'SecurityDepositPaid')
        ALTER TABLE dbo.WN_Bookings ADD SecurityDepositPaid DECIMAL(18,2) NOT NULL DEFAULT 0.00;

    UPDATE dbo.WN_Bookings 
    SET 
        AdvanceRentMonths = CASE WHEN ISNULL(AdvanceRentMonths, 0) <= 0 THEN ISNULL(BillingPeriodMonths, 1) ELSE AdvanceRentMonths END,
        SecurityDepositMonths = CASE WHEN ISNULL(SecurityDepositMonths, 0) <= 0 AND ISNULL(SecurityDepositOverride, 0) > 0 THEN 2 ELSE ISNULL(SecurityDepositMonths, 0) END,
        SecurityDepositRequired = CASE WHEN ISNULL(SecurityDepositRequired, 0) <= 0 THEN ISNULL(SecurityDepositOverride, ISNULL(SecurityDepositMonths, 0) * ISNULL(MonthlyRent, SubtotalAmount / NULLIF(AdvanceRentMonths, 1))) ELSE SecurityDepositRequired END,
        SecurityDepositPaid = ISNULL(SecurityDepositPaid, 0.00)
    WHERE ISNULL(AdvanceRentMonths, 0) <= 0 OR ISNULL(SecurityDepositRequired, 0) <= 0 OR SecurityDepositPaid IS NULL;

    -- Backfill/fix any existing bookings and corresponding invoices where SecurityDepositMonths > 1
    -- but security deposit was recorded as only 1 month
    UPDATE b
    SET b.SecurityDepositRequired = b.SecurityDepositMonths * b.MonthlyRent,
        b.TotalAmount = b.SubtotalAmount - b.DiscountAmount + ISNULL(i.TaxTotal, 0) + (b.SecurityDepositMonths * b.MonthlyRent)
    FROM dbo.WN_Bookings b
    LEFT JOIN dbo.WN_Invoices i ON i.BookingId = b.Id
    WHERE b.SecurityDepositMonths > 1
      AND b.MonthlyRent > 0
      AND b.SecurityDepositOverride IS NULL
      AND (b.SecurityDepositRequired < (b.SecurityDepositMonths * b.MonthlyRent) OR b.SecurityDepositRequired = b.MonthlyRent);

    UPDATE i
    SET i.SecurityDepositMonths = b.SecurityDepositMonths,
        i.SecurityDepositAmount = b.SecurityDepositRequired,
        i.GrandTotal = i.SubTotal - i.DiscountTotal + i.TaxTotal + b.SecurityDepositRequired
    FROM dbo.WN_Invoices i
    JOIN dbo.WN_Bookings b ON b.Id = i.BookingId
    WHERE b.SecurityDepositMonths > 1
      AND b.SecurityDepositRequired > 0
      AND (i.SecurityDepositAmount < b.SecurityDepositRequired OR i.SecurityDepositMonths <> b.SecurityDepositMonths);

    UPDATE il
    SET il.Quantity = b.SecurityDepositMonths,
        il.UnitPrice = b.MonthlyRent,
        il.Description = 'Security Deposit (' + CAST(b.SecurityDepositMonths AS NVARCHAR(5)) + ' Month(s) Refundable)'
    FROM dbo.WN_InvoiceLines il
    JOIN dbo.WN_Invoices i ON i.Id = il.InvoiceId
    JOIN dbo.WN_Bookings b ON b.Id = i.BookingId
    WHERE il.ChargeTypeId = 2
      AND b.SecurityDepositMonths > 1
      AND b.MonthlyRent > 0
      AND (il.Quantity < b.SecurityDepositMonths OR (il.Quantity * il.UnitPrice) < b.SecurityDepositRequired);

    UPDATE bd
    SET bd.SecurityDeposit = b.SecurityDepositRequired
    FROM dbo.WN_BookingDetails bd
    JOIN dbo.WN_Bookings b ON b.IdGUID = bd.BookingGuid
    WHERE b.SecurityDepositMonths > 1
      AND b.SecurityDepositRequired > 0
      AND bd.SecurityDeposit < b.SecurityDepositRequired;

    UPDATE p
    SET p.Amount = b.TotalAmount
    FROM dbo.WN_Payments p
    JOIN dbo.WN_Bookings b ON b.Id = p.BookingIdInt OR b.IdGUID = p.BookingId
    WHERE b.SecurityDepositMonths > 1
      AND b.TotalAmount > p.Amount
      AND (p.PaymentStatus = 'Pending' OR p.StatusId = 1);
END";
                await using var cmd = new SqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync();
                await EnsureBookingsInsertSpUpdatedAsync(c);
                await EnsureBookingSummaryViewUpdatedAsync(c);
                await EnsureInvoiceProceduresUpdatedAsync(c);
                _bookingColumnsChecked = true;
            }
            catch { }
        }

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetBookingsAsync(int page, int limit, string? search)
        {
            await using var c = await Open();
            await EnsureBookingColumnsAndBackfillAsync(c);
            await using var cmd = SP("dbo.WN_Bookings_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            int total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) ? Convert.ToInt32(t) : rows.Count;
            return (rows, total);
        }

        public async Task<IDictionary<string, object?>?> GetBookingByPublicIdAsync(Guid publicId, string? userEmail = null)
        {
            await using var c = await Open();
            await EnsureBookingColumnsAndBackfillAsync(c);
            await using var cmd = SP("dbo.WN_Bookings_GetByPublicId", c);
            cmd.Parameters.AddWithValue("@PublicId", publicId);
            cmd.Parameters.AddWithValue("@UserEmail", (object?)userEmail ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetMyBookingsAsync(string userEmail)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_GetMyList", c);
            cmd.Parameters.AddWithValue("@UserEmail", userEmail);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetRecentBookingsAsync(int top = 10)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_GetRecent", c);
            cmd.Parameters.AddWithValue("@Top", top);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetBookingCalendarAsync(int spaceId, int year, int month)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_GetCalendar", c);
            cmd.Parameters.AddWithValue("@SpaceId", spaceId);
            cmd.Parameters.AddWithValue("@Year", year);
            cmd.Parameters.AddWithValue("@Month", month);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesForBookingAsync(int spaceTypeId, DateTime startOn, DateTime endOn, int? capacity = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_GetAvailableSpaces", c);
            cmd.Parameters.AddWithValue("@SpaceTypeId", spaceTypeId);
            cmd.Parameters.AddWithValue("@StartOn", startOn);
            cmd.Parameters.AddWithValue("@EndOn", endOn);
            cmd.Parameters.AddWithValue("@Capacity", (object?)capacity ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesForReassignmentAsync(int spaceTypeId, DateTime startOn, DateTime endOn, int excludeBookingId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_GetAvailableSpacesForReassignment", c);
            cmd.Parameters.AddWithValue("@SpaceTypeId", spaceTypeId);
            cmd.Parameters.AddWithValue("@StartOn", startOn);
            cmd.Parameters.AddWithValue("@EndOn", endOn);
            cmd.Parameters.AddWithValue("@ExcludeBookingId", excludeBookingId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetSmartAvailableSpacesAsync(string categoryCode, DateTime startOn, DateTime endOn, int? capacity = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_GetSmartAvailable", c);
            cmd.Parameters.AddWithValue("@CategoryCode", categoryCode);
            cmd.Parameters.AddWithValue("@StartOn", startOn);
            cmd.Parameters.AddWithValue("@EndOn", endOn);
            cmd.Parameters.AddWithValue("@Capacity", (object?)capacity ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IDictionary<string, object?>> InsertBookingAsync(
            int userId, int spaceId, int pricingId, DateTime startOn, DateTime endOn, string? notes, int? createdById, string? userEmail,
            string? customerEmail = null, string? customerFirstName = null, string? customerLastName = null, string? customerPhone = null,
            string? customerCnic = null, string? customerAddress = null, int? customerCityId = null, string? customerNotes = null,
            decimal discountPercentage = 0, string discountType = "Percentage", decimal discountValue = 0,
            decimal? securityDepositOverride = null, int? floorId = null, int? billingPeriodMonths = null, int? securityDepositMonths = null, int? advanceRentMonths = null)
        {
            await using var c = await Open();
            await EnsureBookingColumnsAndBackfillAsync(c);
            await using var cmd = SP("dbo.WN_Bookings_Insert", c);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@SpaceId", spaceId);
            cmd.Parameters.AddWithValue("@PricingId", pricingId);
            cmd.Parameters.AddWithValue("@StartOn", startOn);
            cmd.Parameters.AddWithValue("@EndOn", endOn);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UserEmail", (object?)userEmail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerEmail", (object?)customerEmail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerFirstName", (object?)customerFirstName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerLastName", (object?)customerLastName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerPhone", (object?)customerPhone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerCnic", (object?)customerCnic ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerAddress", (object?)customerAddress ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerCityId", (object?)customerCityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerNotes", (object?)customerNotes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DiscountPercentage", discountPercentage);
            cmd.Parameters.AddWithValue("@BillingPeriodMonths", (object?)billingPeriodMonths ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SecurityDepositMonths", (object?)securityDepositMonths ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AdvanceRentMonths", (object?)advanceRentMonths ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            IDictionary<string, object?> result = new Dictionary<string, object?>();
            if (await r.ReadAsync()) result = ToDict(r);
            await r.CloseAsync();

            // Persist new fields (DiscountType, DiscountAmount, SecurityDepositOverride, FloorId) if booking was created
            if (result.TryGetValue("BookingId", out var bidObj) && bidObj is not null)
            {
                int bookingId = Convert.ToInt32(bidObj);

                // Calculate discount amount
                decimal discountAmount = 0;
                if (discountType == "Amount")
                    discountAmount = discountValue;
                else if (discountType == "Percentage" && discountValue > 0)
                {
                    // Subtotal is in the result or we use discountPercentage
                    decimal subtotal = result.TryGetValue("SubtotalAmount", out var sa) && sa is not null ? Convert.ToDecimal(sa) : 0;
                    discountAmount = subtotal * (discountValue / 100m);
                }

                int advRentM = advanceRentMonths ?? billingPeriodMonths ?? 1;
                int secDepM = securityDepositMonths ?? 0;
                decimal monthlyRent = result.TryGetValue("MonthlyRent", out var mrObj) && mrObj is not null ? Convert.ToDecimal(mrObj) : 0m;
                if (monthlyRent == 0m && result.TryGetValue("SubtotalAmount", out var subObj) && subObj is not null)
                {
                    monthlyRent = Convert.ToDecimal(subObj) / Math.Max(1, advRentM);
                }
                decimal secDepReq = securityDepositOverride ?? (secDepM * monthlyRent);

                var updateParts = new List<string> {
                    "DiscountType = @DT",
                    "DiscountAmount = @DA",
                    "AdvanceRentMonths = @ARM",
                    "SecurityDepositMonths = @SDM",
                    "SecurityDepositRequired = @SDR",
                    "SecurityDepositPaid = ISNULL(SecurityDepositPaid, 0.00)"
                };
                if (billingPeriodMonths.HasValue) updateParts.Add("BillingPeriodMonths = @BPM");
                if (securityDepositOverride.HasValue) updateParts.Add("SecurityDepositOverride = @SDO");
                if (floorId.HasValue) updateParts.Add("FloorId = @FID");

                var updateSql = $@"
                    UPDATE dbo.WN_Bookings 
                    SET {string.Join(", ", updateParts)},
                        TotalAmount = CASE WHEN @SDR > 0 THEN SubtotalAmount - @DA + ISNULL((SELECT TOP 1 TaxTotal FROM dbo.WN_Invoices WHERE BookingId = @BID), 0) + @SDR ELSE TotalAmount END
                    WHERE Id = @BID;

                    UPDATE dbo.WN_BookingDetails
                    SET SecurityDeposit = @SDR
                    WHERE BookingGuid = (SELECT IdGUID FROM dbo.WN_Bookings WHERE Id = @BID);

                    UPDATE dbo.WN_BookingLines
                    SET Quantity = CASE WHEN @SDM > 0 THEN @SDM ELSE 1 END,
                        UnitPrice = CASE WHEN @SDM > 0 THEN @SDR / @SDM ELSE @SDR END
                    WHERE BookingId = @BID AND ChargeTypeId = 2;

                    UPDATE dbo.WN_Invoices
                    SET SecurityDepositAmount = @SDR,
                        SecurityDepositMonths = @SDM,
                        GrandTotal = SubTotal - DiscountTotal + TaxTotal + @SDR
                    WHERE BookingId = @BID;

                    UPDATE dbo.WN_InvoiceLines
                    SET Quantity = CASE WHEN @SDM > 0 THEN @SDM ELSE 1 END,
                        UnitPrice = CASE WHEN @SDM > 0 THEN @SDR / @SDM ELSE @SDR END,
                        Description = CASE WHEN @SDM > 1 THEN 'Security Deposit (' + CAST(@SDM AS NVARCHAR(5)) + ' Month(s) Refundable)' ELSE 'Security Deposit (Refundable)' END
                    WHERE InvoiceId IN (SELECT Id FROM dbo.WN_Invoices WHERE BookingId = @BID) AND ChargeTypeId = 2;
                ";
                await using var upd = new SqlCommand(updateSql, c);
                upd.Parameters.AddWithValue("@DT", discountType);
                upd.Parameters.AddWithValue("@DA", discountAmount);
                upd.Parameters.AddWithValue("@ARM", advRentM);
                upd.Parameters.AddWithValue("@SDM", secDepM);
                upd.Parameters.AddWithValue("@SDR", secDepReq);
                upd.Parameters.AddWithValue("@BID", bookingId);
                if (billingPeriodMonths.HasValue) upd.Parameters.AddWithValue("@BPM", billingPeriodMonths.Value);
                if (securityDepositOverride.HasValue) upd.Parameters.AddWithValue("@SDO", securityDepositOverride.Value);
                if (floorId.HasValue) upd.Parameters.AddWithValue("@FID", floorId.Value);
                await upd.ExecuteNonQueryAsync();

                result["DiscountType"] = discountType;
                result["DiscountAmount"] = discountAmount;
                result["AdvanceRentMonths"] = advRentM;
                result["SecurityDepositMonths"] = secDepM;
                result["SecurityDepositRequired"] = secDepReq;
                result["SecurityDepositPaid"] = 0.00m;
                if (result.TryGetValue("TotalAmount", out var curTotalObj) && curTotalObj is not null)
                {
                    decimal sub = result.TryGetValue("SubtotalAmount", out var saVal) && saVal is not null ? Convert.ToDecimal(saVal) : 0m;
                    decimal tax = result.TryGetValue("TaxAmount", out var taVal) && taVal is not null ? Convert.ToDecimal(taVal) : 0m;
                    result["TotalAmount"] = (sub - discountAmount) + tax + secDepReq;
                }

                // Ensure WN_Payments entry is created for this booking cycle
                try
                {
                    var challanNum = result.TryGetValue("ChallanNumber", out var cn) ? cn?.ToString() : null;
                    var validity = result.TryGetValue("ChallanValidUntil", out var vu) && vu is not null ? Convert.ToDateTime(vu) : DateTime.UtcNow.AddDays(5);

                    var amtSql = "SELECT TotalAmount, IdGUID, UserGuid FROM dbo.WN_Bookings WHERE Id = @BID";
                    await using var amtCmd = new SqlCommand(amtSql, c);
                    amtCmd.Parameters.AddWithValue("@BID", bookingId);
                    await using var amtReader = await amtCmd.ExecuteReaderAsync();

                    decimal totalAmt = 0;
                    Guid? bookingGuid = null;
                    Guid? userGuid = null;
                    if (await amtReader.ReadAsync())
                    {
                        totalAmt = amtReader.IsDBNull(0) ? 0 : amtReader.GetDecimal(0);
                        bookingGuid = amtReader.IsDBNull(1) ? null : amtReader.GetGuid(1);
                        userGuid = amtReader.IsDBNull(2) ? null : amtReader.GetGuid(2);
                    }
                    await amtReader.CloseAsync();

                    var checkPaymentSql = "SELECT COUNT(1) FROM dbo.WN_Payments WHERE (BookingIdInt = @BID OR TransactionRef = @Ref)";
                    await using var checkCmd = new SqlCommand(checkPaymentSql, c);
                    checkCmd.Parameters.AddWithValue("@BID", bookingId);
                    checkCmd.Parameters.AddWithValue("@Ref", (object?)challanNum ?? DBNull.Value);
                    var existingCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    if (existingCount == 0 && totalAmt > 0)
                    {
                        var invSql = "SELECT TOP 1 Id FROM dbo.WN_Invoices WHERE BookingId = @BID ORDER BY Id DESC";
                        await using var invCmd = new SqlCommand(invSql, c);
                        invCmd.Parameters.AddWithValue("@BID", bookingId);
                        var invIdObj = await invCmd.ExecuteScalarAsync();
                        int? invoiceId = invIdObj != null && invIdObj != DBNull.Value ? Convert.ToInt32(invIdObj) : null;

                        var paySql = @"
                            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Payments') AND name = 'UserId' AND system_type_id = 56)
                            BEGIN
                                INSERT INTO dbo.WN_Payments (
                                    UserId, BookingId, InvoiceId, Amount, Currency, CurrencyCode,
                                    PaymentMethod, PaymentMethodId, PaymentStatus, StatusId,
                                    TransactionRef, GatewayRef, ExpiresOn, ExpiryDate, CreatedById, CreatedAt, CreatedOn
                                ) VALUES (
                                    @UID, @BID, @INV, @AMT, 'PKR', 'PKR',
                                    'Bank Transfer / Challan', 1, 'Pending', 1,
                                    @REF, @REF, @EXP, @EXP, @CBY, SYSUTCDATETIME(), SYSUTCDATETIME()
                                );
                            END
                            ELSE
                            BEGIN
                                INSERT INTO dbo.WN_Payments (
                                    IdGUID, PublicId, UserId, BookingId, InvoiceId, Amount, Currency, CurrencyCode,
                                    PaymentMethod, PaymentMethodId, PaymentStatus, StatusId,
                                    TransactionRef, GatewayRef, ExpiresOn, ExpiryDate, CreatedById, CreatedAt, CreatedOn
                                ) VALUES (
                                    NEWID(), NEWID(), @UG, @BG, @INV, @AMT, 'PKR', 'PKR',
                                    'Bank Transfer / Challan', 1, 'Pending', 1,
                                    @REF, @REF, @EXP, @EXP, @CBY, SYSUTCDATETIME(), SYSUTCDATETIME()
                                );
                            END

                            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Payments') AND name = 'UserIdInt')
                            BEGIN
                                UPDATE dbo.WN_Payments
                                SET UserIdInt = @UID, BookingIdInt = @BID
                                WHERE InvoiceId = @INV OR TransactionRef = @REF;
                            END
                        ";
                        await using var payCmd = new SqlCommand(paySql, c);
                        payCmd.Parameters.AddWithValue("@UG", (object?)userGuid ?? DBNull.Value);
                        payCmd.Parameters.AddWithValue("@BG", (object?)bookingGuid ?? DBNull.Value);
                        payCmd.Parameters.AddWithValue("@UID", userId);
                        payCmd.Parameters.AddWithValue("@BID", bookingId);
                        payCmd.Parameters.AddWithValue("@INV", (object?)invoiceId ?? DBNull.Value);
                        payCmd.Parameters.AddWithValue("@AMT", totalAmt);
                        payCmd.Parameters.AddWithValue("@REF", (object?)challanNum ?? $"WN-BK-{bookingId}");
                        payCmd.Parameters.AddWithValue("@EXP", validity);
                        payCmd.Parameters.AddWithValue("@CBY", (object?)createdById ?? DBNull.Value);
                        await payCmd.ExecuteNonQueryAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating payment entry: {ex.Message}");
                }
            }

            return result;
        }

        public async Task<IDictionary<string, object?>> InsertSmartBookingAsync(
            string userEmail, string categoryCode, DateTime startOn, DateTime endOn, int? capacity, string? notes, int? createdById,
            string? customerEmail = null, string? customerFirstName = null, string? customerLastName = null, string? customerPhone = null,
            string? customerCnic = null, string? customerAddress = null, int? customerCityId = null, string? customerNotes = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_InsertSmart", c);
            cmd.Parameters.AddWithValue("@UserEmail", userEmail);
            cmd.Parameters.AddWithValue("@CategoryCode", categoryCode);
            cmd.Parameters.AddWithValue("@StartOn", startOn);
            cmd.Parameters.AddWithValue("@EndOn", endOn);
            cmd.Parameters.AddWithValue("@Capacity", (object?)capacity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerEmail", (object?)customerEmail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerFirstName", (object?)customerFirstName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerLastName", (object?)customerLastName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerPhone", (object?)customerPhone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerCnic", (object?)customerCnic ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerAddress", (object?)customerAddress ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerCityId", (object?)customerCityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerNotes", (object?)customerNotes ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) return ToDict(r);
            return new Dictionary<string, object?>();
        }

        public async Task UpdateBookingAsync(int id, DateTime? startOn, DateTime? endOn, string? notes, int? updatedById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_Update", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@StartOn", (object?)startOn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@EndOn", (object?)endOn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedById", (object?)updatedById ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateBookingStatusAsync(int id, byte statusId, int? updatedById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_UpdateStatus", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@BookingStatusId", statusId);
            cmd.Parameters.AddWithValue("@UpdatedById", (object?)updatedById ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task CancelBookingAsync(int id, string? userEmail, string? cancelReason, int? updatedById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_Cancel", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@UserEmail", (object?)userEmail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CancelReason", (object?)cancelReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedById", (object?)updatedById ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ReassignBookingAsync(int id, int newSpaceId, int newPricingId, string? userEmail, int? updatedById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_Reassign", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@NewSpaceId", newSpaceId);
            cmd.Parameters.AddWithValue("@NewPricingId", newPricingId);
            cmd.Parameters.AddWithValue("@UserEmail", (object?)userEmail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedById", (object?)updatedById ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- Payment ---

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetPaymentsAsync(int page, int limit, string? search)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Payments_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            int total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) ? Convert.ToInt32(t) : rows.Count;
            return (rows, total);
        }

        public async Task<IDictionary<string, object?>?> GetPaymentSummaryAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Payments_GetSummary", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetMyPaymentsAsync(string userEmail)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Payments_GetMyList", c);
            cmd.Parameters.AddWithValue("@UserEmail", userEmail);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IDictionary<string, object?>> InsertPaymentAsync(int userId, int? bookingId, byte paymentMethodId, decimal amount, string? notes, int? createdById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Payments_Insert", c);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@BookingId", (object?)bookingId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PaymentMethodId", paymentMethodId);
            cmd.Parameters.AddWithValue("@Amount", amount);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) return ToDict(r);
            return new Dictionary<string, object?>();
        }

        public async Task<IDictionary<string, object?>> GenerateVoucherAsync(int userId, int? bookingId, decimal amount, DateTime expiresOn, int? createdById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Payments_GenerateVoucher", c);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@BookingId", (object?)bookingId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Amount", amount);
            cmd.Parameters.AddWithValue("@ExpiresOn", expiresOn);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) return ToDict(r);
            return new Dictionary<string, object?>();
        }

        public async Task UpdatePaymentStatusAsync(int id, byte statusId, int? updatedById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Payments_UpdateStatus", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@StatusId", statusId);
            cmd.Parameters.AddWithValue("@UpdatedById", (object?)updatedById ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeletePaymentAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Payments_Delete", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- Membership ---

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetMembershipsAsync(int page, int limit, string? search)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Memberships_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            int total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) ? Convert.ToInt32(t) : rows.Count;
            return (rows, total);
        }

        public async Task<IDictionary<string, object?>?> GetMembershipSummaryAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Memberships_GetSummary", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<IDictionary<string, object?>> InsertMembershipAsync(int userId, int planId, DateTime startOn, DateTime? endOn, bool autoRenew, string? notes, int? createdById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Memberships_Insert", c);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@PlanId", planId);
            cmd.Parameters.AddWithValue("@StartOn", startOn);
            cmd.Parameters.AddWithValue("@EndOn", (object?)endOn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AutoRenew", autoRenew ? 1 : 0);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) return ToDict(r);
            return new Dictionary<string, object?>();
        }

        public async Task UpdateMembershipStatusAsync(int id, byte statusId, int? updatedById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Memberships_UpdateStatus", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@StatusId", statusId);
            cmd.Parameters.AddWithValue("@UpdatedById", (object?)updatedById ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteMembershipAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Memberships_Delete", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- PricingPlan ---

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllPricingPlansAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_MembershipPlans_GetAll", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetPricingPlansAsync(int page, int limit)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_MembershipPlans_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            int total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) ? Convert.ToInt32(t) : rows.Count;
            return (rows, total);
        }

        public async Task<IDictionary<string, object?>?> GetPricingPlanSummaryAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_MembershipPlans_GetSummary", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<(int? Id, string? PublicId)> InsertPricingPlanAsync(string name, string? description, byte billingPeriodId, decimal price, int? includesHours, string currencyCode, int? createdById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_MembershipPlans_Insert", c);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BillingPeriodId", billingPeriodId);
            cmd.Parameters.AddWithValue("@Price", price);
            cmd.Parameters.AddWithValue("@IncludesHours", (object?)includesHours ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CurrencyCode", currencyCode);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return (row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null,
                        row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
            }
            return (null, null);
        }

        public async Task UpdatePricingPlanAsync(int id, string? name, string? description, byte? billingPeriodId, decimal? price, int? includesHours, string? currencyCode)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_MembershipPlans_Update", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BillingPeriodId", (object?)billingPeriodId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Price", (object?)price ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IncludesHours", (object?)includesHours ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CurrencyCode", (object?)currencyCode ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeletePricingPlanAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_MembershipPlans_Delete", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- PlanFeature ---

        public async Task<IEnumerable<IDictionary<string, object?>>> GetPlanFeaturesByPlanIdAsync(int planId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_MembershipPlanFeatures_GetByPlan", c);
            cmd.Parameters.AddWithValue("@PlanId", planId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<(int? Id, string? PublicId)> InsertPlanFeatureAsync(int planId, string featureName, string? featureValue, short sortOrder)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_MembershipPlanFeatures_Insert", c);
            cmd.Parameters.AddWithValue("@PlanId", planId);
            cmd.Parameters.AddWithValue("@FeatureName", featureName);
            cmd.Parameters.AddWithValue("@FeatureValue", (object?)featureValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SortOrder", sortOrder);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return (row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null,
                        row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
            }
            return (null, null);
        }

        public async Task UpdatePlanFeatureAsync(int id, string? featureName, string? featureValue, short? sortOrder)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_MembershipPlanFeatures_Update", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@FeatureName", (object?)featureName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FeatureValue", (object?)featureValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SortOrder", (object?)sortOrder ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeletePlanFeatureAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_MembershipPlanFeatures_Delete", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- Location ---

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllLocationsAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Locations_GetAll", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetLocationsAsync(int page, int limit, string? search)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Locations_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BranchId", DBNull.Value);
            cmd.Parameters.AddWithValue("@CompanyId", DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            int total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) ? Convert.ToInt32(t) : rows.Count;
            return (rows, total);
        }

        public async Task<(int? Id, string? PublicId)> InsertLocationAsync(int branchId, string name, string? address, int cityId, string? openingTime, string? closingTime, decimal? latitude, decimal? longitude, int? createdById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Locations_Insert", c);
            cmd.Parameters.AddWithValue("@BranchId", branchId);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CityId", cityId);
            cmd.Parameters.AddWithValue("@OpeningTime", (object?)openingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ClosingTime", (object?)closingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Latitude", (object?)latitude ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Longitude", (object?)longitude ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return (row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null,
                        row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
            }
            return (null, null);
        }

        public async Task UpdateLocationAsync(int id, string? name, string? address, int? cityId, string? openingTime, string? closingTime, decimal? latitude, decimal? longitude)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Locations_Update", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CityId", (object?)cityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OpeningTime", (object?)openingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ClosingTime", (object?)closingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Latitude", (object?)latitude ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Longitude", (object?)longitude ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteLocationAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Locations_Delete", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- Branch ---

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllBranchesAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Branches_GetList", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllCompaniesAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Companies_GetList", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllCitiesAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Cities_GetList", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        // --- Floor ---

        public async Task<IEnumerable<IDictionary<string, object?>>> GetFloorsAsync(int? locationId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Floors_GetList", c);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<int?> InsertFloorAsync(int locationId, string name, short floorNumber, int? createdById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Floors_Insert", c);
            cmd.Parameters.AddWithValue("@LocationId", locationId);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@FloorNumber", floorNumber);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        // --- Space ---

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllSpaceTypesAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_SpaceTypes_GetList", c);
            cmd.Parameters.AddWithValue("@CategoryId", DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetSpaceTypesAsync(int page, int limit)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_SpaceTypes_GetList", c);
            cmd.Parameters.AddWithValue("@CategoryId", DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var all = await ReadAll(r);
            int total = all.Count > 0 && all[0].TryGetValue("TotalCount", out var t) ? Convert.ToInt32(t) : all.Count;
            var rows = all.Skip((page - 1) * limit).Take(limit).ToList();
            return (rows, total);
        }

        public async Task<(int? Id, string? PublicId)> InsertSpaceTypeAsync(string name, string? description, byte? categoryId, short? capacity, bool hourlyAllowed, int? createdById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_SpaceTypes_Insert", c);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CategoryId", (object?)categoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Capacity", (object?)capacity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@HourlyAllowed", hourlyAllowed ? 1 : 0);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return (row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null,
                        row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
            }
            return (null, null);
        }

        public async Task UpdateSpaceTypeAsync(int id, string? name, string? description, byte? categoryId, short? capacity, bool? hourlyAllowed, int? updatedById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_SpaceTypes_Update", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CategoryId", (object?)categoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Capacity", (object?)capacity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@HourlyAllowed", hourlyAllowed.HasValue ? (object)(hourlyAllowed.Value ? 1 : 0) : DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedById", (object?)updatedById ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteSpaceTypeAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_SpaceTypes_Delete", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- Space ---

        private async Task EnsureSpaceConfigColumnsExistAsync(SqlConnection c)
        {
            try
            {
                string sql = @"
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'WN_SpaceConfig')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_SpaceConfig') AND name = 'SecurityAccountId')
        ALTER TABLE dbo.WN_SpaceConfig ADD SecurityAccountId INT NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_SpaceConfig') AND name = 'RentAccountId')
        ALTER TABLE dbo.WN_SpaceConfig ADD RentAccountId INT NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_SpaceConfig') AND name = 'DepositAccountId')
        ALTER TABLE dbo.WN_SpaceConfig ADD DepositAccountId INT NULL;
END";
                await using var cmd = new SqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetSpaceConfigAsync()
        {
            await using var c = await Open();
            await EnsureSpaceConfigColumnsExistAsync(c);
            await using var cmd = SP("dbo.WN_SpaceConfig_GetList", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetSpaceConfigV2Async(int? companyId, int? branchId, int? locationId)
        {
            await using var c = await Open();
            await EnsureSpaceConfigColumnsExistAsync(c);
            await using var cmd = SP("dbo.WN_SpaceConfig_GetListV2", c);
            cmd.Parameters.AddWithValue("@CompanyId", (object?)companyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BranchId", (object?)branchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<int> InsertSpaceConfigV2Async(SpaceConfigV2Request req, string? createdBy)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_SpaceConfig_Insert", c);
            cmd.Parameters.AddWithValue("@SpaceCategory", (object?)req.SpaceCategory ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TotalSpaces", req.TotalSpaces);
            cmd.Parameters.AddWithValue("@CodePrefix", req.CodePrefix ?? string.Empty);
            cmd.Parameters.AddWithValue("@MinCode", req.MinCode);
            cmd.Parameters.AddWithValue("@OpeningTime", (object?)req.OpeningTime ?? "08:00");
            cmd.Parameters.AddWithValue("@ClosingTime", (object?)req.ClosingTime ?? "20:00");
            cmd.Parameters.AddWithValue("@SecurityDeposit", req.SecurityDeposit);
            cmd.Parameters.AddWithValue("@FloorId", (object?)req.FloorId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Price", req.Price);
            cmd.Parameters.AddWithValue("@BillingPeriodId", (object?)req.BillingPeriodId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Amenities", (object?)req.Amenities ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationId", (object?)req.LocationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SpaceTypeId", (object?)req.SpaceTypeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedBy", (object?)createdBy ?? (object?)req.CreatedBy ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync() && r["NewId"] != DBNull.Value)
            {
                return Convert.ToInt32(r["NewId"]);
            }
            return 0;
        }

        public async Task UpdateSpaceConfigV2Async(int id, SpaceConfigV2Request req, string? updatedBy)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_SpaceConfig_Update", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@TotalSpaces", req.TotalSpaces);
            cmd.Parameters.AddWithValue("@CodePrefix", req.CodePrefix ?? string.Empty);
            cmd.Parameters.AddWithValue("@MinCode", req.MinCode);
            cmd.Parameters.AddWithValue("@OpeningTime", (object?)req.OpeningTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ClosingTime", (object?)req.ClosingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SecurityDeposit", req.SecurityDeposit);
            cmd.Parameters.AddWithValue("@FloorId", (object?)req.FloorId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Price", req.Price);
            cmd.Parameters.AddWithValue("@BillingPeriodId", (object?)req.BillingPeriodId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Amenities", (object?)req.Amenities ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedBy", (object?)updatedBy ?? (object?)req.UpdatedBy ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteSpaceConfigV2Async(int id)
        {
            await using var c = await Open();
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "UPDATE dbo.WN_SpaceConfig SET Status = 0, UpdatedOn = GETUTCDATE() WHERE Id = @Id", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<decimal> GetSecurityDepositAsync(string category)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_SpaceConfig_GetDepositByCategory", c);
            cmd.Parameters.AddWithValue("@CategoryCode", category);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return row.TryGetValue("SecurityDeposit", out var d) && d is not null ? Convert.ToDecimal(d) : 0;
            }
            return 0;
        }

        public async Task UpdateSpaceConfigAsync(string category, string? updatedBy, int? totalSpaces, string? defaultCapacities, string? openingTime, string? closingTime, decimal? securityDeposit, decimal? price = null, byte? billingPeriodId = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_SpaceConfig_Update", c);
            cmd.Parameters.AddWithValue("@SpaceCategory", category);
            cmd.Parameters.AddWithValue("@UpdatedBy", (object?)updatedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TotalSpaces", (object?)totalSpaces ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DefaultCapacities", (object?)defaultCapacities ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OpeningTime", (object?)openingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ClosingTime", (object?)closingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SecurityDeposit", (object?)securityDeposit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Price", (object?)price ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BillingPeriodId", (object?)billingPeriodId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IDictionary<string, object?>> GenerateSpaceInventoryAsync(int locationId, int spaceTypeId, string codePrefix, int minCode, int totalSpaces)
        {
            await using var c = await Open();
            // If called with just a configId (locationId param used as configId, rest zero)
            int configId = (spaceTypeId == 0 && minCode == 0 && totalSpaces == 0)
                ? locationId
                : 0;

            if (configId == 0)
            {
                // Find config by locationId + spaceTypeId
                await using var find = new Microsoft.Data.SqlClient.SqlCommand(
                    "SELECT Id FROM dbo.WN_SpaceConfig WHERE LocationId=@L AND SpaceTypeId=@ST AND Status=1", c);
                find.Parameters.AddWithValue("@L", locationId);
                find.Parameters.AddWithValue("@ST", spaceTypeId);
                await using var fr = await find.ExecuteReaderAsync();
                if (await fr.ReadAsync()) configId = Convert.ToInt32(fr["Id"]);
            }

            if (configId == 0)
                return new Dictionary<string, object?> { ["ErrorMessage"] = "No active config found." };

            Guid? locationGuid = null;
            Guid? spaceTypeGuid = null;
            int configMinCode = 0;
            int configTotalSpaces = 0;

            await using (var configCmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT sc.MinCode, sc.TotalSpaces, l.IdGUID AS LocationGuid, st.IdGUID AS SpaceTypeGuid " +
                "FROM dbo.WN_SpaceConfig sc " +
                "JOIN dbo.WN_Locations l ON l.Id = sc.LocationId " +
                "JOIN dbo.WN_SpaceTypes st ON st.Id = sc.SpaceTypeId " +
                "WHERE sc.Id = @ConfigId AND sc.Status = 1", c))
            {
                configCmd.Parameters.AddWithValue("@ConfigId", configId);
                await using var configReader = await configCmd.ExecuteReaderAsync();
                if (await configReader.ReadAsync())
                {
                    configMinCode = Convert.ToInt32(configReader["MinCode"]);
                    configTotalSpaces = Convert.ToInt32(configReader["TotalSpaces"]);
                    locationGuid = configReader["LocationGuid"] is Guid lg ? lg : null;
                    spaceTypeGuid = configReader["SpaceTypeGuid"] is Guid stg ? stg : null;
                }
            }

            await using var cmd = SP("dbo.WN_SpaceConfig_GenerateSpaces", c);
            cmd.Parameters.AddWithValue("@ConfigId", configId);

            IDictionary<string, object?>? result;
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                result = await r.ReadAsync() ? ToDict(r) : null;
            }

            if (locationGuid.HasValue && spaceTypeGuid.HasValue)
            {
                await using var fix = new Microsoft.Data.SqlClient.SqlCommand(
                    "UPDATE s SET " +
                    "    IsActive = ISNULL(s.IsActive, 1)," +
                    "    Status   = ISNULL(s.Status, 1) " +
                    "FROM dbo.WN_Spaces s " +
                    "WHERE (s.LocationId = @LocationGuid OR s.LocationId = @LocationId) " +
                    "  AND (s.SpaceTypeId = @SpaceTypeGuid OR s.SpaceTypeId = @SpaceTypeId);", c);
                fix.Parameters.AddWithValue("@LocationGuid", locationGuid.Value);
                fix.Parameters.AddWithValue("@SpaceTypeGuid", spaceTypeGuid.Value);
                fix.Parameters.AddWithValue("@LocationId", locationId);
                fix.Parameters.AddWithValue("@SpaceTypeId", spaceTypeId);
                await fix.ExecuteNonQueryAsync();
            }

            return result ?? new Dictionary<string, object?>();
        }

        private async Task EnsureSpaceConfigSpUpdatedAsync(SqlConnection c)
        {
            try
            {
                string sql = @"
CREATE OR ALTER PROCEDURE dbo.WN_SpaceConfig_GetSpaceStatus
    @ConfigId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @LocationId    INT;
    DECLARE @SpaceTypeId   INT;

    SELECT 
        @LocationId  = LocationId,
        @SpaceTypeId = SpaceTypeId
    FROM dbo.WN_SpaceConfig WHERE Id = @ConfigId;

    SELECT
        s.Id,
        COALESCE(CAST(s.IdGUID AS NVARCHAR(50)), CAST(s.Id AS NVARCHAR(50))) AS IdGuid,
        COALESCE(CAST(s.IdGUID AS NVARCHAR(50)), CAST(s.Id AS NVARCHAR(50))) AS PublicId,
        s.Code,
        s.Name,
        ISNULL(s.Status, 1) AS Status,
        ISNULL(s.IsActive, 1) AS IsActive,
        CASE WHEN EXISTS (
            SELECT 1 FROM dbo.WN_Bookings b
            WHERE b.SpaceId = s.Id
              AND (b.IsDeleted = 0 OR b.IsDeleted IS NULL)
              AND b.BookingStatusId IN (1, 2)
              AND b.StartOn <= SYSUTCDATETIME()
              AND b.EndOn >= SYSUTCDATETIME()
        ) THEN 1 ELSE 0 END AS HasBookings
    FROM dbo.WN_Spaces s
    WHERE (s.IsActive IS NULL OR s.IsActive = 1)
      AND s.LocationId = @LocationId
      AND s.SpaceTypeId = @SpaceTypeId
    ORDER BY s.Id ASC;
END";
                await using var cmd = new SqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        private async Task EnsureSpacesGetListSpUpdatedAsync(SqlConnection c)
        {
            try
            {
                string sql = @"
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_GetList]
    @Page        INT           = 1,
    @Limit       INT           = 20,
    @Search      NVARCHAR(255) = NULL,
    @LocationId  INT           = NULL,
    @SpaceTypeId INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;

    SELECT
        s.Id, s.IdGUID AS PublicId, s.Code, s.Name, s.Description,
        s.LocationId, l.Name AS LocationName,
        s.FloorId, f.Name AS FloorName, f.FloorNumber,
        s.SpaceTypeId, st.Name AS SpaceTypeName,
        sc.Code AS CategoryCode, sc.Label AS CategoryLabel,
        s.Capacity, s.ImageUrl, s.IsActive,
        CASE
            WHEN s.IsActive = 0 THEN 'Inactive'
            WHEN EXISTS (
                SELECT 1 FROM dbo.WN_Bookings b WITH (NOLOCK)
                WHERE b.SpaceId = s.Id
                  AND b.BookingStatusId IN (1, 2)
                  AND b.EndOn >= SYSUTCDATETIME()
                  AND (b.IsDeleted IS NULL OR b.IsDeleted = 0)
            ) THEN 'Booked'
            ELSE 'Available'
        END AS Status,
        (
            SELECT MAX(b.EndOn)
            FROM dbo.WN_Bookings b WITH (NOLOCK)
            WHERE b.SpaceId = s.Id
              AND b.BookingStatusId IN (1, 2)
              AND b.EndOn >= SYSUTCDATETIME()
              AND (b.IsDeleted IS NULL OR b.IsDeleted = 0)
        ) AS BookedTill,
        s.Price, s.BillingPeriodId, bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
        s.Price AS SeatPrice, s.Price AS RoomPrice,
        s.Amenities,
        COUNT(*) OVER() AS TotalCount
    FROM  dbo.WN_Spaces          s  WITH (NOLOCK)
    JOIN  dbo.WN_Locations       l  WITH (NOLOCK) ON l.Id  = s.LocationId
    JOIN  dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
    JOIN  dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    LEFT JOIN dbo.WN_Floors      f  WITH (NOLOCK) ON f.Id  = s.FloorId
    LEFT JOIN dbo.WN_BillingPeriods bp WITH (NOLOCK) ON bp.Id = s.BillingPeriodId
    WHERE (@Search IS NULL
            OR s.Name LIKE '%' + @Search + '%'
            OR s.Code LIKE '%' + @Search + '%')
      AND (@LocationId  IS NULL OR s.LocationId  = @LocationId)
      AND (@SpaceTypeId IS NULL OR s.SpaceTypeId = @SpaceTypeId)
    ORDER BY s.Id DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END";
                await using var cmd = new SqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        private async Task EnsureQuotationsConvertToBookingSpUpdatedAsync(SqlConnection c)
        {
            try
            {
                string sql = @"
CREATE OR ALTER PROCEDURE [dbo].[WN_Quotations_ConvertToBooking]
    @QuotationId INT,
    @CreatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @CustomerId INT, @SpaceId INT, @StartOn DATETIME2, @EndOn DATETIME2, 
                @Subtotal DECIMAL(18,2), @DiscountPct DECIMAL(5,2), @DiscountType NVARCHAR(20), 
                @DiscountVal DECIMAL(18,2), @SecDep DECIMAL(18,2), @FloorId INT, @BPM INT, 
                @SupportChargesId TINYINT;

        SELECT 
            @CustomerId = CustomerId,
            @SpaceId = SpaceId,
            @StartOn = StartDateTime,
            @EndOn = EndDateTime,
            @Subtotal = SubtotalAmount,
            @DiscountPct = ISNULL(DiscountPercentage, 0.0),
            @DiscountType = ISNULL(DiscountType, 'Percentage'),
            @DiscountVal = ISNULL(DiscountAmount, 0.0),
            @SecDep = SecurityDeposit,
            @FloorId = FloorId,
            @BPM = ISNULL(BillingPeriodMonths, 3),
            @SupportChargesId = ISNULL(SupportChargesId, 4)
        FROM dbo.WN_Quotations
        WHERE Id = @QuotationId;

        IF @CustomerId IS NULL
            RAISERROR('Quotation not found.', 16, 1);

        DECLARE @UserId INT, @CustomerEmail NVARCHAR(255), @CustomerFirstName NVARCHAR(100),
                @CustomerLastName NVARCHAR(100), @CustomerPhone NVARCHAR(50), @CustomerCnic NVARCHAR(50),
                @CustomerAddress NVARCHAR(500), @CustomerCityId INT, @CustomerNotes NVARCHAR(1000),
                @CustomerCode NVARCHAR(20);

        SELECT TOP 1 
            @UserId = UserId,
            @CustomerEmail = Email,
            @CustomerFirstName = FirstName,
            @CustomerLastName = LastName,
            @CustomerPhone = PhoneNumber,
            @CustomerCnic = CnicOrPassport,
            @CustomerAddress = Address,
            @CustomerCityId = CityId,
            @CustomerNotes = Notes,
            @CustomerCode = Code
        FROM dbo.WN_Customers
        WHERE Id = @CustomerId;

        -- Validate UserId against WN_Users
        IF @UserId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.WN_Users WHERE Id = @UserId)
        BEGIN
            SET @UserId = NULL;
        END

        IF @UserId IS NULL AND @CustomerEmail IS NOT NULL AND @CustomerEmail <> ''
        BEGIN
            SELECT TOP 1 @UserId = Id FROM dbo.WN_Users WHERE Email = @CustomerEmail;
        END

        DECLARE @CustFullName NVARCHAR(200) = RTRIM(LTRIM(ISNULL(@CustomerFirstName, '') + ' ' + ISNULL(@CustomerLastName, '')));
        IF @CustFullName = '' SET @CustFullName = ISNULL(@CustomerFirstName, 'Customer');

        DECLARE @InsertResult TABLE (
            BookingId INT,
            BookingPublicId UNIQUEIDENTIFIER,
            ChallanNumber NVARCHAR(50),
            ChallanValidUntil DATETIME,
            SubtotalAmount DECIMAL(18,2),
            TaxAmount DECIMAL(18,2),
            TotalAmount DECIMAL(18,2),
            ErrorMessage NVARCHAR(MAX)
        );

        INSERT INTO @InsertResult
        EXEC dbo.WN_Bookings_Insert
            @UserId = @UserId,
            @SpaceId = @SpaceId,
            @PricingId = 0,
            @StartOn = @StartOn,
            @EndOn = @EndOn,
            @Notes = 'Converted from Quotation',
            @CreatedById = @CreatedById,
            @UserEmail = @CustomerEmail,
            @CustomerEmail = @CustomerEmail,
            @CustomerFirstName = @CustFullName,
            @CustomerLastName = @CustomerLastName,
            @CustomerPhone = @CustomerPhone,
            @CustomerCnic = @CustomerCnic,
            @CustomerAddress = @CustomerAddress,
            @CustomerCityId = @CustomerCityId,
            @CustomerNotes = @CustomerNotes,
            @DiscountPercentage = @DiscountPct,
            @DiscountAmount = @DiscountVal,
            @DiscountType = @DiscountType,
            @BillingPeriodMonths = @BPM,
            @SupportChargesId = @SupportChargesId;

        DECLARE @NewBookingId INT;
        DECLARE @NewBookingPublicId UNIQUEIDENTIFIER;
        DECLARE @ErrMsg NVARCHAR(MAX);

        SELECT TOP 1 
            @NewBookingId = BookingId,
            @NewBookingPublicId = BookingPublicId,
            @ErrMsg = ErrorMessage
        FROM @InsertResult;

        IF @NewBookingId IS NULL OR @NewBookingId <= 0
        BEGIN
            RAISERROR(@ErrMsg, 16, 1);
        END

        -- Ensure CustomerCode and details on Booking are properly linked to the original Customer
        UPDATE dbo.WN_Bookings
        SET CustomerCode = ISNULL(@CustomerCode, CustomerCode),
            UserId = @UserId
        WHERE Id = @NewBookingId;

        UPDATE dbo.WN_BookingDetails
        SET CustomerCode = ISNULL(@CustomerCode, CustomerCode),
            CustomerName = @CustFullName,
            CustomerEmail = @CustomerEmail
        WHERE BookingGuid = @NewBookingPublicId;

        UPDATE dbo.WN_Quotations
        SET Status = 'Converted',
            BookingId = @NewBookingId,
            IsActive = 0,
            UpdatedDate = SYSUTCDATETIME(),
            UpdatedById = @CreatedById
        WHERE Id = @QuotationId;

        COMMIT TRANSACTION;

        SELECT 
            @NewBookingId AS BookingId,
            @NewBookingPublicId AS BookingPublicId,
            @QuotationId AS QuotationId,
            'Converted' AS Status,
            NULL AS ErrorMessage;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;";
                await using var cmd = new SqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        private async Task EnsureBookingSummaryViewUpdatedAsync(SqlConnection c)
        {
            try
            {
                string sql = @"
CREATE OR ALTER VIEW [dbo].[VW_WN_BookingSummary] AS
    SELECT 
        b.Id                      AS BookingId, 
        b.IdGUID                  AS BookingPublicId, 
        b.IdGUID                  AS BookingIdGuid, 
        b.StartOn                 AS StartOn,
        b.StartOn                 AS ContractStartDate,
        b.EndOn                   AS EndOn,
        b.EndOn                   AS ContractEndDate,
        b.BookingStatusId, 
        bs.Code                   AS BookingStatusCode, 
        bs.Label                  AS BookingStatusLabel,
        b.Notes, 
        b.CancelReason, 
        b.CreatedOn               AS BookedOn,
        u.Id                      AS UserId, 
        u.IdGUID                  AS UserPublicId, 
        u.IdGUID                  AS UserIdGuid, 
        COALESCE(
            NULLIF(RTRIM(LTRIM(ISNULL(cust.FirstName, '') + ' ' + ISNULL(cust.LastName, ''))), ''),
            NULLIF(u.Name, ''),
            'Customer'
        )                         AS UserName, 
        COALESCE(NULLIF(cust.Email, ''), u.Email) AS UserEmail,
        COALESCE(
            NULLIF(RTRIM(LTRIM(ISNULL(cust.FirstName, '') + ' ' + ISNULL(cust.LastName, ''))), ''),
            NULLIF(u.Name, ''),
            'Customer'
        )                         AS CustomerName, 
        COALESCE(NULLIF(cust.Email, ''), u.Email) AS CustomerEmail,
        b.CustomerCode            AS CustomerCode,
        cust.PhoneNumber          AS CustomerPhone,
        s.Id                      AS SpaceId, 
        s.IdGUID                  AS SpacePublicId, 
        s.IdGUID                  AS SpaceIdGuid, 
        s.Code                    AS SpaceCode, 
        ISNULL(NULLIF(s.Code, ''), s.Name) AS SpaceNumber,
        s.Name                    AS SpaceName, 
        s.Capacity                AS SpaceCapacity,
        st.Id                     AS SpaceTypeId, 
        st.Name                   AS SpaceTypeName,
        l.Id                      AS LocationId, 
        l.Name                    AS LocationName,
        br.Id                     AS BranchId, 
        br.[Description]          AS BranchName,
        co.Id                     AS CompanyId, 
        co.CompanyName            AS CompanyName,
        ISNULL(sp.SeatPrice, 0.0) AS SeatPrice,
        
        CASE 
          WHEN b.StartOn IS NOT NULL AND b.EndOn IS NOT NULL AND DATEDIFF(month, b.StartOn, b.EndOn) > 0 
          THEN DATEDIFF(month, b.StartOn, b.EndOn)
          ELSE 1 
        END AS NumberOfMonths,
        CASE 
          WHEN b.StartOn IS NOT NULL AND b.EndOn IS NOT NULL AND DATEDIFF(month, b.StartOn, b.EndOn) > 0 
          THEN DATEDIFF(month, b.StartOn, b.EndOn)
          ELSE 1 
        END AS ContractDuration,

        CAST(
          ISNULL(
            NULLIF(
              CASE 
                WHEN st.Name LIKE '%Private%' OR sc.Code IN ('PrivateOffice', 'Private')
                THEN ISNULL(sp.SeatPrice * s.Capacity, 0.0)
                ELSE ISNULL(sp.SeatPrice, 0.0)
              END, 0.0),
            CASE 
              WHEN b.StartOn IS NOT NULL AND b.EndOn IS NOT NULL AND DATEDIFF(month, b.StartOn, b.EndOn) > 0
              THEN ISNULL(b.SubtotalAmount, b.TotalAmount) / DATEDIFF(month, b.StartOn, b.EndOn)
              ELSE ISNULL(b.SubtotalAmount, ISNULL(b.TotalAmount, 35000.00))
            END
          ) AS DECIMAL(18,2)
        ) AS MonthlyRent,

        CASE 
          WHEN st.Name LIKE '%Private%' OR sc.Code IN ('PrivateOffice', 'Private')
          THEN ISNULL(sp.SeatPrice * s.Capacity, 0.0)
          ELSE ISNULL(sp.SeatPrice, 0.0)
        END AS RoomPrice,

        ISNULL(
          NULLIF(inv_meta.BillingPeriodMonths, 0),
          CASE 
            WHEN bp.Code = 'Quarterly' OR bp.Code = '3 Months' OR bp.Label LIKE '%3 Month%' THEN 3
            WHEN bp.Code = 'SemiAnnual' OR bp.Code = '6 Months' OR bp.Label LIKE '%6 Month%' THEN 6
            WHEN bp.Code = 'BiMonthly' OR bp.Code = '2 Months' OR bp.Label LIKE '%2 Month%' THEN 2
            WHEN bp.Code = 'Annual' OR bp.Code = '12 Months' OR bp.Label LIKE '%12 Month%' THEN 12
            ELSE 1
          END
        ) AS BillingPeriodMonths,

        bp.Code                   AS BillingPeriodCode, 
        ISNULL(bp.Label, CAST(ISNULL(inv_meta.BillingPeriodMonths, 1) AS NVARCHAR(10)) + ' Month(s)') AS BillingPeriodLabel,
        ISNULL(bp.Label, CAST(ISNULL(inv_meta.BillingPeriodMonths, 1) AS NVARCHAR(10)) + ' Month(s)') AS BillingPeriod,

        CAST(
          COALESCE(
            NULLIF(b.SecurityDepositRequired, 0),
            NULLIF(inv_meta.SecurityDepositAmount, 0),
            CASE 
              WHEN ISNULL(b.SecurityDepositMonths, 0) > 0 
              THEN b.SecurityDepositMonths * ISNULL(NULLIF(sp.SeatPrice * s.Capacity, 0), 35000.00 * ISNULL(s.Capacity, 1))
              WHEN st.Name LIKE '%Private%' OR sc.Code IN ('PrivateOffice', 'Private') 
              THEN 2 * ISNULL(NULLIF(sp.SeatPrice * s.Capacity, 0), 35000.00 * ISNULL(s.Capacity, 1))
              ELSE 0.0
            END
          ) AS DECIMAL(18,2)
        ) AS SecurityDeposit,

        CAST(
          (
            ISNULL(
              NULLIF(
                CASE 
                  WHEN st.Name LIKE '%Private%' OR sc.Code IN ('PrivateOffice', 'Private')
                  THEN ISNULL(sp.SeatPrice * s.Capacity, 0.0)
                  ELSE ISNULL(sp.SeatPrice, 0.0)
                END, 0.0),
              CASE 
                WHEN b.StartOn IS NOT NULL AND b.EndOn IS NOT NULL AND DATEDIFF(month, b.StartOn, b.EndOn) > 0
                THEN ISNULL(b.SubtotalAmount, b.TotalAmount) / DATEDIFF(month, b.StartOn, b.EndOn)
                ELSE ISNULL(b.SubtotalAmount, ISNULL(b.TotalAmount, 35000.00))
              END
            ) * ISNULL(
                  NULLIF(inv_meta.BillingPeriodMonths, 0),
                  CASE 
                    WHEN bp.Code = 'Quarterly' OR bp.Code = '3 Months' OR bp.Label LIKE '%3 Month%' THEN 3
                    WHEN bp.Code = 'SemiAnnual' OR bp.Code = '6 Months' OR bp.Label LIKE '%6 Month%' THEN 6
                    WHEN bp.Code = 'BiMonthly' OR bp.Code = '2 Months' OR bp.Label LIKE '%2 Month%' THEN 2
                    WHEN bp.Code = 'Annual' OR bp.Code = '12 Months' OR bp.Label LIKE '%12 Month%' THEN 12
                    ELSE 1
                  END
                )
          ) * (1.0 - (ISNULL(b.DiscountPercentage, 0.0) / 100.0))
          AS DECIMAL(18,2)
        ) AS CurrentCycleAmount,

        CAST(
          ISNULL(
            NULLIF(b.TotalAmount, 0.0),
            (
              (
                ISNULL(
                  NULLIF(
                    CASE 
                      WHEN st.Name LIKE '%Private%' OR sc.Code IN ('PrivateOffice', 'Private')
                      THEN ISNULL(sp.SeatPrice * s.Capacity, 0.0)
                      ELSE ISNULL(sp.SeatPrice, 0.0)
                    END, 0.0),
                  CASE 
                    WHEN b.StartOn IS NOT NULL AND b.EndOn IS NOT NULL AND DATEDIFF(month, b.StartOn, b.EndOn) > 0
                    THEN ISNULL(b.SubtotalAmount, b.TotalAmount) / DATEDIFF(month, b.StartOn, b.EndOn)
                    ELSE ISNULL(b.SubtotalAmount, ISNULL(b.TotalAmount, 35000.00))
                  END
                ) * CASE 
                      WHEN b.StartOn IS NOT NULL AND b.EndOn IS NOT NULL AND DATEDIFF(month, b.StartOn, b.EndOn) > 0 
                      THEN DATEDIFF(month, b.StartOn, b.EndOn)
                      ELSE 1 
                    END
              ) * (1.0 - (ISNULL(b.DiscountPercentage, 0.0) / 100.0))
            ) + COALESCE(NULLIF(b.SecurityDepositRequired, 0), NULLIF(inv_meta.SecurityDepositAmount, 0), ISNULL(sp.SecurityDeposit, 0.0))
          ) AS DECIMAL(18,2)
        ) AS TotalContractAmount,

        ISNULL(b.TotalAmount, 0.0) AS TotalAmount,
        CAST(ISNULL(inv_paid.PaidTotal, 0.0) AS DECIMAL(18,2)) AS TotalPaidAmount,

        CAST(
          CASE 
            WHEN ISNULL(
                   NULLIF(b.TotalAmount, 0.0),
                   ((ISNULL(NULLIF(CASE WHEN st.Name LIKE '%Private%' OR sc.Code IN ('PrivateOffice', 'Private') THEN ISNULL(sp.SeatPrice * s.Capacity, 0.0) ELSE ISNULL(sp.SeatPrice, 0.0) END, 0.0), 35000.00) * CASE WHEN b.StartOn IS NOT NULL AND b.EndOn IS NOT NULL AND DATEDIFF(month, b.StartOn, b.EndOn) > 0 THEN DATEDIFF(month, b.StartOn, b.EndOn) ELSE 1 END) * (1.0 - (ISNULL(b.DiscountPercentage, 0.0) / 100.0))) + COALESCE(NULLIF(b.SecurityDepositRequired, 0), NULLIF(inv_meta.SecurityDepositAmount, 0), ISNULL(sp.SecurityDeposit, 0.0))
                 ) - ISNULL(inv_paid.PaidTotal, 0.0) > 0
            THEN ISNULL(
                   NULLIF(b.TotalAmount, 0.0),
                   ((ISNULL(NULLIF(CASE WHEN st.Name LIKE '%Private%' OR sc.Code IN ('PrivateOffice', 'Private') THEN ISNULL(sp.SeatPrice * s.Capacity, 0.0) ELSE ISNULL(sp.SeatPrice, 0.0) END, 0.0), 35000.00) * CASE WHEN b.StartOn IS NOT NULL AND b.EndOn IS NOT NULL AND DATEDIFF(month, b.StartOn, b.EndOn) > 0 THEN DATEDIFF(month, b.StartOn, b.EndOn) ELSE 1 END) * (1.0 - (ISNULL(b.DiscountPercentage, 0.0) / 100.0))) + COALESCE(NULLIF(b.SecurityDepositRequired, 0), NULLIF(inv_meta.SecurityDepositAmount, 0), ISNULL(sp.SecurityDeposit, 0.0))
                 ) - ISNULL(inv_paid.PaidTotal, 0.0)
            ELSE 0.0
          END AS DECIMAL(18,2)
        ) AS BalanceLeft,

        CASE 
          WHEN b.StartOn IS NOT NULL THEN
            CASE 
              WHEN inv_paid.PaidCyclesCount > 0 THEN
                DATEADD(month, (inv_paid.PaidCyclesCount * ISNULL(NULLIF(inv_meta.BillingPeriodMonths, 0), 1)), b.StartOn)
              ELSE
                DATEADD(month, ISNULL(NULLIF(inv_meta.BillingPeriodMonths, 0), 1), b.StartOn)
            END
          ELSE ch.ValidUntil
        END AS NextBillDueDate,

        CASE 
          WHEN b.StartOn IS NOT NULL THEN
            CASE 
              WHEN inv_paid.PaidCyclesCount > 0 THEN
                DATEADD(month, (inv_paid.PaidCyclesCount * ISNULL(NULLIF(inv_meta.BillingPeriodMonths, 0), 1)), b.StartOn)
              ELSE
                DATEADD(month, ISNULL(NULLIF(inv_meta.BillingPeriodMonths, 0), 1), b.StartOn)
            END
          ELSE ch.ValidUntil
        END AS NextBillingDate,

        ISNULL(b.DiscountPercentage, 0.0) AS DiscountPercentage,
        ISNULL(b.DiscountAmount, 0.0)     AS DiscountAmount,
        ISNULL(b.SubtotalAmount, 0.0)     AS SubtotalAmount,
        ch.ChallanNumber, 
        ch.ValidUntil             AS ChallanValidUntil, 
        ch.StatusId               AS ChallanStatusId,
        ISNULL(b.SecurityDepositMonths, 0) AS SecurityDepositMonths,
        ISNULL(b.SecurityDepositRequired, 0.00) AS SecurityDepositRequired,
        ISNULL(b.SecurityDepositPaid, 0.00) AS SecurityDepositPaid,
        ISNULL(b.AdvanceRentMonths, 1) AS AdvanceRentMonths

    FROM       [dbo].[WN_Bookings]        b
    LEFT JOIN  [dbo].[WN_Users]           u    ON u.Id  = b.UserId
    LEFT JOIN  [dbo].[WN_Customers]       cust ON cust.Code = b.CustomerCode OR (b.CustomerCode IS NULL AND cust.UserId = b.UserId)
    LEFT JOIN  [dbo].[WN_Spaces]          s    ON s.Id  = b.SpaceId
    LEFT JOIN  [dbo].[WN_SpaceTypes]      st   ON st.Id = s.SpaceTypeId
    LEFT JOIN  [dbo].[WN_SpaceCategories] sc   ON sc.Id = st.CategoryId
    LEFT JOIN  [dbo].[WN_Locations]       l    ON l.Id  = s.LocationId
    LEFT JOIN  [dbo].[Branches]           br   ON br.Id = l.BranchId
    LEFT JOIN  [dbo].[Company]            co   ON co.Id = br.CompanyId
    LEFT JOIN  [dbo].[WN_SpacePricing]    sp   ON sp.Id = b.PricingId
    LEFT JOIN  [dbo].[WN_BillingPeriods]  bp   ON bp.Id = sp.BillingPeriodId
    LEFT JOIN  [dbo].[WN_BookingStatuses] bs   ON bs.Id = b.BookingStatusId

    OUTER APPLY (
        SELECT TOP 1 
            BillingPeriodMonths,
            SecurityDepositAmount,
            BillingPeriodStart,
            BillingPeriodEnd
        FROM dbo.WN_Invoices
        WHERE BookingId = b.Id AND IsDeleted = 0
        ORDER BY Id ASC
    ) inv_meta

    OUTER APPLY (
        SELECT 
            SUM(ISNULL(PaidTotal, 0.0)) AS PaidTotal,
            COUNT(CASE WHEN StatusId = 2 OR StatusId = 1 THEN 1 END) AS PaidCyclesCount
        FROM dbo.WN_Invoices
        WHERE BookingId = b.Id AND IsDeleted = 0
    ) inv_paid

    OUTER APPLY (
        SELECT TOP 1 
            ChallanNumber,
            ValidUntil,
            StatusId
        FROM dbo.WN_Challans
        WHERE BookingId = b.Id
        ORDER BY Id DESC
    ) ch;";
                await using var cmd = new SqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        private async Task EnsureBookingsInsertSpUpdatedAsync(SqlConnection c)
        {
            try
            {
                string sql = @"
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_Insert]
    @UserId                  INT,
    @SpaceId                 INT,
    @PricingId               INT,
    @StartOn                 DATETIME2,
    @EndOn                   DATETIME2,
    @Notes                   NVARCHAR(MAX) = NULL,
    @CreatedById             INT           = NULL,
    @UserEmail               NVARCHAR(256) = NULL,
    @CustomerEmail           NVARCHAR(255) = NULL,
    @CustomerFirstName       NVARCHAR(100) = NULL,
    @CustomerLastName        NVARCHAR(100) = NULL,
    @CustomerPhone           NVARCHAR(50)  = NULL,
    @CustomerCnic            NVARCHAR(50)  = NULL,
    @CustomerAddress         NVARCHAR(500) = NULL,
    @CustomerCityId          INT           = NULL,
    @CustomerNotes           NVARCHAR(1000) = NULL,
    @DiscountPercentage      DECIMAL(5,2)  = 0,
    @DiscountAmount          DECIMAL(18,2) = 0,
    @DiscountType            NVARCHAR(20)  = 'Percentage',
    @BillingPeriodMonths     INT           = NULL,
    @SecurityDepositMonths   INT           = NULL,
    @AdvanceRentMonths       INT           = NULL,
    @SupportChargesId        TINYINT       = NULL,
    @Capacity                SMALLINT      = NULL,
    @OverrideSubtotal        DECIMAL(18,2) = NULL,
    @OverrideTax             DECIMAL(18,2) = NULL,
    @OverrideTotal           DECIMAL(18,2) = NULL,
    @OverrideDuration        DECIMAL(18,2) = NULL,
    @OverrideUnitPrice       DECIMAL(18,2) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @CapOverride SMALLINT = @Capacity;
        DECLARE @CategoryCode NVARCHAR(50), @SpaceGuid UNIQUEIDENTIFIER, @SpaceCode NVARCHAR(50), @SpaceName NVARCHAR(255), @LocationId INT;

        SELECT 
            @Capacity     = ISNULL(@CapOverride, s.Capacity),
            @CategoryCode = sc.Code,
            @SpaceGuid    = s.IdGUID,
            @SpaceCode    = s.Code,
            @SpaceName    = s.Name,
            @LocationId   = s.LocationId
        FROM dbo.WN_Spaces s WITH (NOLOCK)
        LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
        LEFT JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
        WHERE s.Id = @SpaceId AND s.IsActive = 1;

        IF @Capacity IS NULL
        BEGIN
            SELECT TOP 1 
                @Capacity   = ISNULL(Capacity, 1), 
                @SpaceGuid  = IdGUID, 
                @SpaceCode  = Code, 
                @SpaceName  = Name,
                @LocationId = LocationId
            FROM dbo.WN_Spaces WITH (NOLOCK) WHERE Id = @SpaceId;
        END

        DECLARE @BillingPeriodCode NVARCHAR(20);
        IF @CategoryCode IN ('PrivateOffice', 'Private', 'SharedSpace', 'Shared')
            SET @BillingPeriodCode = 'Monthly';
        ELSE IF @CategoryCode IN ('ConferenceRoom', 'Conference', 'MeetingRoom', 'Meeting')
            SET @BillingPeriodCode = 'Daily';
        ELSE
            SET @BillingPeriodCode = 'Hourly';

        DECLARE @ResolvedPricingId INT, @SeatPrice DECIMAL(18,4), @SecurityDeposit DECIMAL(18,4);

        SELECT TOP 1 
            @ResolvedPricingId = sp.Id,
            @SeatPrice        = sp.SeatPrice,
            @SecurityDeposit  = sp.SecurityDeposit
        FROM dbo.WN_SpacePricing sp WITH (NOLOCK)
        JOIN dbo.WN_BillingPeriods bp WITH (NOLOCK) ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId       = @SpaceId
          AND sp.IsActive      = 1
          AND (bp.Code          = @BillingPeriodCode OR sp.BillingPeriodId > 0)
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC;

        IF @SeatPrice IS NULL OR @SeatPrice <= 0
        BEGIN
            SELECT TOP 1 
                @SeatPrice       = CASE 
                                     WHEN @CategoryCode IN ('PrivateOffice', 'Private') THEN 35000.00
                                     WHEN @CategoryCode IN ('SharedSpace', 'Shared') THEN 30000.00
                                     WHEN @CategoryCode IN ('ConferenceRoom', 'Conference', 'MeetingRoom', 'Meeting') THEN 20000.00
                                     ELSE 6000.00
                                   END,
                @SecurityDeposit = CASE 
                                     WHEN @CategoryCode IN ('PrivateOffice', 'Private') THEN 35000.00 * ISNULL(@Capacity, 1)
                                     ELSE 0.00
                                   END
            FROM dbo.WN_Spaces WITH (NOLOCK) WHERE Id = @SpaceId;

            SET @ResolvedPricingId = ISNULL(@ResolvedPricingId, 0);
        END

        IF @OverrideUnitPrice IS NOT NULL AND @OverrideUnitPrice > 0
            SET @SeatPrice = @OverrideUnitPrice;

        IF EXISTS (
            SELECT 1 FROM dbo.WN_Bookings WITH (UPDLOCK)
            WHERE SpaceId         = @SpaceId
              AND IsDeleted       = 0
              AND BookingStatusId IN (1, 2)
              AND @StartOn        < EndOn
              AND @EndOn          > StartOn
        )
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId,
                   NULL AS ChallanNumber, NULL AS ChallanValidUntil,
                   'Space is not available for the requested period (overlapping booking).' AS ErrorMessage;
            RETURN;
        END

        DECLARE @TargetEmail NVARCHAR(255) = ISNULL(@CustomerEmail, @UserEmail);
        DECLARE @CustomerCode NVARCHAR(20) = NULL;

        IF @TargetEmail IS NOT NULL AND @TargetEmail <> ''
        BEGIN
            SELECT TOP 1 @CustomerCode = Code
            FROM dbo.WN_Customers WITH (UPDLOCK)
            WHERE Email = @TargetEmail;
        END

        IF @CustomerCode IS NULL AND @UserId IS NOT NULL AND @UserId > 0
        BEGIN
            SELECT TOP 1 @CustomerCode = Code
            FROM dbo.WN_Customers WITH (UPDLOCK)
            WHERE UserId = @UserId;
        END

        IF @CustomerCode IS NULL
        BEGIN
            DECLARE @FName NVARCHAR(100) = ISNULL(@CustomerFirstName, 'Customer');
            DECLARE @LName NVARCHAR(100) = @CustomerLastName;
            DECLARE @Email NVARCHAR(255) = @TargetEmail;
            DECLARE @Phone NVARCHAR(50)  = @CustomerPhone;

            IF @Email IS NULL
            BEGIN
                SELECT @Email = Email, @FName = ISNULL(@FName, Name)
                FROM dbo.WN_Users WITH (NOLOCK)
                WHERE Id = @UserId;
            END

            DECLARE @NewCustId INT;
            
            INSERT INTO dbo.WN_Customers (
                UserId, FirstName, LastName, Email, PhoneNumber, CnicOrPassport, Address, CityId, IsActive, CreatedAt, Notes
            )
            VALUES (
                @UserId, @FName, @LName, @Email, @Phone, @CustomerCnic, @CustomerAddress, @CustomerCityId, 1, SYSUTCDATETIME(), @CustomerNotes
            );

            SET @NewCustId = SCOPE_IDENTITY();
            SELECT @CustomerCode = Code FROM dbo.WN_Customers WHERE Id = @NewCustId;
        END
        ELSE
        BEGIN
            IF @UserId IS NOT NULL AND @UserId > 0
            BEGIN
                UPDATE dbo.WN_Customers
                SET UserId = @UserId
                WHERE Code = @CustomerCode AND (UserId IS NULL OR UserId <= 0);
            END
        END

        DECLARE @RentAccountId    INT = NULL;
        DECLARE @DepositAccountId INT = NULL;

        SELECT TOP 1 
            @RentAccountId    = sp.RentAccountId,
            @DepositAccountId = sp.DepositAccountId
        FROM dbo.WN_SpacePricing sp WITH (NOLOCK)
        WHERE sp.SpaceId = @SpaceId AND sp.IsActive = 1;

        IF @RentAccountId IS NULL
        BEGIN
            SELECT TOP 1 
                @RentAccountId    = RentAccountId,
                @DepositAccountId = DepositAccountId
            FROM dbo.WN_SpaceConfig WITH (NOLOCK)
            WHERE SpaceTypeId = (SELECT SpaceTypeId FROM dbo.WN_Spaces WHERE Id = @SpaceId);
        END

        IF @RentAccountId IS NULL SET @RentAccountId = 2852;
        IF @DepositAccountId IS NULL AND (@SecurityDeposit > 0 OR @SecurityDepositMonths > 0) SET @DepositAccountId = 76;

        DECLARE @Duration DECIMAL(18,2) = 1.0;
        DECLARE @RentAmount DECIMAL(18,2) = 0.0;
        DECLARE @AdvMonths INT;
        DECLARE @SecMonths INT;
        DECLARE @BillPeriodMonths INT;
        
        IF @BillingPeriodCode = 'Monthly'
        BEGIN
            DECLARE @Months INT = DATEDIFF(month, @StartOn, @EndOn);
            IF @Months <= 0 SET @Months = 1;
            SET @Duration = CAST(@Months AS DECIMAL(18,2));

            SET @AdvMonths = ISNULL(@AdvanceRentMonths, ISNULL(@BillingPeriodMonths, CASE WHEN @CategoryCode IN ('PrivateOffice','Private','SharedSpace','Shared') THEN 3 ELSE 1 END));
            SET @SecMonths = ISNULL(@SecurityDepositMonths, CASE WHEN @CategoryCode IN ('PrivateOffice','Private') THEN 2 ELSE 0 END);
            SET @BillPeriodMonths = ISNULL(@BillingPeriodMonths, @AdvMonths);
            
            IF @CategoryCode IN ('PrivateOffice', 'Private')
                SET @RentAmount = @SeatPrice * ISNULL(@Capacity, 1) * @AdvMonths;
            ELSE
                SET @RentAmount = @SeatPrice * @AdvMonths;
        END
        ELSE
        BEGIN
            DECLARE @Minutes INT = DATEDIFF(minute, @StartOn, @EndOn);
            DECLARE @TotalHours DECIMAL(18,2) = CEILING(CAST(@Minutes AS DECIMAL(18,2)) / 60.0);
            IF @TotalHours <= 0 SET @TotalHours = 1.0;

            IF @TotalHours >= 24.0
            BEGIN
                DECLARE @Days DECIMAL(18,2) = CEILING(@TotalHours / 24.0);
                SET @Duration = @Days;
                SET @RentAmount = @SeatPrice * @Days;
            END
            ELSE
            BEGIN
                SET @Duration = @TotalHours;
                SET @RentAmount = @SeatPrice * @TotalHours;
            END

            SET @AdvMonths = ISNULL(@AdvanceRentMonths, 1);
            SET @SecMonths = ISNULL(@SecurityDepositMonths, 0);
            SET @BillPeriodMonths = ISNULL(@BillingPeriodMonths, 1);
        END

        IF @OverrideDuration IS NOT NULL AND @OverrideDuration > 0
            SET @Duration = @OverrideDuration;
        IF @OverrideSubtotal IS NOT NULL AND @OverrideSubtotal > 0
            SET @RentAmount = @OverrideSubtotal;

        DECLARE @MonthlyRent DECIMAL(18,2) = 0.0;
        IF @OverrideUnitPrice IS NOT NULL AND @OverrideUnitPrice > 0
            SET @MonthlyRent = @OverrideUnitPrice;
        ELSE IF @CategoryCode IN ('PrivateOffice', 'Private')
            SET @MonthlyRent = @SeatPrice * ISNULL(@Capacity, 1);
        ELSE
            SET @MonthlyRent = @SeatPrice;

        DECLARE @MonthlyDepositRate DECIMAL(18,2) = 0.0;
        IF @SecurityDeposit IS NOT NULL AND @SecurityDeposit > 0
            SET @MonthlyDepositRate = @SecurityDeposit;
        ELSE IF @CategoryCode IN ('PrivateOffice', 'Private', 'SharedSpace', 'Shared')
            SET @MonthlyDepositRate = @MonthlyRent;

        IF @SecMonths > 0
            SET @SecurityDeposit = @MonthlyDepositRate * @SecMonths;
        ELSE
            SET @SecurityDeposit = 0.00;

        DECLARE @DiscountAmt DECIMAL(18,2) = ISNULL(@DiscountAmount, 0);
        IF @DiscountAmt <= 0 AND ISNULL(@DiscountPercentage, 0) > 0
            SET @DiscountAmt = @RentAmount * (@DiscountPercentage / 100.0);
        ELSE IF @DiscountAmt > 0 AND ISNULL(@DiscountPercentage, 0) <= 0 AND @RentAmount > 0
            SET @DiscountPercentage = ROUND((@DiscountAmt / @RentAmount) * 100.0, 2);

        DECLARE @DiscountOnRent DECIMAL(18,2) = 0.00;
        DECLARE @DiscountOnDeposit DECIMAL(18,2) = 0.00;

        IF ISNULL(@DiscountPercentage, 0) > 0
        BEGIN
            SET @DiscountOnRent = ROUND(@RentAmount * (@DiscountPercentage / 100.0), 2);
            SET @DiscountOnDeposit = ROUND(ISNULL(@SecurityDeposit, 0) * (@DiscountPercentage / 100.0), 2);
        END
        ELSE IF ISNULL(@DiscountAmount, 0) > 0
        BEGIN
            DECLARE @TotalBase DECIMAL(18,2) = @RentAmount + ISNULL(@SecurityDeposit, 0);
            IF @TotalBase > 0
            BEGIN
                SET @DiscountOnRent = ROUND(@DiscountAmount * (@RentAmount / @TotalBase), 2);
                SET @DiscountOnDeposit = @DiscountAmount - @DiscountOnRent;
            END
        END

        DECLARE @DiscountedRent DECIMAL(18,2) = @RentAmount - @DiscountOnRent;
        DECLARE @DiscountedDeposit DECIMAL(18,2) = ISNULL(@SecurityDeposit, 0) - @DiscountOnDeposit;
        DECLARE @TotalDiscountAmount DECIMAL(18,2) = @DiscountOnRent + @DiscountOnDeposit;
        DECLARE @BaseRent DECIMAL(18,2) = @DiscountedRent;

        DECLARE @ProvinceId INT = NULL;
        DECLARE @TaxApplicable BIT = 0;
        DECLARE @AppliedChargePercentage DECIMAL(5,2) = 10.00;
        DECLARE @AppliedTaxPercentage DECIMAL(5,2) = 16.00;
        DECLARE @SupportChargeAmount DECIMAL(18,2) = 0.00;
        DECLARE @TaxAmount DECIMAL(18,2) = 0.00;
        DECLARE @BookingDateAnchor DATETIME = SYSUTCDATETIME();

        IF @LocationId IS NOT NULL
        BEGIN
            SELECT TOP 1 @ProvinceId = l.ProvinceId
            FROM dbo.WN_Locations l WITH (NOLOCK)
            WHERE l.Id = @LocationId;
        END

        IF @SupportChargesId IS NOT NULL
        BEGIN
            IF @ProvinceId IS NOT NULL
            BEGIN
                SELECT TOP 1 @TaxApplicable = ISNULL(TaxApplicable, 0)
                FROM dbo.WN_ProvinceChargeTypeTax WITH (NOLOCK)
                WHERE ProvinceId = @ProvinceId AND ChargeTypeId = @SupportChargesId;
            END

            SELECT TOP 1 @AppliedChargePercentage = ISNULL(ChargePercentage, 10.00)
            FROM dbo.WN_ChargeTypeRate WITH (NOLOCK)
            WHERE ChargeTypeId = @SupportChargesId
              AND StartDate <= @BookingDateAnchor
              AND (EndDate IS NULL OR EndDate > @BookingDateAnchor)
            ORDER BY StartDate DESC;

            IF @TaxApplicable = 1 AND @ProvinceId IS NOT NULL
            BEGIN
                SELECT TOP 1 @AppliedTaxPercentage = ISNULL(TaxPercentage, 16.00)
                FROM dbo.WN_Tax WITH (NOLOCK)
                WHERE ProvinceId = @ProvinceId
                  AND StartDate <= @BookingDateAnchor
                  AND (EndDate IS NULL OR EndDate > @BookingDateAnchor)
                ORDER BY StartDate DESC;
            END
        END

        SET @SupportChargeAmount = ROUND(@DiscountedRent * (@AppliedChargePercentage / 100.0), 2);
        SET @TaxAmount           = ROUND(@SupportChargeAmount * (@AppliedTaxPercentage / 100.0), 2);

        IF @OverrideTax IS NOT NULL AND @OverrideTax >= 0
            SET @TaxAmount = @OverrideTax;

        DECLARE @TotalAmount DECIMAL(18,2) = @DiscountedRent + @TaxAmount + @DiscountedDeposit;

        IF @OverrideTotal IS NOT NULL AND @OverrideTotal > 0
            SET @TotalAmount = @OverrideTotal;

        DECLARE @Today      DATE = CAST(SYSUTCDATETIME() AS DATE);
        DECLARE @SeqNum     INT;
        DECLARE @ChallanNum NVARCHAR(50);

        UPDATE dbo.WN_ChallanCounter WITH (UPDLOCK, HOLDLOCK)
        SET LastNumber = LastNumber + 1
        WHERE CounterDate = @Today;

        IF @@ROWCOUNT = 0
            INSERT INTO dbo.WN_ChallanCounter (CounterDate, LastNumber)
            SELECT @Today, 1
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.WN_ChallanCounter WITH (UPDLOCK, HOLDLOCK)
                WHERE CounterDate = @Today
            );

        SELECT @SeqNum = LastNumber
        FROM dbo.WN_ChallanCounter WITH (NOLOCK)
        WHERE CounterDate = @Today;

        SET @ChallanNum = 'WN-' + CONVERT(NVARCHAR(8), @Today, 112) + '-'
                        + RIGHT('000000' + CAST(@SeqNum AS NVARCHAR(6)), 6);

        DECLARE @ChallanValidUntil DATETIME = DATEADD(DAY, 5, @Today);
        DECLARE @UserGuid UNIQUEIDENTIFIER;
        SELECT @UserGuid = IdGUID FROM dbo.WN_Users WHERE Id = @UserId;
        DECLARE @CreatedByGuid UNIQUEIDENTIFIER = NULL;
        IF @CreatedById IS NOT NULL
            SELECT @CreatedByGuid = IdGUID FROM dbo.WN_Users WHERE Id = @CreatedById;

        -- Insert Booking
        INSERT INTO dbo.WN_Bookings (
            IdGUID, BookingDate, CustomerCode,
            BankAccountId, SecurityDepositAccountId, Notes, RejectReason,
            CreatedOn, UpdatedOn, TransactionDate,
            ChallanNumber, ValidityDate, UserId, SpaceId, PricingId,
            StartOn, EndOn, BookingStatusId, CancelReason, IsDeleted, CreatedById, UpdatedById,
            DiscountPercentage, DiscountAmount, DiscountType, SubtotalAmount, TotalAmount,
            MonthlyRent, BillingPeriodMonths, AdvanceRentMonths, SecurityDepositMonths, SecurityDepositRequired, SecurityDepositPaid
        )
        VALUES (
            NEWID(), SYSUTCDATETIME(), @CustomerCode,
            @RentAccountId, @DepositAccountId, @Notes, NULL,
            SYSUTCDATETIME(), NULL, SYSUTCDATETIME(),
            @ChallanNum, @ChallanValidUntil, @UserId, @SpaceId, @ResolvedPricingId,
            @StartOn, @EndOn, 1, NULL, 0, @CreatedById, NULL,
            @DiscountPercentage, @TotalDiscountAmount, ISNULL(@DiscountType, 'Percentage'), @RentAmount, @TotalAmount,
            @MonthlyRent, @BillPeriodMonths, @AdvMonths, @SecMonths, @SecurityDeposit, 0.00
        );

        DECLARE @BookingId INT = SCOPE_IDENTITY();
        DECLARE @BookingGuid UNIQUEIDENTIFIER;
        SELECT @BookingGuid = IdGUID FROM dbo.WN_Bookings WHERE Id = @BookingId;

        -- Insert BookingDetail snapshot row
        EXEC dbo.WN_BookingDetails_Insert
            @BookingGuid             = @BookingGuid,
            @CustomerCode            = @CustomerCode,
            @CustomerName            = @CustomerFirstName,
            @CustomerEmail           = @TargetEmail,
            @SpaceName               = @SpaceName,
            @SpaceCode               = @SpaceCode,
            @SpaceCategory           = @CategoryCode,
            @StartDateTime           = @StartOn,
            @EndDateTime             = @EndOn,
            @RentAmount              = @DiscountedRent,
            @SecurityDeposit         = @DiscountedDeposit,
            @RentAccountId           = @RentAccountId,
            @DepositAccountId        = @DepositAccountId,
            @PaymentMethod           = NULL,
            @Notes                   = @Notes,
            @SupportChargesId        = @SupportChargesId,
            @AppliedChargePercentage = @AppliedChargePercentage,
            @AppliedTaxPercentage    = @AppliedTaxPercentage;

        -- Insert Challan
        INSERT INTO dbo.WN_Challans
            (BookingId, ChallanNumber, ValidUntil, StatusId, CreatedById)
        VALUES
            (@BookingId, @ChallanNum, @ChallanValidUntil, 1, @CreatedById);

        -- Insert BookingLines
        DECLARE @UnitPrice DECIMAL(18,2);
        IF @OverrideUnitPrice IS NOT NULL AND @OverrideUnitPrice > 0
            SET @UnitPrice = @OverrideUnitPrice;
        ELSE IF @CategoryCode IN ('PrivateOffice', 'Private')
            SET @UnitPrice = @SeatPrice * ISNULL(@Capacity, 1);
        ELSE
            SET @UnitPrice = @SeatPrice;

        DECLARE @LineTaxRate DECIMAL(6,4) = CASE WHEN @DiscountedRent > 0 THEN CAST(ROUND(@TaxAmount / @DiscountedRent, 4) AS DECIMAL(6,4)) ELSE 0 END;

        INSERT INTO dbo.WN_BookingLines
            (BookingId, ChargeTypeId, Description, Quantity, UnitPrice,
             DiscountAmount, TaxRate, AccountId, CreatedById)
        VALUES
            (@BookingId, 1, 'Room Rent', @AdvMonths, @UnitPrice,
             @DiscountOnRent, @LineTaxRate, @RentAccountId, @CreatedById);

        IF @SecurityDeposit > 0
            INSERT INTO dbo.WN_BookingLines
                (BookingId, ChargeTypeId, Description, Quantity, UnitPrice,
                 DiscountAmount, TaxRate, AccountId, CreatedById)
            VALUES
                (@BookingId, 2, 
                 CASE WHEN @SecMonths > 1 
                      THEN 'Security Deposit (' + CAST(@SecMonths AS NVARCHAR(5)) + ' Month(s) Refundable)' 
                      ELSE 'Security Deposit (Refundable)' 
                 END, 
                 @SecMonths, @MonthlyDepositRate,
                 @DiscountOnDeposit, 0, @DepositAccountId, @CreatedById);

        -- Insert Invoice Header
        DECLARE @InvoiceNumber NVARCHAR(50) = 'INV-' + REPLACE(@ChallanNum, 'WN-', '');
        DECLARE @CycleEnd DATETIME = DATEADD(MONTH, @AdvMonths, @StartOn);

        INSERT INTO dbo.WN_Invoices (
            PublicId, InvoiceNumber, UserId, BookingId, MembershipId,
            IssuedOn, DueOn, SubTotal, DiscountTotal, TaxTotal, GrandTotal, PaidTotal,
            CurrencyCode, StatusId, Notes, CreatedOn, CreatedById, InvoiceTypeId,
            AdvanceRentMonths, SecurityDepositMonths, SecurityDepositAmount, AccountsCoaId,
            BillingPeriodMonths, BillingPeriodStart, BillingPeriodEnd
        )
        VALUES (
            NEWID(), @InvoiceNumber, @UserId, @BookingId, NULL,
            @Today, CAST(@ChallanValidUntil AS DATE), @RentAmount, @TotalDiscountAmount, @TaxAmount, @TotalAmount, 0,
            'PKR', 1, @Notes, SYSUTCDATETIME(), @CreatedById, 1,
            @AdvMonths, @SecMonths, ISNULL(@DiscountedDeposit, 0), @RentAccountId,
            @BillPeriodMonths, @StartOn, @CycleEnd
        );

        DECLARE @InvoiceId INT = SCOPE_IDENTITY();

        -- Insert Invoice Lines
        INSERT INTO dbo.WN_InvoiceLines (
            InvoiceId, ChargeTypeId, Description, Quantity, UnitPrice, DiscountAmount, TaxRate, AccountId, SortOrder
        )
        VALUES (
            @InvoiceId, 1, 'Room Rent', @AdvMonths, @UnitPrice, @DiscountOnRent, @LineTaxRate, @RentAccountId, 1
        );

        IF @SecurityDeposit > 0
            INSERT INTO dbo.WN_InvoiceLines (
                InvoiceId, ChargeTypeId, Description, Quantity, UnitPrice, DiscountAmount, TaxRate, AccountId, SortOrder
            )
            VALUES (
                @InvoiceId, 2, 
                CASE WHEN @SecMonths > 1 
                     THEN 'Security Deposit (' + CAST(@SecMonths AS NVARCHAR(5)) + ' Month(s) Refundable)' 
                     ELSE 'Security Deposit (Refundable)' 
                END, 
                @SecMonths, @MonthlyDepositRate, @DiscountOnDeposit, 0, @DepositAccountId, 2
            );

        -- Auto-generate Access Cards for this booking (Capacity + 1)
        EXEC dbo.WN_AccessCards_PopulateAllBookings @TargetBookingId = @BookingId;

        COMMIT TRANSACTION;

        SELECT 
            @BookingId AS BookingId,
            @BookingGuid AS BookingPublicId,
            @ChallanNum AS ChallanNumber,
            @ChallanValidUntil AS ChallanValidUntil,
            @RentAmount AS SubtotalAmount,
            @TaxAmount AS TaxAmount,
            @TotalAmount AS TotalAmount,
            NULL AS ErrorMessage;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
        SELECT NULL AS BookingId, NULL AS BookingPublicId,
               NULL AS ChallanNumber, NULL AS ChallanValidUntil,
               NULL AS SubtotalAmount, NULL AS TaxAmount, NULL AS TotalAmount,
               @ErrMsg AS ErrorMessage;
    END CATCH
END;";
                await using var cmd = new SqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        private async Task EnsureInvoiceProceduresUpdatedAsync(SqlConnection c)
        {
            try
            {
                string sql = @"
CREATE OR ALTER PROCEDURE dbo.WN_GetStatementInvoicePdfData
    @InvoiceId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Result 1: Header / Vendor / Customer / Center Info
    SELECT TOP 1
        i.Id,
        i.InvoiceNumber,
        i.UserId,
        i.BookingId,
        i.IssuedOn,
        i.DueOn,
        COALESCE(i.BillingPeriodStart, b.StartOn, i.IssuedOn) AS BillingPeriodStart,
        COALESCE(i.BillingPeriodEnd, b.EndOn, i.DueOn) AS BillingPeriodEnd,
        ISNULL(i.GrandTotal, 0) AS GrandTotal,
        ISNULL(i.PaidTotal, 0) AS PaidTotal,
        ISNULL(i.CurrencyCode, 'PKR') AS CurrencyCode,
        COALESCE(NULLIF(LTRIM(RTRIM(c.Company)), ''), NULLIF(LTRIM(RTRIM(ucomp.CompanyName)), ''), '-') AS AccountName,
        ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), u.Name) AS AttnName,
        COALESCE(c.Address, u.Address, '') AS BillingAddress,
        ISNULL(c.Code, 'WN' + RIGHT('00000' + CAST(ISNULL(c.Id, i.UserId) AS VARCHAR(10)), 5)) AS AccountNumber,
        ISNULL(c.CnicOrPassport, '') AS SntnNtnNic,
        ISNULL(loc.Name, 'WorkNest') AS CenterName,
        ISNULL(comp.CompanyName, 'WorkNest Coworking Spaces (Pvt) Ltd') AS VendorLegalName,
        ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(comp.AddressLine1, '') + ' ' + ISNULL(comp.AddressLine2, ''))), ''), ISNULL(loc.Address, '3rd Floor EOBI Building-II, I-8 Markaz, Islamabad')) AS VendorAddress,
        ISNULL(comp.Contact, '+92 309 9771774 / +92 308 0256000') AS VendorPhone,
        ISNULL(comp.Fax, '+92 51 8439201') AS VendorFax,
        ISNULL(comp.NTN, '7492018-3') AS VendorNtn,
        ISNULL(bd.AppliedChargePercentage, 10.00) AS AppliedChargePercentage,
        ISNULL(bd.AppliedTaxPercentage, 16.00) AS AppliedTaxPercentage,
        ISNULL(bd.SupportChargeAmount, 0.00) AS SupportChargeAmount,
        COALESCE(NULLIF(b.SecurityDepositRequired, 0), NULLIF(i.SecurityDepositAmount, 0), ISNULL(bd.SecurityDeposit, 0)) AS SecurityDepositAmount
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON (c.UserId = i.UserId OR c.Id = i.UserId OR (u.Email IS NOT NULL AND c.Email = u.Email)) AND (c.IsActive = 1 OR c.IsActive IS NULL)
    LEFT JOIN dbo.Company ucomp WITH (NOLOCK) ON ucomp.Id = u.CompanyId
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.BookingGuid = b.IdGUID
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    LEFT JOIN dbo.WN_Locations loc WITH (NOLOCK) ON loc.Id = s.LocationId
    LEFT JOIN dbo.Company comp WITH (NOLOCK) ON comp.Id = ISNULL(NULLIF(loc.CompanyId, 0), 486)
    WHERE i.Id = @InvoiceId;

    -- Result 2: Line Items
    SELECT 
        l.ChargeTypeId,
        l.Description,
        (l.Quantity * l.UnitPrice - l.DiscountAmount) AS PriceExclVat,
        l.TaxAmount AS VatAmount,
        l.LineTotal AS TotalInclVat,
        l.TaxRate,
        ISNULL(ct.Label, 'Business Support Services') AS CategoryName
    FROM dbo.WN_InvoiceLines l WITH (NOLOCK)
    LEFT JOIN dbo.WN_ChargeTypes ct WITH (NOLOCK) ON ct.Id = l.ChargeTypeId
    WHERE l.InvoiceId = @InvoiceId
    ORDER BY l.SortOrder, l.Id;

    -- Result 3: Prior Balances
    DECLARE @UserId INT;
    SELECT @UserId = UserId FROM dbo.WN_Invoices WHERE Id = @InvoiceId;

    SELECT 
        ISNULL(SUM(GrandTotal - PaidTotal), 0) AS PriorBalance,
        ISNULL(SUM(PaidTotal), 0) AS PaymentReceived
    FROM dbo.WN_Invoices WITH (NOLOCK)
    WHERE UserId = @UserId AND Id < @InvoiceId;

    -- Result 4: Bank Details
    SELECT TOP 1 Description AS BankName, ShortDesc AS BankAccountNumber
    FROM dbo.AccountsCOA WITH (NOLOCK)
    WHERE AccountNature = 'Bank' OR Description LIKE '%Bank%';
END;";
                await using var cmd = new SqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetSpaceStatusForConfigAsync(int configId)
        {
            await using var c = await Open();
            await EnsureSpaceConfigSpUpdatedAsync(c);
            await using var cmd = SP("dbo.WN_SpaceConfig_GetSpaceStatus", c);
            cmd.Parameters.AddWithValue("@ConfigId", configId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<(List<string> Deleted, List<string> Blocked)> DeleteSpacesFromConfigAsync(int configId, string? spaceGuids)
        {
            var deleted = new List<string>();
            var blocked = new List<string>();

            await using var c = await Open();

            int locationId = 0;
            int spaceTypeId = 0;

            await using (var getCfg = new SqlCommand(
                "SELECT LocationId, SpaceTypeId FROM dbo.WN_SpaceConfig WHERE Id = @ConfigId", c))
            {
                getCfg.Parameters.AddWithValue("@ConfigId", configId);
                await using var rCfg = await getCfg.ExecuteReaderAsync();
                if (await rCfg.ReadAsync())
                {
                    locationId = rCfg["LocationId"] != DBNull.Value ? Convert.ToInt32(rCfg["LocationId"]) : 0;
                    spaceTypeId = rCfg["SpaceTypeId"] != DBNull.Value ? Convert.ToInt32(rCfg["SpaceTypeId"]) : 0;
                }
            }

            if (locationId == 0 && spaceTypeId == 0) return (deleted, blocked);

            var specifiedGuids = string.IsNullOrWhiteSpace(spaceGuids)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : spaceGuids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var spaces = new List<(int Id, string Code, string IdGuid, string PublicId)>();

            await using (var getSpaces = new SqlCommand(
                "SELECT s.Id, s.Code, CAST(ISNULL(s.IdGUID, s.Id) AS NVARCHAR(50)) AS IdGuidStr, CAST(ISNULL(s.IdGUID, s.Id) AS NVARCHAR(50)) AS PublicIdStr " +
                "FROM dbo.WN_Spaces s " +
                "WHERE s.LocationId = @L AND s.SpaceTypeId = @ST AND (s.IsActive IS NULL OR s.IsActive = 1)", c))
            {
                getSpaces.Parameters.AddWithValue("@L", locationId);
                getSpaces.Parameters.AddWithValue("@ST", spaceTypeId);
                await using var rSp = await getSpaces.ExecuteReaderAsync();
                while (await rSp.ReadAsync())
                {
                    var id = Convert.ToInt32(rSp["Id"]);
                    var code = rSp["Code"]?.ToString() ?? string.Empty;
                    var idGuid = rSp["IdGuidStr"]?.ToString() ?? string.Empty;
                    var pid = rSp["PublicIdStr"]?.ToString() ?? string.Empty;
                    if (specifiedGuids.Count == 0 || specifiedGuids.Contains(idGuid) || specifiedGuids.Contains(pid) || specifiedGuids.Contains(id.ToString()))
                    {
                        spaces.Add((id, code, idGuid, pid));
                    }
                }
            }

            foreach (var sp in spaces)
            {
                bool hasActiveBookings = false;
                await using (var checkBk = new SqlCommand(
                    "SELECT TOP 1 1 FROM dbo.WN_Bookings " +
                    "WHERE SpaceId = @SpaceId AND IsDeleted = 0 AND BookingStatusId IN (1, 2) AND EndOn > SYSUTCDATETIME()", c))
                {
                    checkBk.Parameters.AddWithValue("@SpaceId", sp.Id);
                    await using var rBk = await checkBk.ExecuteReaderAsync();
                    hasActiveBookings = await rBk.ReadAsync();
                }

                if (hasActiveBookings)
                {
                    blocked.Add(sp.Code);
                }
                else
                {
                    await using (var delCmd = new SqlCommand(
                        "UPDATE dbo.WN_Spaces SET IsActive = 0, Status = 0 WHERE Id = @SpaceId", c))
                    {
                        delCmd.Parameters.AddWithValue("@SpaceId", sp.Id);
                        await delCmd.ExecuteNonQueryAsync();
                    }
                    deleted.Add(sp.Code);
                }
            }

            return (deleted, blocked);
        }

        // --- Amenity ---

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllAmenitiesAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Amenities_GetList", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<int?> InsertAmenityAsync(string name, string? icon)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Amenities_Insert", c);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Icon", (object?)icon ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        // --- Gallery ---

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllGalleryImagesAsync(int? locationId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_GalleryImages_GetAll", c);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetGalleryImagesAsync(int page, int limit, int? locationId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_GalleryImages_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            int total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) ? Convert.ToInt32(t) : rows.Count;
            return (rows, total);
        }

        public async Task<(int? Id, string? PublicId)> InsertGalleryImageAsync(int? locationId, int? spaceId, string? title, string? description, string imageUrl, int sortOrder, int? createdById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_GalleryImages_Insert", c);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SpaceId", (object?)spaceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Title", (object?)title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", imageUrl);
            cmd.Parameters.AddWithValue("@SortOrder", sortOrder);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return (row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null,
                        row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
            }
            return (null, null);
        }

        public async Task UpdateGalleryImageAsync(int id, string? title, string? description, string? imageUrl, int? sortOrder)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_GalleryImages_Update", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Title", (object?)title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)imageUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SortOrder", (object?)sortOrder ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteGalleryImageAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_GalleryImages_Delete", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- Contact ---

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetContactsAsync(int page, int limit, string? search)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Contacts_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            int total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) ? Convert.ToInt32(t) : rows.Count;
            return (rows, total);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetRecentContactsAsync(int top)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Contacts_GetRecent", c);
            cmd.Parameters.AddWithValue("@Top", top);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<(int? Id, string? PublicId)> InsertContactAsync(string contactType, int? userId, string name, string email, string? phone, string? message)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Contacts_Insert", c);
            cmd.Parameters.AddWithValue("@ContactType", contactType);
            cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Message", (object?)message ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return (row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null,
                                row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
            }
            return (null, null);
        }

        public async Task UpdateContactStatusAsync(int id, byte statusId, int? updatedById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Contacts_UpdateStatus", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@StatusId", statusId);
            cmd.Parameters.AddWithValue("@UpdatedById", (object?)updatedById ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteContactAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Contacts_Delete", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- Dashboard ---

        public async Task<IEnumerable<IEnumerable<IDictionary<string, object?>>>> GetDashboardSummaryAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Dashboard_GetSummary", c);
            await using var r = await cmd.ExecuteReaderAsync();
            var results = new List<List<IDictionary<string, object?>>>();
            do { results.Add(await ReadAll(r)); } while (await r.NextResultAsync());
            return results;
        }

        // --- AccountCOA ---

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllAccountsCoaAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_AccountsCOA_GetList", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IDictionary<string, object?>?> GetAccountCoaByIdAsync(int accountId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_AccountsCOA_GetById", c);
            cmd.Parameters.AddWithValue("@AccountId", accountId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        // --- AmountFields ---

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllAmountFieldsAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_AmountFields_GetList", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task UpdateAmountFieldAccountAsync(int id, int? accountId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_AmountFields_UpdateAccount", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@AccountId", (object?)accountId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- Customer ---

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllCustomersAsync(int page, int limit, string? search)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Customers_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> SearchCustomersAsync(string query)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Customers_Search", c);
            cmd.Parameters.AddWithValue("@Query", query);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IDictionary<string, object?>?> GetCustomerByGuidAsync(string guid)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Customers_GetByGuid", c);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<IDictionary<string, object?>> CreateCustomerAsync(string firstName, string? lastName, string email, string? phone, string? cnic, string? address, int? cityId, string? notes, string? createdBy, int? userId = null, string? company = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Customers_Insert", c);
            cmd.Parameters.AddWithValue("@FirstName", firstName);
            cmd.Parameters.AddWithValue("@LastName", (object?)lastName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CnicOrPassport", (object?)cnic ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CityId", (object?)cityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedBy", (object?)createdBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Company", (object?)company ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) return ToDict(r);
            return new Dictionary<string, object?>();
        }

        public async Task UpdateCustomerAsync(string guid, string? firstName, string? lastName, string? email, string? phone, string? cnic, string? address, int? cityId, string? notes, bool? isActive, string? company = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Customers_Update", c);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            cmd.Parameters.AddWithValue("@FirstName", (object?)firstName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LastName", (object?)lastName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Email", (object?)email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CnicOrPassport", (object?)cnic ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CityId", (object?)cityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", isActive.HasValue ? (object)(isActive.Value ? 1 : 0) : DBNull.Value);
            cmd.Parameters.AddWithValue("@Company", (object?)company ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteCustomerAsync(string guid)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Customers_Delete", c);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IDictionary<string, object?>?> GetCustomerByUserIdAsync(int userId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Customers_GetByUserId", c);
            cmd.Parameters.AddWithValue("@UserId", userId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<IDictionary<string, object?>?> GetCustomerByEmailAsync(string email)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Customers_GetByEmail", c);
            cmd.Parameters.AddWithValue("@Email", email);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<IDictionary<string, object?>?> GetActivePricingForSpaceAsync(int spaceId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_SpacePricing_GetActive", c);
            cmd.Parameters.AddWithValue("@SpaceId", spaceId);
            cmd.Parameters.AddWithValue("@BillingPeriodId", DBNull.Value);
            cmd.Parameters.AddWithValue("@TierTypeId", 1);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<(IDictionary<string, object?>? Header, IEnumerable<IDictionary<string, object?>> Lines)> GetBookingDetailsAsync(string bookingIdentifier, string? userEmail)
        {
            await using var c = await Open();
            await EnsureBookingSummaryViewUpdatedAsync(c);
            await using var cmd = SP("dbo.WN_BookingDetails_GetByBooking", c);
            cmd.Parameters.AddWithValue("@BookingIdentifier", bookingIdentifier);
            cmd.Parameters.AddWithValue("@UserEmail", (object?)userEmail ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();

            IDictionary<string, object?>? header = null;
            if (await r.ReadAsync())
                header = ToDict(r);

            var lines = new List<IDictionary<string, object?>>();
            if (await r.NextResultAsync())
                lines = await ReadAll(r);

            return (header, lines);
        }

        public async Task<(IDictionary<string, object?>? ChallanHeader, IEnumerable<IDictionary<string, object?>> Lines)> GetChallanWithDetailsAsync(int bookingId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Challans_GetFullByBooking", c);
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            await using var r = await cmd.ExecuteReaderAsync();

            // Result Set 1: Challan + Booking header (take first row)
            IDictionary<string, object?>? header = null;
            if (await r.ReadAsync())
                header = ToDict(r);

            // Result Set 2: BookingLines breakdown
            var lines = new List<IDictionary<string, object?>>();
            if (await r.NextResultAsync())
                lines = await ReadAll(r);

            return (header, lines);
        }

        public async Task ExecuteRawSqlAsync(string sql)
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand(sql, c);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- AccessCard ---
        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetAccessCardsAsync(int page, int limit, string? search, int? bookingId, int? customerId, int? spaceId, int? status)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_AccessCards_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page < 1 ? 1 : page);
            cmd.Parameters.AddWithValue("@Limit", limit < 1 ? 20 : limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BookingId", (object?)bookingId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerId", (object?)customerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SpaceId", (object?)spaceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            int total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) ? Convert.ToInt32(t) : rows.Count;
            return (rows, total);
        }

        public async Task<IDictionary<string, object?>?> GetAccessCardByIdAsync(string id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_AccessCards_GetById", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<IDictionary<string, object?>> CreateAccessCardAsync(int locationId, int customerId, int bookingId, int spaceId, string? cardNumber, DateTime startDate, DateTime endDate, int status, int? createdById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_AccessCards_Insert", c);
            cmd.Parameters.AddWithValue("@LocationId", locationId);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            cmd.Parameters.AddWithValue("@SpaceId", spaceId);
            cmd.Parameters.AddWithValue("@CardNumber", (object?)cardNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@StartDate", startDate);
            cmd.Parameters.AddWithValue("@EndDate", endDate);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : new Dictionary<string, object?>();
        }

        public async Task UpdateAccessCardAsync(string id, int? locationId, int? customerId, int? bookingId, int? spaceId, string? cardNumber, DateTime? startDate, DateTime? endDate, int? status, int? updatedById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_AccessCards_Update", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerId", (object?)customerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BookingId", (object?)bookingId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SpaceId", (object?)spaceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CardNumber", (object?)cardNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@StartDate", (object?)startDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@EndDate", (object?)endDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedById", (object?)updatedById ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteAccessCardAsync(string id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_AccessCards_Delete", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task GenerateAccessCardsForBookingDbAsync(int bookingId, int? createdById = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_AccessCards_PopulateAllBookings", c);
            cmd.Parameters.AddWithValue("@TargetBookingId", bookingId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

