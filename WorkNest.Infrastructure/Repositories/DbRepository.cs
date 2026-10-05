
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using WorkNest.Application.Interfaces;
using WorkNest.Application.DTOs.SpaceConfig;
using WorkNest.Application.DTOs.Payment;
using WorkNest.Application.DTOs.Announcement;
using WorkNest.Application.DTOs.HikDevice;

using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace WorkNest.Infrastructure.Repositories
{
    public class DbRepository : IDbRepository
    {
        private readonly string _connectionString;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public DbRepository(IConfiguration configuration, IHttpContextAccessor? httpContextAccessor = null)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _httpContextAccessor = httpContextAccessor;
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
            await using (var cmd = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", c))
            {
                await cmd.ExecuteNonQueryAsync();
            }

            try
            {
                var httpContext = _httpContextAccessor?.HttpContext;
                string? userIdOrGuid = null;
                string? userEmail = null;

                if (httpContext != null)
                {
                    var user = httpContext.User;
                    if (user != null)
                    {
                        userIdOrGuid = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                    ?? user.FindFirst("sub")?.Value
                                    ?? user.FindFirst("UserId")?.Value
                                    ?? user.FindFirst("id")?.Value;

                        userEmail = user.FindFirst(ClaimTypes.Email)?.Value
                                 ?? user.FindFirst("email")?.Value
                                 ?? user.Identity?.Name;
                    }

                    if (httpContext.Request?.Headers != null)
                    {
                        if (string.IsNullOrWhiteSpace(userEmail))
                        {
                            if (httpContext.Request.Headers.TryGetValue("x-user-email", out var headerEmail) && !string.IsNullOrWhiteSpace(headerEmail))
                            {
                                userEmail = headerEmail.ToString();
                            }
                            else if (httpContext.Request.Headers.TryGetValue("X-User-Email", out var headerEmail2) && !string.IsNullOrWhiteSpace(headerEmail2))
                            {
                                userEmail = headerEmail2.ToString();
                            }
                        }

                        if (string.IsNullOrWhiteSpace(userIdOrGuid))
                        {
                            if (httpContext.Request.Headers.TryGetValue("x-user-id", out var headerUid) && !string.IsNullOrWhiteSpace(headerUid))
                            {
                                userIdOrGuid = headerUid.ToString();
                            }
                            else if (httpContext.Request.Headers.TryGetValue("X-User-Id", out var headerUid2) && !string.IsNullOrWhiteSpace(headerUid2))
                            {
                                userIdOrGuid = headerUid2.ToString();
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(userIdOrGuid) || !string.IsNullOrWhiteSpace(userEmail))
                {
                    await using var setCtx = new SqlCommand(@"
                        DECLARE @ResolvedId INT = NULL;
                        
                        IF @UserIdOrGuid IS NOT NULL AND ISNUMERIC(@UserIdOrGuid) = 1
                        BEGIN
                            SET @ResolvedId = CAST(@UserIdOrGuid AS INT);
                        END
                        
                        IF @ResolvedId IS NULL
                        BEGIN
                            SELECT TOP 1 @ResolvedId = Id 
                            FROM dbo.WN_Users WITH (NOLOCK)
                            WHERE (CAST(IdGUID AS NVARCHAR(64)) = @UserIdOrGuid OR Email = @UserIdOrGuid OR Email = @UserEmail);
                        END

                        IF @ResolvedId IS NOT NULL
                        BEGIN
                            EXEC sp_set_session_context @key = N'AppUserId', @value = @ResolvedId;
                        END

                        IF @UserEmail IS NOT NULL
                        BEGIN
                            EXEC sp_set_session_context @key = N'AppUserEmail', @value = @UserEmail;
                        END

                        EXEC sp_set_session_context @key = N'SourceApp', @value = N'WebAPI';", c);

                    setCtx.Parameters.AddWithValue("@UserIdOrGuid", (object?)userIdOrGuid ?? DBNull.Value);
                    setCtx.Parameters.AddWithValue("@UserEmail", (object?)userEmail ?? DBNull.Value);
                    await setCtx.ExecuteNonQueryAsync();
                }
            }
            catch
            {
                // Silently swallow session context setup issues to ensure database connectivity is preserved
            }

            return c;
        }

        // --- User ---

        public async Task<(int? Id, string? PublicId)> SyncUserAsync(string email, string? name, string? phone, string? passwordHash = null, int? roleId = null, int? companyId = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Users_GetByEmail", c);
            cmd.Parameters.AddWithValue("@Email", email);
            await using var r = await cmd.ExecuteReaderAsync();

            var resolvedName = !string.IsNullOrWhiteSpace(name)
                ? name.Trim()
                : (!string.IsNullOrWhiteSpace(email) ? email.Split('@')[0] : "User");
            var userName = resolvedName;
            var finalRoleId = roleId ?? WorkNest.Common.Constants.Roles.GeneralId;

            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                var id = row.TryGetValue("Id", out var eid) ? Convert.ToInt32(eid) : (int?)null;
                var pub = (row.TryGetValue("IdGUID", out var idg) && idg is not null) ? idg.ToString() : (row.TryGetValue("PublicId", out var eg) ? eg?.ToString() : null);
                await r.CloseAsync();

                var existingName = row.TryGetValue("Name", out var exN) ? exN?.ToString() : null;
                var existingUserName = row.TryGetValue("UserName", out var exUN) ? exUN?.ToString() : null;
                var existingRoleId = row.TryGetValue("RoleId", out var exR) && exR is not null ? Convert.ToInt32(exR) : (int?)null;
                var existingCompanyId = row.TryGetValue("CompanyId", out var exC) && exC is not null ? Convert.ToInt32(exC) : (int?)null;

                var nameToSet = !string.IsNullOrWhiteSpace(existingName) ? existingName : resolvedName;
                var userNameToSet = !string.IsNullOrWhiteSpace(existingUserName) ? existingUserName : userName;
                var roleToSet = existingRoleId ?? finalRoleId;
                var companyToSet = companyId ?? existingCompanyId;

                await using var upd = SP("dbo.WN_Users_Update", c);
                upd.Parameters.AddWithValue("@Id", id);
                upd.Parameters.AddWithValue("@Name", (object?)nameToSet ?? DBNull.Value);
                upd.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
                upd.Parameters.AddWithValue("@CompanyId", (object?)companyToSet ?? DBNull.Value);
                upd.Parameters.AddWithValue("@CityId", DBNull.Value);
                upd.Parameters.AddWithValue("@Address", DBNull.Value);
                upd.Parameters.AddWithValue("@CnicOrPassport", DBNull.Value);
                upd.Parameters.AddWithValue("@AvatarUrl", DBNull.Value);
                upd.Parameters.AddWithValue("@Notes", DBNull.Value);
                await upd.ExecuteNonQueryAsync();

                await using var rawCmd = new SqlCommand("UPDATE dbo.WN_Users SET RoleId = ISNULL(RoleId, @RoleId), UserName = ISNULL(UserName, @UserName), CompanyId = COALESCE(@CompanyId, CompanyId) WHERE Id = @Id", c);
                rawCmd.Parameters.AddWithValue("@RoleId", (object?)roleToSet ?? DBNull.Value);
                rawCmd.Parameters.AddWithValue("@UserName", (object?)userNameToSet ?? DBNull.Value);
                rawCmd.Parameters.AddWithValue("@CompanyId", (object?)companyToSet ?? DBNull.Value);
                rawCmd.Parameters.AddWithValue("@Id", id);
                await rawCmd.ExecuteNonQueryAsync();

                return (id, pub);
            }
            await r.CloseAsync();

            await using var ins = SP("dbo.WN_Users_Insert", c);
            ins.Parameters.AddWithValue("@Email", email);
            ins.Parameters.AddWithValue("@PasswordHash", (object?)passwordHash ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Name", (object?)resolvedName ?? DBNull.Value);
            ins.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
            ins.Parameters.AddWithValue("@RoleId", (object?)finalRoleId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@CompanyId", (object?)companyId ?? DBNull.Value);
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
                var insertedId = row.TryGetValue("Id", out var nid) ? Convert.ToInt32(nid) : (int?)null;
                var insertedPub = (row.TryGetValue("IdGUID", out var idg) && idg is not null) ? idg.ToString() : (row.TryGetValue("PublicId", out var ng) ? ng?.ToString() : null);
                await ir.CloseAsync();

                if (insertedId.HasValue)
                {
                    await using var rawCmd = new SqlCommand("UPDATE dbo.WN_Users SET UserName = ISNULL(UserName, @UserName), RoleId = ISNULL(RoleId, @RoleId), CompanyId = COALESCE(@CompanyId, CompanyId) WHERE Id = @Id", c);
                    rawCmd.Parameters.AddWithValue("@UserName", (object?)userName ?? DBNull.Value);
                    rawCmd.Parameters.AddWithValue("@RoleId", (object?)finalRoleId ?? DBNull.Value);
                    rawCmd.Parameters.AddWithValue("@CompanyId", (object?)companyId ?? DBNull.Value);
                    rawCmd.Parameters.AddWithValue("@Id", insertedId.Value);
                    await rawCmd.ExecuteNonQueryAsync();
                }

                return (insertedId, insertedPub);
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
            int? billingPeriodMonths = null,
            decimal? perSeatBasePrice = null,
            int? capacity = null,
            decimal? monthlyBasePrice = null,
            decimal? maxDiscountPercent = null,
            int? securityDepositMonths = null,
            decimal? securityDeposit = null,
            string? offeringType = null,
            decimal? withholdingTaxRate = null)
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

            // Persist new breakdown and cap fields after insert
            if (result.TryGetValue("Id", out var qidObj) && qidObj is not null)
            {
                int quotationId = Convert.ToInt32(qidObj);

                decimal discountAmount = (discountType == "Amount" || discountType == "Fixed")
                    ? discountValue
                    : subtotalAmount * (discountValue / 100m);

                decimal secDeposit = securityDeposit.HasValue
                    ? securityDeposit.Value
                    : (securityDepositOverride ?? 0);

                var updateParts = new List<string> {
                    "DiscountType = @DT",
                    "DiscountAmount = @DA",
                    "SecurityDeposit = @SD"
                };
                if (floorId.HasValue) updateParts.Add("FloorId = @FID");
                if (billingPeriodMonths.HasValue) updateParts.Add("BillingPeriodMonths = @BPM");
                if (securityDepositMonths.HasValue) updateParts.Add("SecurityDepositMonths = @SDM");
                if (perSeatBasePrice.HasValue) updateParts.Add("PerSeatBasePrice = @PSBP");
                if (capacity.HasValue) updateParts.Add("Capacity = @CAP");
                if (monthlyBasePrice.HasValue) updateParts.Add("MonthlyBasePrice = @MBP");
                if (maxDiscountPercent.HasValue) updateParts.Add("MaxDiscountPercent = @MDP");
                if (!string.IsNullOrWhiteSpace(offeringType)) updateParts.Add("OfferingType = @OT");
                if (withholdingTaxRate.HasValue) updateParts.Add("WithholdingTaxRate = @WTR");

                var updateSql = $"UPDATE dbo.WN_Quotations SET {string.Join(", ", updateParts)} WHERE Id = @QID";
                await using var upd = new SqlCommand(updateSql, c);
                upd.Parameters.AddWithValue("@DT", discountType);
                upd.Parameters.AddWithValue("@DA", discountAmount);
                upd.Parameters.AddWithValue("@SD", secDeposit);
                upd.Parameters.AddWithValue("@QID", quotationId);
                if (floorId.HasValue) upd.Parameters.AddWithValue("@FID", floorId.Value);
                if (billingPeriodMonths.HasValue) upd.Parameters.AddWithValue("@BPM", billingPeriodMonths.Value);
                if (securityDepositMonths.HasValue) upd.Parameters.AddWithValue("@SDM", securityDepositMonths.Value);
                if (perSeatBasePrice.HasValue) upd.Parameters.AddWithValue("@PSBP", perSeatBasePrice.Value);
                if (capacity.HasValue) upd.Parameters.AddWithValue("@CAP", capacity.Value);
                if (monthlyBasePrice.HasValue) upd.Parameters.AddWithValue("@MBP", monthlyBasePrice.Value);
                if (maxDiscountPercent.HasValue) upd.Parameters.AddWithValue("@MDP", maxDiscountPercent.Value);
                if (!string.IsNullOrWhiteSpace(offeringType)) upd.Parameters.AddWithValue("@OT", offeringType);
                if (withholdingTaxRate.HasValue) upd.Parameters.AddWithValue("@WTR", withholdingTaxRate.Value);

                await upd.ExecuteNonQueryAsync();

                result["DiscountType"] = discountType;
                result["DiscountAmount"] = discountAmount;
                result["SecurityDeposit"] = secDeposit;
                if (billingPeriodMonths.HasValue) result["BillingPeriodMonths"] = billingPeriodMonths.Value;
                if (securityDepositMonths.HasValue) result["SecurityDepositMonths"] = securityDepositMonths.Value;
                if (perSeatBasePrice.HasValue) result["PerSeatBasePrice"] = perSeatBasePrice.Value;
                if (capacity.HasValue) result["Capacity"] = capacity.Value;
                if (monthlyBasePrice.HasValue) result["MonthlyBasePrice"] = monthlyBasePrice.Value;
                if (maxDiscountPercent.HasValue) result["MaxDiscountPercent"] = maxDiscountPercent.Value;
                if (!string.IsNullOrWhiteSpace(offeringType)) result["OfferingType"] = offeringType;
                if (withholdingTaxRate.HasValue) result["WithholdingTaxRate"] = withholdingTaxRate.Value;
            }

            return result;
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetOfferingTypesAsync(bool? activeOnly = null)
        {
            await using var c = await Open();
            string sql = "SELECT Id, Description, DiscountCap, ISNULL(Status, 1) AS Status FROM dbo.WN_OfferingType";
            if (activeOnly.HasValue)
            {
                sql += activeOnly.Value ? " WHERE ISNULL(Status, 1) = 1" : " WHERE ISNULL(Status, 1) = 0";
            }
            sql += " ORDER BY Id ASC";
            await using var cmd = new SqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
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
            string? search,
            int? locationId = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Quotations_GetList", c);

            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);

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
            await using var cmd = SP("dbo.WN_Quotations_ConvertToBooking", c);

            cmd.Parameters.AddWithValue("@QuotationId", quotationId);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();

            if (await r.ReadAsync())
                return ToDict(r);

            return new Dictionary<string, object?>();
        }



        public async Task<IDictionary<string, object?>> AcceptQuotationAsync(int quotationId, int version, int customerId, string? note, int? userId)
        {
            await using var c = await Open();

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

            string whereClause = quotationId.HasValue ? $"WHERE QuotationId = {quotationId.Value}" : "";
            string sql = $"SELECT TOP ({limit}) * FROM dbo.WN_QuotationActivities {whereClause} ORDER BY CreatedDate DESC";
            await using var cmd = new SqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task SendQuotationStatusAsync(int quotationId, string status, int? userId)
        {
            await using var c = await Open();

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

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetUsersAsync(int page, int limit, string? search, int? locationId = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Users_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
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

        public async Task<(int? Id, string? PublicId)> CreateUserAsync(string email, string? passwordHash, string? name, string? phone, int? roleId, int? companyId, int? cityId, string? address, string? cnic, string? avatarUrl, string? notes, int? createdById, int? locationId = null)
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
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return (row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null,
                        (row.TryGetValue("IdGUID", out var idg) && idg is not null) ? idg.ToString() : (row.TryGetValue("PublicId", out var g) ? g?.ToString() : null));
            }
            return (null, null);
        }

        public async Task UpdateUserAsync(int id, string? name, string? phone, int? companyId, int? cityId, string? address, string? cnic, string? avatarUrl, string? notes, int? locationId = null)
        {
            await using var c = await Open();
            try
            {
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
                cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Message.Contains("@LocationId") || ex.Message.Contains("too many arguments"))
            {
                await using var fallback = SP("dbo.WN_Users_Update", c);
                fallback.Parameters.AddWithValue("@Id", id);
                fallback.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
                fallback.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
                fallback.Parameters.AddWithValue("@CompanyId", (object?)companyId ?? DBNull.Value);
                fallback.Parameters.AddWithValue("@CityId", (object?)cityId ?? DBNull.Value);
                fallback.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);
                fallback.Parameters.AddWithValue("@CnicOrPassport", (object?)cnic ?? DBNull.Value);
                fallback.Parameters.AddWithValue("@AvatarUrl", (object?)avatarUrl ?? DBNull.Value);
                fallback.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
                await fallback.ExecuteNonQueryAsync();
            }
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

        public async Task SetUserRoleAndLocationAsync(int id, int roleId, int? locationId)
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand("UPDATE dbo.WN_Users SET RoleId = @RoleId, LocationId = @LocationId WHERE Id = @Id", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@RoleId", roleId);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- Space ---

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetSpacesAsync(int page, int limit, string? search)
        {
            await using var c = await Open();
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

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesAsync(string? shiftType = "24_7")
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Spaces_GetAvailable", c);
            cmd.Parameters.AddWithValue("@ShiftType", (object?)shiftType ?? "24_7");
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesByTypeAsync(int spaceTypeId, DateTime startOn, DateTime endOn, string? shiftType = "24_7")
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_GetAvailableSpaces", c);
            cmd.Parameters.AddWithValue("@SpaceTypeId", spaceTypeId);
            cmd.Parameters.AddWithValue("@StartOn", startOn);
            cmd.Parameters.AddWithValue("@EndOn", endOn);
            cmd.Parameters.AddWithValue("@Capacity", DBNull.Value);
            cmd.Parameters.AddWithValue("@ShiftType", (object?)shiftType ?? "24_7");
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailabilityCountsAsync(string? shiftType = "24_7")
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Spaces_GetAvailabilityCounts", c);
            cmd.Parameters.AddWithValue("@ShiftType", (object?)shiftType ?? "24_7");
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



        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetBookingsAsync(int page, int limit, string? search, int? locationId = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            int total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) ? Convert.ToInt32(t) : rows.Count;
            return (rows, total);
        }

        public async Task<IDictionary<string, object?>?> GetBookingByPublicIdAsync(Guid publicId, string? userEmail = null)
        {
            await using var c = await Open();
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

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesForBookingAsync(int spaceTypeId, DateTime startOn, DateTime endOn, int? capacity = null, string? shiftType = "24_7")
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_GetAvailableSpaces", c);
            cmd.Parameters.AddWithValue("@SpaceTypeId", spaceTypeId);
            cmd.Parameters.AddWithValue("@StartOn", startOn);
            cmd.Parameters.AddWithValue("@EndOn", endOn);
            cmd.Parameters.AddWithValue("@Capacity", (object?)capacity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ShiftType", (object?)shiftType ?? "24_7");
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesForReassignmentAsync(int spaceTypeId, DateTime startOn, DateTime endOn, int excludeBookingId, string? shiftType = "24_7")
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_GetAvailableSpacesForReassignment", c);
            cmd.Parameters.AddWithValue("@SpaceTypeId", spaceTypeId);
            cmd.Parameters.AddWithValue("@StartOn", startOn);
            cmd.Parameters.AddWithValue("@EndOn", endOn);
            cmd.Parameters.AddWithValue("@ExcludeBookingId", excludeBookingId);
            cmd.Parameters.AddWithValue("@ShiftType", (object?)shiftType ?? "24_7");
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetSmartAvailableSpacesAsync(string categoryCode, DateTime startOn, DateTime endOn, int? capacity = null, string? shiftType = "24_7")
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Bookings_GetSmartAvailable", c);
            cmd.Parameters.AddWithValue("@CategoryCode", categoryCode);
            cmd.Parameters.AddWithValue("@StartOn", startOn);
            cmd.Parameters.AddWithValue("@EndOn", endOn);
            cmd.Parameters.AddWithValue("@Capacity", (object?)capacity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ShiftType", (object?)shiftType ?? "24_7");
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IDictionary<string, object?>> InsertBookingAsync(
            int userId, int spaceId, int pricingId, DateTime startOn, DateTime endOn, string? notes, int? createdById, string? userEmail,
            string? customerEmail = null, string? customerFirstName = null, string? customerLastName = null, string? customerPhone = null,
            string? customerCnic = null, string? customerAddress = null, int? customerCityId = null, string? customerNotes = null,
            decimal discountPercentage = 0, string discountType = "Percentage", decimal discountValue = 0,
            decimal? securityDepositOverride = null, int? floorId = null, int? billingPeriodMonths = null, int? securityDepositMonths = null, int? advanceRentMonths = null,
            string? shiftType = "24_7")
        {
            await using var c = await Open();
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
            cmd.Parameters.AddWithValue("@ShiftType", (object?)shiftType ?? "24_7");
            await using var r = await cmd.ExecuteReaderAsync();
            IDictionary<string, object?> result = new Dictionary<string, object?>();
            if (await r.ReadAsync()) result = ToDict(r);
            await r.CloseAsync();

            // Persist new fields (DiscountType, DiscountAmount, SecurityDepositOverride, FloorId) if booking was created
            if (result.TryGetValue("BookingId", out var bidObj) && bidObj is not null)
            {
                int bookingId = Convert.ToInt32(bidObj);

                // Calculate discount amount against room rent subtotal
                decimal subtotal = result.TryGetValue("SubtotalAmount", out var sa) && sa is not null ? Convert.ToDecimal(sa) : 0;
                decimal discountAmount = 0;
                if (discountType == "Amount" || discountType == "Fixed")
                {
                    discountAmount = subtotal > 0 ? Math.Min(discountValue, subtotal) : discountValue;
                }
                else if ((discountType == "Percentage" || discountType == "Percent") && discountValue > 0)
                {
                    discountAmount = Math.Round(subtotal * (discountValue / 100m), 2);
                }
                else if (discountPercentage > 0)
                {
                    discountAmount = Math.Round(subtotal * (discountPercentage / 100m), 2);
                }

                int advRentM = advanceRentMonths ?? billingPeriodMonths ?? 1;
                int secDepM = securityDepositMonths ?? 0;
                decimal monthlyRent = result.TryGetValue("MonthlyRent", out var mrObj) && mrObj is not null ? Convert.ToDecimal(mrObj) : 0m;
                if (result.TryGetValue("SubtotalAmount", out var subObj) && subObj is not null)
                {
                    decimal subVal = Convert.ToDecimal(subObj);
                    if (subVal > 0m && (monthlyRent == 0m || Math.Abs(monthlyRent * Math.Max(1, advRentM) - subVal) > 1m))
                    {
                        monthlyRent = subVal / Math.Max(1, advRentM);
                    }
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
                if (!string.IsNullOrWhiteSpace(shiftType)) updateParts.Add("ShiftType = @ST");

                var updateSql = $@"
                    UPDATE dbo.WN_Bookings 
                    SET {string.Join(", ", updateParts)},
                        TotalAmount = CASE WHEN @SDR > 0 THEN SubtotalAmount - @DA + ISNULL((SELECT TOP 1 TaxTotal FROM dbo.WN_Invoices WHERE BookingId = @BID), 0) + @SDR ELSE TotalAmount END
                    WHERE Id = @BID;

                    UPDATE dbo.WN_BookingDetails
                    SET SecurityDeposit = @SDR
                    WHERE BookingGuid = (SELECT IdGUID FROM dbo.WN_Bookings WHERE Id = @BID);

                    UPDATE dbo.WN_BookingLines
                    SET DiscountAmount = CASE WHEN ChargeTypeId = 1 THEN @DA ELSE 0 END,
                        Quantity = CASE WHEN ChargeTypeId = 2 THEN (CASE WHEN @SDM > 0 THEN @SDM ELSE 1 END) ELSE Quantity END,
                        UnitPrice = CASE WHEN ChargeTypeId = 2 THEN (CASE WHEN @SDM > 0 THEN @SDR / @SDM ELSE @SDR END) ELSE UnitPrice END
                    WHERE BookingId = @BID;

                    UPDATE dbo.WN_Invoices
                    SET SecurityDepositAmount = @SDR,
                        SecurityDepositMonths = @SDM,
                        DiscountTotal = @DA,
                        GrandTotal = SubTotal - @DA + ISNULL(TaxTotal, 0) + @SDR
                    WHERE BookingId = @BID;

                    UPDATE dbo.WN_InvoiceLines
                    SET DiscountAmount = CASE WHEN ChargeTypeId = 1 THEN @DA ELSE 0 END,
                        Quantity = CASE WHEN ChargeTypeId = 2 THEN (CASE WHEN @SDM > 0 THEN @SDM ELSE 1 END) ELSE Quantity END,
                        UnitPrice = CASE WHEN ChargeTypeId = 2 THEN (CASE WHEN @SDM > 0 THEN @SDR / @SDM ELSE @SDR END) ELSE UnitPrice END,
                        Description = CASE WHEN ChargeTypeId = 2 THEN (CASE WHEN @SDM > 1 THEN 'Security Deposit (' + CAST(@SDM AS NVARCHAR(5)) + ' Month(s) Refundable)' ELSE 'Security Deposit (Refundable)' END) ELSE Description END
                    WHERE InvoiceId IN (SELECT Id FROM dbo.WN_Invoices WHERE BookingId = @BID);
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
                if (!string.IsNullOrWhiteSpace(shiftType)) upd.Parameters.AddWithValue("@ST", shiftType);
                await upd.ExecuteNonQueryAsync();

                result["DiscountType"] = discountType;
                result["DiscountAmount"] = discountAmount;
                result["AdvanceRentMonths"] = advRentM;
                result["SecurityDepositMonths"] = secDepM;
                result["SecurityDepositRequired"] = secDepReq;
                result["SecurityDepositPaid"] = 0.00m;
                if (!string.IsNullOrWhiteSpace(shiftType)) result["ShiftType"] = shiftType;
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
            string? customerCnic = null, string? customerAddress = null, int? customerCityId = null, string? customerNotes = null,
            string? shiftType = "24_7")
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
            cmd.Parameters.AddWithValue("@ShiftType", (object?)shiftType ?? "24_7");
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

        public async Task<(int? Id, string? PublicId)> InsertSpaceTypeAsync(string name, string? description, byte? categoryId, short? capacity, bool hourlyAllowed, int? accountReceivableId, int? rentAccountId, int? servicesIncomeId, int? salesTaxId, int? securityReceivedId, int? createdById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_SpaceTypes_Insert", c);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CategoryId", (object?)categoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Capacity", (object?)capacity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@HourlyAllowed", hourlyAllowed ? 1 : 0);
            cmd.Parameters.AddWithValue("@AccountReceivableId", (object?)accountReceivableId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RentAccountId", (object?)rentAccountId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ServicesIncomeId", (object?)servicesIncomeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SalesTaxId", (object?)salesTaxId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SecurityReceivedId", (object?)securityReceivedId ?? DBNull.Value);
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

        public async Task UpdateSpaceTypeAsync(int id, string? name, string? description, byte? categoryId, short? capacity, bool? hourlyAllowed, int? accountReceivableId, int? rentAccountId, int? servicesIncomeId, int? salesTaxId, int? securityReceivedId, int? updatedById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_SpaceTypes_Update", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CategoryId", (object?)categoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Capacity", (object?)capacity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@HourlyAllowed", hourlyAllowed.HasValue ? (object)(hourlyAllowed.Value ? 1 : 0) : DBNull.Value);
            cmd.Parameters.AddWithValue("@AccountReceivableId", (object?)accountReceivableId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RentAccountId", (object?)rentAccountId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ServicesIncomeId", (object?)servicesIncomeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SalesTaxId", (object?)salesTaxId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SecurityReceivedId", (object?)securityReceivedId ?? DBNull.Value);
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



        public async Task<IEnumerable<IDictionary<string, object?>>> GetSpaceConfigAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_SpaceConfig_GetList", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetSpaceConfigV2Async(int? companyId, int? branchId, int? locationId)
        {
            await using var c = await Open();
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



        public async Task<IEnumerable<IDictionary<string, object?>>> GetSpaceStatusForConfigAsync(int configId)
        {
            await using var c = await Open();
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
            await using var cmd = SP("dbo.WN_SpaceConfig_DeleteSpaces", c);
            cmd.Parameters.AddWithValue("@ConfigId", configId);
            cmd.Parameters.AddWithValue("@SpaceGuids", (object?)spaceGuids ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                if (r["DeletedCode"] != DBNull.Value)
                    deleted.Add(r["DeletedCode"].ToString()!);
            }

            if (await r.NextResultAsync())
            {
                while (await r.ReadAsync())
                {
                    if (r["BlockedCode"] != DBNull.Value)
                        blocked.Add(r["BlockedCode"].ToString()!);
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

        private static void AddIdOrGuidParameters(SqlCommand cmd, string identifier)
        {
            if (Guid.TryParse(identifier, out var guidVal))
            {
                cmd.Parameters.AddWithValue("@IdGUID", guidVal);
                cmd.Parameters.AddWithValue("@Id", DBNull.Value);
            }
            else if (int.TryParse(identifier, out var intVal))
            {
                cmd.Parameters.AddWithValue("@IdGUID", DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", intVal);
            }
            else
            {
                cmd.Parameters.AddWithValue("@IdGUID", DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", DBNull.Value);
            }
        }

        public async Task<IDictionary<string, object?>?> GetCustomerByGuidAsync(string guid)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Customers_GetByGuid", c);
            AddIdOrGuidParameters(cmd, guid);
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

        public async Task<IDictionary<string, object?>?> UpdateCustomerAsync(string guid, string? firstName, string? lastName, string? email, string? phone, string? cnic, string? address, int? cityId, string? notes, bool? isActive, string? company = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Customers_Update", c);
            AddIdOrGuidParameters(cmd, guid);
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
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task DeleteCustomerAsync(string guid)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Customers_Delete", c);
            AddIdOrGuidParameters(cmd, guid);
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

        // --- Attendants & Access Control Implementation ---

        public async Task<(int PersonId, Guid PersonGuid)> AddAttendantSpAsync(string name, string email, string phone, string idType, string idNumber, int customerId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_AddAttendant", c);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@Phone", phone);
            cmd.Parameters.AddWithValue("@IdType", idType);
            cmd.Parameters.AddWithValue("@IdNumber", idNumber);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);

            var pId = new SqlParameter("@PersonId", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var pGuid = new SqlParameter("@PersonGuid", SqlDbType.UniqueIdentifier) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(pId);
            cmd.Parameters.Add(pGuid);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var personId = Convert.ToInt32(r["PersonId"]);
                var personGuid = (Guid)r["PersonGuid"];
                return (personId, personGuid);
            }

            return ((int)(pId.Value ?? 0), pGuid.Value is Guid g ? g : Guid.Empty);
        }

        public async Task UpdatePersonAsync(int personId, string name, string email, string phone)
        {
            await using var c = await Open();
            const string sql = @"UPDATE dbo.WN_Persons SET Name = @Name, Email = @Email, Phone = @Phone WHERE PersonId = @PersonId;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@PersonId", personId);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@Phone", phone);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetCustomerAttendantsDbAsync(int customerId)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT ca.Id AS CustomerAttendantId, p.PersonId, p.PersonGuid, p.Name, p.Email, p.Phone, p.IdType, p.IdNumber, ca.CustomerId, ca.IsActive, ca.CreatedAt
                FROM dbo.WN_CustomerAttendants ca WITH (NOLOCK)
                JOIN dbo.WN_Persons p WITH (NOLOCK) ON p.PersonId = ca.PersonId
                WHERE ca.CustomerId = @CustomerId AND ca.IsActive = 1
                ORDER BY p.Name ASC;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetBookingAttendantsDbAsync(int bookingDetailId)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT 
                    ba.Id,
                    ba.BookingDetailId,
                    ba.PersonId,
                    p.PersonGuid,
                    ba.CustomerId,
                    p.Name,
                    p.Email,
                    p.Phone,
                    p.IdType,
                    p.IdNumber,
                    ba.AssignedFrom,
                    ba.AssignedTo,
                    ISNULL(acc.IsEnabled, 1) AS IsEnabled,
                    ba.IsOverCapacity,
                    ba.ExcessSeatCount,
                    ba.SurchargeApplied
                FROM dbo.WN_BookingAttendants ba WITH (NOLOCK)
                JOIN dbo.WN_Persons p WITH (NOLOCK) ON p.PersonId = ba.PersonId
                LEFT JOIN dbo.WN_AccessStatus acc WITH (NOLOCK) ON acc.BookingDetailId = ba.BookingDetailId AND acc.PersonId = ba.PersonId
                WHERE ba.BookingDetailId = @BookingDetailId AND (ba.AssignedTo IS NULL OR ba.AssignedTo >= CAST(SYSUTCDATETIME() AS DATE))
                ORDER BY p.Name ASC;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@BookingDetailId", bookingDetailId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IDictionary<string, object?>> AssignAttendantToBookingSpAsync(int bookingDetailId, int personId, int customerId, DateTime assignedFrom)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_AssignAttendantToBooking", c);
            cmd.Parameters.AddWithValue("@BookingDetailId", bookingDetailId);
            cmd.Parameters.AddWithValue("@PersonId", personId);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@AssignedFrom", assignedFrom.Date);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return RowToDictionary(r);
            }
            return new Dictionary<string, object?>();
        }

        public async Task SoftRemoveAttendantFromBookingDbAsync(int bookingDetailId, int personId)
        {
            await using var c = await Open();
            const string sql = @"
                UPDATE dbo.WN_BookingAttendants 
                SET AssignedTo = DATEADD(day, -1, CAST(SYSUTCDATETIME() AS DATE)) 
                WHERE BookingDetailId = @BookingDetailId AND PersonId = @PersonId AND (AssignedTo IS NULL OR AssignedTo >= CAST(SYSUTCDATETIME() AS DATE));

                UPDATE dbo.WN_AccessStatus 
                SET IsEnabled = 0, RevokedAt = SYSUTCDATETIME() 
                WHERE BookingDetailId = @BookingDetailId AND PersonId = @PersonId;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@BookingDetailId", bookingDetailId);
            cmd.Parameters.AddWithValue("@PersonId", personId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> ToggleAccessStatusSpAsync(int bookingDetailId, int customerId, int? personId, bool isEnabled)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_ToggleAccessStatus", c);
            cmd.Parameters.AddWithValue("@BookingDetailId", bookingDetailId);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@PersonId", (object?)personId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsEnabled", isEnabled);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return Convert.ToInt32(r["RowsUpdated"]);
            }
            return 0;
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAccessStatusExportDbAsync()
        {
            await using var c = await Open();
            const string sql = @"SELECT PersonGuid, Name, Phone, Email, IsEnabled, BookingDetailId, CustomerId FROM dbo.WN_vw_AccessStatusExport;";
            await using var cmd = new SqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetCustomerActiveSpacesDbAsync(int customerId)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT 
                    bd.Id AS BookingDetailId,
                    bd.BookingGuid,
                    ISNULL(bd.SpaceName, N'Dedicated Space') AS SpaceName,
                    ISNULL(bd.SpaceCode, N'') AS SpaceCode,
                    ISNULL(bd.SpaceCategory, N'Private Office') AS SpaceCategory,
                    bd.StartDateTime,
                    bd.EndDateTime,
                    ISNULL(s.Capacity, 1) AS Capacity,
                    (SELECT COUNT(1) FROM dbo.WN_BookingAttendants ba WHERE ba.BookingDetailId = bd.Id AND (ba.AssignedTo IS NULL OR ba.AssignedTo >= CAST(SYSUTCDATETIME() AS DATE))) AS ActiveAttendantsCount
                FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
                LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.Code = bd.CustomerCode OR c.Email = bd.CustomerEmail
                LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Code = bd.SpaceCode OR s.Name = bd.SpaceName
                WHERE (c.Id = @CustomerId OR bd.CustomerCode = (SELECT Code FROM dbo.WN_Customers WHERE Id = @CustomerId))
                  AND bd.IsDeleted = 0
                ORDER BY bd.Id DESC;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IDictionary<string, object?>?> GetBookingDetailSummaryDbAsync(int bookingDetailId)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT 
                    bd.Id AS BookingDetailId,
                    bd.BookingGuid,
                    bd.SpaceName,
                    bd.SpaceCode,
                    bd.SpaceCategory,
                    bd.RentAmount,
                    bd.Amount,
                    s.Price AS SpacePrice,
                    ISNULL(s.Capacity, 1) AS Capacity,
                    (SELECT COUNT(1) FROM dbo.WN_BookingAttendants ba WHERE ba.BookingDetailId = bd.Id AND (ba.AssignedTo IS NULL OR ba.AssignedTo >= CAST(SYSUTCDATETIME() AS DATE))) AS CurrentActiveAttendants
                FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
                LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Code = bd.SpaceCode OR s.Name = bd.SpaceName
                WHERE bd.Id = @BookingDetailId;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@BookingDetailId", bookingDetailId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return RowToDictionary(r);
            }
            return null;
        }

        public async Task<IDictionary<string, object?>> CreateSurchargeInvoiceSpAsync(int bookingDetailId, int personId, int customerId, decimal surchargeAmount, int excessSeatCount)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_CreateSurchargeInvoice", c);
            cmd.Parameters.AddWithValue("@BookingDetailId", bookingDetailId);
            cmd.Parameters.AddWithValue("@PersonId", personId);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@SurchargeAmount", surchargeAmount);
            cmd.Parameters.AddWithValue("@ExcessSeatCount", excessSeatCount);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return RowToDictionary(r);
            }
            return new Dictionary<string, object?>();
        }

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetCustomerQuotationsAsync(
            int customerId,
            int page,
            int limit,
            string? status)
        {
            await using var c = await Open();
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;
            int offset = (page - 1) * limit;

            string whereClause = "WHERE q.CustomerId = @CustomerId AND (@Status IS NULL OR @Status = '' OR @Status = 'all' OR q.Status = @Status) ";

            string countSql = $"SELECT COUNT(1) FROM dbo.WN_Quotations q {whereClause}";
            await using var countCmd = new SqlCommand(countSql, c);
            countCmd.Parameters.AddWithValue("@CustomerId", customerId);
            countCmd.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
            int total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            string sql = $@"
                SELECT q.*, CONCAT(cu.FirstName, ' ', ISNULL(cu.LastName, '')) AS CustomerName, cu.Email AS CustomerEmail, cu.Company AS CustomerCompany, 
                       s.Name AS SpaceName, s.Code AS SpaceCode, l.Name AS LocationName, st.Name AS SpaceTypeName
                FROM dbo.WN_Quotations q 
                LEFT JOIN dbo.WN_Customers cu ON cu.Id = q.CustomerId 
                LEFT JOIN dbo.WN_Spaces s ON s.Id = q.SpaceId 
                LEFT JOIN dbo.WN_Locations l ON l.Id = s.LocationId 
                LEFT JOIN dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeId 
                {whereClause}
                ORDER BY q.CreatedDate DESC 
                OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY";

            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Offset", offset);
            cmd.Parameters.AddWithValue("@Limit", limit);

            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            return (rows, total);
        }

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetCustomerInvoicesDbAsync(
            int customerId,
            int userId,
            int page,
            int limit,
            int? statusId)
        {
            await using var c = await Open();
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;
            int offset = (page - 1) * limit;

            string whereClause = @"
                WHERE (
                    (@CustomerId > 0 AND (
                        i.UserId = @CustomerId
                        OR i.UserId IN (SELECT UserId FROM dbo.WN_Customers WHERE Id = @CustomerId AND UserId IS NOT NULL)
                        OR i.BookingId IN (SELECT Id FROM dbo.WN_Bookings WHERE CustomerId = @CustomerId)
                        OR i.UserId IN (
                            SELECT u.Id FROM dbo.WN_Users u WITH (NOLOCK)
                            INNER JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.Email = u.Email
                            WHERE c.Id = @CustomerId
                        )
                    ))
                    OR (@UserId > 0 AND i.UserId = @UserId)
                )
                AND (@StatusId IS NULL OR @StatusId <= 0 OR i.StatusId = @StatusId)";

            string countSql = $"SELECT COUNT(1) FROM dbo.WN_Invoices i WITH (NOLOCK) {whereClause}";
            await using var countCmd = new SqlCommand(countSql, c);
            countCmd.Parameters.AddWithValue("@CustomerId", customerId);
            countCmd.Parameters.AddWithValue("@UserId", userId);
            countCmd.Parameters.AddWithValue("@StatusId", (object?)statusId ?? DBNull.Value);
            int total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            string sql = $@"
                SELECT 
                    i.Id,
                    i.PublicId,
                    i.InvoiceNumber,
                    i.UserId,
                    i.BookingId,
                    i.IssuedOn,
                    i.DueOn,
                    COALESCE(i.BillingPeriodStart, b.StartOn) AS BillingPeriodStart,
                    COALESCE(i.BillingPeriodEnd, b.EndOn) AS BillingPeriodEnd,
                    ISNULL(i.SubTotal, 0) AS SubTotal,
                    ISNULL(i.DiscountTotal, 0) AS DiscountTotal,
                    ISNULL(i.TaxTotal, 0) AS TaxTotal,
                    ISNULL(i.GrandTotal, 0) AS GrandTotal,
                    ISNULL(i.PaidTotal, 0) AS PaidTotal,
                    ISNULL(i.GrandTotal - i.PaidTotal, 0) AS BalanceDue,
                    i.CurrencyCode,
                    i.StatusId,
                    i.InvoiceTypeId,
                    i.Notes,
                    i.CreatedOn,
                    CASE i.StatusId 
                        WHEN 61 THEN 'Unpaid' 
                        WHEN 62 THEN 'Paid' 
                        WHEN 1 THEN 'Unpaid' 
                        WHEN 2 THEN 'Paid' 
                        WHEN 3 THEN 'Partial' 
                        WHEN 4 THEN 'Overdue' 
                        ELSE 'Unknown' 
                    END AS StatusLabel
                FROM dbo.WN_Invoices i WITH (NOLOCK)
                LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
                {whereClause}
                ORDER BY i.Id DESC
                OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY";

            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@StatusId", (object?)statusId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Offset", offset);
            cmd.Parameters.AddWithValue("@Limit", limit);

            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            return (rows, total);
        }

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetCustomerAttendantsPaginatedDbAsync(
            int customerId,
            int page,
            int limit)
        {
            await using var c = await Open();
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;
            int offset = (page - 1) * limit;

            string countSql = "SELECT COUNT(1) FROM dbo.WN_CustomerAttendants ca WITH (NOLOCK) WHERE ca.CustomerId = @CustomerId AND ca.IsActive = 1";
            await using var countCmd = new SqlCommand(countSql, c);
            countCmd.Parameters.AddWithValue("@CustomerId", customerId);
            int total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            string sql = @"
                SELECT ca.Id AS CustomerAttendantId, p.PersonId, p.PersonGuid, p.Name, p.Email, p.Phone, p.IdType, p.IdNumber, ca.CustomerId, ca.IsActive, ca.CreatedAt,
                       b.Id AS BookingId, s.Name AS SpaceName
                FROM dbo.WN_CustomerAttendants ca WITH (NOLOCK)
                JOIN dbo.WN_Persons p WITH (NOLOCK) ON p.PersonId = ca.PersonId
                LEFT JOIN dbo.WN_BookingAttendants ba WITH (NOLOCK) ON ba.PersonId = p.PersonId AND (ba.AssignedTo IS NULL OR ba.AssignedTo >= CAST(SYSUTCDATETIME() AS DATE))
                LEFT JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.Id = ba.BookingDetailId
                LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.IdGUID = bd.BookingGuid
                LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                WHERE ca.CustomerId = @CustomerId AND ca.IsActive = 1
                ORDER BY ca.Id DESC
                OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY";

            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@Offset", offset);
            cmd.Parameters.AddWithValue("@Limit", limit);

            await using var r = await cmd.ExecuteReaderAsync();
            var rows = await ReadAll(r);
            return (rows, total);
        }

        public async Task<bool> CheckBookingOwnershipAsync(int bookingId, int customerId, int userId)
        {
            await using var c = await Open();
            string sql = @"
                SELECT TOP 1 1 
                FROM dbo.WN_Bookings b WITH (NOLOCK)
                LEFT JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.BookingGuid = b.IdGUID
                WHERE (b.Id = @BookingId OR bd.Id = @BookingId)
                  AND (b.CustomerId = @CustomerId 
                    OR b.UserGuid IN (SELECT IdGUID FROM dbo.WN_Users WHERE Id = @UserId)
                    OR b.CustomerId IN (SELECT Id FROM dbo.WN_Customers WHERE UserId = @UserId))";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@UserId", userId);
            var res = await cmd.ExecuteScalarAsync();
            return res != null && res != DBNull.Value;
        }

        public async Task<CustomerSTInvoiceDto?> GetCustomerSTInvoiceByPublicIdAsync(Guid publicId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_GetCustomerSTInvoiceByPublicId", c);
            cmd.Parameters.AddWithValue("@PublicId", publicId);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            var dto = new CustomerSTInvoiceDto
            {
                STInvoiceId = r.GetInt32(r.GetOrdinal("STInvoiceId")),
                STPublicId = r.GetGuid(r.GetOrdinal("STPublicId")),
                CustomerInvoiceId = r.GetInt32(r.GetOrdinal("CustomerInvoiceId")),
                STInvoiceNumber = r.GetString(r.GetOrdinal("STInvoiceNumber")),
                ParentInvoiceNumber = r.GetString(r.GetOrdinal("ParentInvoiceNumber")),
                IssuedOn = r.GetDateTime(r.GetOrdinal("IssuedOn")),
                DueOn = r.GetDateTime(r.GetOrdinal("DueOn")),
                CurrencyCode = r.IsDBNull(r.GetOrdinal("CurrencyCode")) ? "PKR" : r.GetString(r.GetOrdinal("CurrencyCode")),

                TariffHeading = r.GetString(r.GetOrdinal("TariffHeading")),
                TariffLabel = r.GetString(r.GetOrdinal("TariffLabel")),

                RoomRentDescription = r.GetString(r.GetOrdinal("RoomRentDescription")),
                RoomRentAmount = r.GetDecimal(r.GetOrdinal("RoomRentAmount")),
                RoomRentTaxRate = r.GetDecimal(r.GetOrdinal("RoomRentTaxRate")),
                RoomRentTaxAmount = r.GetDecimal(r.GetOrdinal("RoomRentTaxAmount")),

                ServiceChargeDescription = r.GetString(r.GetOrdinal("ServiceChargeDescription")),
                ServiceChargeAmount = r.GetDecimal(r.GetOrdinal("ServiceChargeAmount")),
                ServiceChargeTaxRate = r.GetDecimal(r.GetOrdinal("ServiceChargeTaxRate")),
                ServiceChargeTaxAmount = r.GetDecimal(r.GetOrdinal("ServiceChargeTaxAmount")),

                SecurityDepositDescription = r.IsDBNull(r.GetOrdinal("SecurityDepositDescription")) ? null : r.GetString(r.GetOrdinal("SecurityDepositDescription")),
                SecurityDepositAmount = r.IsDBNull(r.GetOrdinal("SecurityDepositAmount")) ? null : r.GetDecimal(r.GetOrdinal("SecurityDepositAmount")),
                SecurityDepositTaxRate = r.GetDecimal(r.GetOrdinal("SecurityDepositTaxRate")),
                SecurityDepositTaxAmount = r.GetDecimal(r.GetOrdinal("SecurityDepositTaxAmount")),

                SubTotal = r.GetDecimal(r.GetOrdinal("SubTotal")),
                TaxTotal = r.GetDecimal(r.GetOrdinal("TaxTotal")),
                GrandTotal = r.GetDecimal(r.GetOrdinal("GrandTotal")),
                CreatedOn = r.GetDateTime(r.GetOrdinal("CreatedOn")),

                CustomerName = r.IsDBNull(r.GetOrdinal("CustomerName")) ? "Valued Customer" : r.GetString(r.GetOrdinal("CustomerName")),
                CustomerAddress = r.IsDBNull(r.GetOrdinal("CustomerAddress")) ? "" : r.GetString(r.GetOrdinal("CustomerAddress")),
                CustomerCode = r.IsDBNull(r.GetOrdinal("CustomerCode")) ? "" : r.GetString(r.GetOrdinal("CustomerCode")),
                SntnNtnNic = r.IsDBNull(r.GetOrdinal("SntnNtnNic")) ? "" : r.GetString(r.GetOrdinal("SntnNtnNic")),

                CenterName = HasColumn(r, "CenterName") && !r.IsDBNull(r.GetOrdinal("CenterName")) ? r.GetString(r.GetOrdinal("CenterName")) : null,
                VendorLegalName = r.IsDBNull(r.GetOrdinal("VendorLegalName")) ? "WorkNest Coworking Spaces (Pvt) Ltd" : r.GetString(r.GetOrdinal("VendorLegalName")),
                VendorAddress = r.IsDBNull(r.GetOrdinal("VendorAddress")) ? "3rd Floor EOBI Building-II, I-8 Markaz, Islamabad" : r.GetString(r.GetOrdinal("VendorAddress")),
                VendorPhone = r.IsDBNull(r.GetOrdinal("VendorPhone")) ? "+92 309 9771774 / +92 308 0256000" : r.GetString(r.GetOrdinal("VendorPhone")),
                VendorNtn = r.IsDBNull(r.GetOrdinal("VendorNtn")) ? "7492018-3" : r.GetString(r.GetOrdinal("VendorNtn")),
            };

            // Single line item: Service Charges, Tax on it, and Total
            decimal serviceChargeTaxRate = dto.ServiceChargeTaxRate > 0 ? dto.ServiceChargeTaxRate : (dto.TaxTotal > 0 ? 16m : 0m);
            decimal serviceChargeTaxAmount = dto.ServiceChargeTaxAmount > 0 ? dto.ServiceChargeTaxAmount : dto.TaxTotal;
            decimal serviceChargeExclusive = dto.ServiceChargeAmount > 0
                ? dto.ServiceChargeAmount
                : (serviceChargeTaxAmount > 0 && serviceChargeTaxRate > 0
                    ? Math.Round(serviceChargeTaxAmount / (serviceChargeTaxRate / 100m), 2)
                    : dto.SubTotal);

            dto.LineItems.Add(new CustomerSTInvoiceLineDto
            {
                Description = !string.IsNullOrWhiteSpace(dto.ServiceChargeDescription) ? dto.ServiceChargeDescription : "Service Charges",
                ExclusiveAmount = serviceChargeExclusive,
                TaxPercentage = serviceChargeTaxRate,
                TaxAmount = serviceChargeTaxAmount
            });

            // Set ST Invoice totals strictly to the Service Charges entry
            dto.SubTotal = serviceChargeExclusive;
            dto.TaxTotal = serviceChargeTaxAmount;
            dto.GrandTotal = serviceChargeExclusive + serviceChargeTaxAmount;

            return dto;
        }

        public async Task<Guid?> GetSTInvoicePublicIdByInvoiceIdAsync(int invoiceId)
        {
            await using var c = await Open();
            string sql = "SELECT TOP 1 PublicId FROM dbo.WN_CustomerSTInvoice WITH (NOLOCK) WHERE CustomerInvoiceId = @InvoiceId ORDER BY Id DESC";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@InvoiceId", invoiceId);
            var res = await cmd.ExecuteScalarAsync();
            if (res != null && res != DBNull.Value && res is Guid g)
            {
                return g;
            }
            if (res != null && res != DBNull.Value && Guid.TryParse(res.ToString(), out var parsedGuid))
            {
                return parsedGuid;
            }
            return null;
        }

        private static bool HasColumn(System.Data.Common.DbDataReader r, string columnName)
        {
            for (int i = 0; i < r.FieldCount; i++)
            {
                if (r.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public async Task<int> InsertAgreementDbAsync(int quotationId, int customerId, string entityType, int refundDays, decimal feeAmount, decimal securityDeposit, string? opHours, string? custName, string? custCnic, string? custPhone, string? custAddress, string? compName, string? ntn, string? secp, int? userId, int? templateVersionId = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Agreements_Insert", c);
            cmd.Parameters.AddWithValue("@QuotationId", quotationId);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@EntityType", entityType);
            cmd.Parameters.AddWithValue("@RefundDays", refundDays > 0 ? refundDays : 30);
            cmd.Parameters.AddWithValue("@FeeAmount", feeAmount);
            cmd.Parameters.AddWithValue("@SecurityDeposit", securityDeposit);
            cmd.Parameters.AddWithValue("@OperatingHours", (object?)opHours ?? "24/7");
            cmd.Parameters.AddWithValue("@CustomerName", (object?)custName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerCnic", (object?)custCnic ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerPhone", (object?)custPhone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerAddress", (object?)custAddress ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CompanyName", (object?)compName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Ntn", (object?)ntn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SecpRegistrationNo", (object?)secp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TemplateVersionId", (object?)templateVersionId ?? DBNull.Value);

            var res = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(res);
        }

        public async Task<IDictionary<string, object?>?> GetAgreementByIdDbAsync(int agreementId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Agreements_GetById", c);
            cmd.Parameters.AddWithValue("@AgreementId", agreementId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                return ToDict(r);

            return null;
        }

        public async Task<IDictionary<string, object?>?> GetQuotationLocationAndCompanyDetailsDbAsync(int quotationId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Quotations_GetLocationAndCompanyDetails", c);
            cmd.Parameters.AddWithValue("@QuotationId", quotationId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                return ToDict(r);

            return null;
        }

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetAgreementsListDbAsync(int page, int limit, string? search, string? status)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Agreements_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);

            int total = 0;
            var list = new List<IDictionary<string, object?>>();

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                total = r.GetInt32(0);

            if (await r.NextResultAsync())
            {
                while (await r.ReadAsync())
                    list.Add(ToDict(r));
            }

            return (list, total);
        }

        public async Task MarkAgreementSignedDbAsync(int agreementId, int? userId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Agreements_MarkSigned", c);
            cmd.Parameters.AddWithValue("@AgreementId", agreementId);
            cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetAgreementBookingIdAsync(int agreementId, int bookingId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Agreements_SetBookingId", c);
            cmd.Parameters.AddWithValue("@AgreementId", agreementId);
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateCustomerAgreementDetailsDbAsync(int customerId, string? fullName, string? phone, string? cnic, string? address, string? company, string? ntn, string? secp)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Customers_UpdateAgreementDetails", c);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@FirstName", (object?)fullName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Cnic", (object?)cnic ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CompanyName", (object?)company ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@NTN", (object?)ntn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SecpRegistrationNo", (object?)secp ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateQuotationStatusAsync(int quotationId, string status)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Quotations_UpdateStatus", c);
            cmd.Parameters.AddWithValue("@QuotationId", quotationId);
            cmd.Parameters.AddWithValue("@Status", status);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task AddQuotationActivityAsync(int quotationId, int version, string activityType, string message, int? userId = null, string? customerNote = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_QuotationActivities_Insert", c);
            cmd.Parameters.AddWithValue("@QuotationId", quotationId);
            cmd.Parameters.AddWithValue("@Version", version);
            cmd.Parameters.AddWithValue("@ActivityType", activityType);
            cmd.Parameters.AddWithValue("@Message", message);
            cmd.Parameters.AddWithValue("@CreatedByUserId", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomerNote", (object?)customerNote ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IDictionary<string, object?>?> GetActiveLeaseTemplateByNameDbAsync(string name)
        {
            await using var c = await Open();
            string sql = @"
                SELECT TOP 1 
                    t.Id,
                    t.Name,
                    t.ContentHtml,
                    t.IsActive,
                    t.CreatedAt,
                    t.CreatedBy,
                    ISNULL(u.Name, u.Email) AS CreatedByName
                FROM dbo.WN_LeaseTemplates t WITH (NOLOCK)
                LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = t.CreatedBy
                WHERE t.Name = @Name AND t.IsActive = 1
                ORDER BY t.Id DESC;";

            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Name", name);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                return ToDict(r);

            return null;
        }

        public async Task<IDictionary<string, object?>> PublishLeaseTemplateDbAsync(string name, string contentHtml, int? createdBy)
        {
            await using var c = await Open();
            string sql = @"
                BEGIN TRANSACTION;
                BEGIN TRY
                    UPDATE dbo.WN_LeaseTemplates
                    SET IsActive = 0
                    WHERE Name = @Name AND IsActive = 1;

                    INSERT INTO dbo.WN_LeaseTemplates (
                        Name, ContentHtml, IsActive, CreatedAt, CreatedBy
                    )
                    OUTPUT 
                        INSERTED.Id,
                        INSERTED.Name,
                        INSERTED.ContentHtml,
                        INSERTED.IsActive,
                        INSERTED.CreatedAt,
                        INSERTED.CreatedBy
                    VALUES (
                        @Name, @ContentHtml, 1, SYSUTCDATETIME(), @CreatedBy
                    );

                    COMMIT TRANSACTION;
                END TRY
                BEGIN CATCH
                    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                    THROW;
                END CATCH;";

            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@ContentHtml", contentHtml);
            cmd.Parameters.AddWithValue("@CreatedBy", (object?)createdBy ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                return ToDict(r);

            throw new InvalidOperationException("Failed to publish lease template.");
        }

        public async Task UpdateAgreementSignedPdfDbAsync(int agreementId, string signedPdfPath, DateTime uploadedAt)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Agreements_UpdateSignedPdf", c);
            cmd.Parameters.AddWithValue("@AgreementId", agreementId);
            cmd.Parameters.AddWithValue("@SignedPdfPath", signedPdfPath);
            cmd.Parameters.AddWithValue("@SignedPdfUploadedAt", uploadedAt);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ClearAgreementSignedPdfDbAsync(int agreementId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Agreements_ClearSignedPdf", c);
            cmd.Parameters.AddWithValue("@AgreementId", agreementId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> DeleteAgreementDbAsync(int agreementId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Agreements_Delete", c);
            cmd.Parameters.AddWithValue("@AgreementId", agreementId);
            int rowsAffected = await cmd.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }

        public async Task EnsureLeaseTemplateSchemaDbAsync()
        {
            await using var c = await Open();
            string sql = @"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WN_LeaseTemplates')
                BEGIN
                    CREATE TABLE dbo.WN_LeaseTemplates (
                        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
                        Name NVARCHAR(100) NOT NULL,
                        ContentHtml NVARCHAR(MAX) NOT NULL,
                        IsActive BIT NOT NULL DEFAULT 1,
                        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        CreatedBy INT NULL
                    );
                    CREATE INDEX IX_WN_LeaseTemplates_Name_IsActive ON dbo.WN_LeaseTemplates(Name, IsActive);
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.WN_Agreements') AND name = 'TemplateVersionId')
                BEGIN
                    ALTER TABLE dbo.WN_Agreements ADD TemplateVersionId INT NULL;
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.WN_Agreements') AND name = 'SignedPdfPath')
                BEGIN
                    ALTER TABLE dbo.WN_Agreements ADD SignedPdfPath NVARCHAR(500) NULL;
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.WN_Agreements') AND name = 'SignedPdfUploadedAt')
                BEGIN
                    ALTER TABLE dbo.WN_Agreements ADD SignedPdfUploadedAt DATETIME2 NULL;
                END;";

            await using var cmd = new SqlCommand(sql, c);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- Announcements & Alerts Implementations ---

        public async Task<AnnouncementDetailDto?> CreateAnnouncementAsync(
            string title, string body, string type, string targetScope,
            int? locationId, int? spaceId, IEnumerable<int>? customUserIds,
            DateTime? scheduledAt, int createdById)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Announcements_Create", c);
            cmd.Parameters.AddWithValue("@Title", title);
            cmd.Parameters.AddWithValue("@Body", body);
            cmd.Parameters.AddWithValue("@Type", type);
            cmd.Parameters.AddWithValue("@TargetScope", targetScope);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SpaceId", (object?)spaceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CustomUserIds", customUserIds != null ? (object)string.Join(",", customUserIds) : DBNull.Value);
            cmd.Parameters.AddWithValue("@ScheduledAt", (object?)scheduledAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedBy", createdById);

            await using var r = await cmd.ExecuteReaderAsync();
            AnnouncementDetailDto? dto = null;

            if (await r.ReadAsync())
            {
                dto = MapAnnouncementDetail(r);
            }

            if (dto != null && await r.NextResultAsync())
            {
                while (await r.ReadAsync())
                {
                    dto.Recipients.Add(MapAnnouncementRecipient(r));
                }
            }

            return dto;
        }

        public async Task<(IEnumerable<AnnouncementSummaryDto> Rows, int Total)> GetAnnouncementsAsync(int page = 1, int limit = 20, string? search = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Announcements_GetList", c);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);

            var list = new List<AnnouncementSummaryDto>();
            int total = 0;

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(MapAnnouncementSummary(r));
                if (total == 0 && !r.IsDBNull(r.GetOrdinal("TotalRecords")))
                {
                    total = Convert.ToInt32(r["TotalRecords"]);
                }
            }

            return (list, total > 0 ? total : list.Count);
        }

        public async Task<AnnouncementDetailDto?> GetAnnouncementByIdAsync(Guid id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Announcements_GetById", c);
            cmd.Parameters.AddWithValue("@AnnouncementId", id);

            AnnouncementDetailDto? dto = null;
            await using var r = await cmd.ExecuteReaderAsync();

            if (await r.ReadAsync())
            {
                dto = MapAnnouncementDetail(r);
            }

            if (dto != null && await r.NextResultAsync())
            {
                while (await r.ReadAsync())
                {
                    dto.Recipients.Add(MapAnnouncementRecipient(r));
                }
            }

            return dto;
        }

        public async Task<(IEnumerable<UserAnnouncementDto> Rows, int Total)> GetUserAnnouncementsAsync(int userId, int page = 1, int limit = 20)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Announcements_GetForUser", c);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);

            var list = new List<UserAnnouncementDto>();
            int total = 0;

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new UserAnnouncementDto
                {
                    Id = r.GetGuid(r.GetOrdinal("Id")),
                    Title = r.GetString(r.GetOrdinal("Title")),
                    Body = r.GetString(r.GetOrdinal("Body")),
                    Type = r.GetString(r.GetOrdinal("Type")),
                    TargetScope = r.GetString(r.GetOrdinal("TargetScope")),
                    ScheduledAt = r.IsDBNull(r.GetOrdinal("ScheduledAt")) ? null : r.GetDateTime(r.GetOrdinal("ScheduledAt")),
                    SentAt = r.IsDBNull(r.GetOrdinal("SentAt")) ? null : r.GetDateTime(r.GetOrdinal("SentAt")),
                    CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
                    IsRead = !r.IsDBNull(r.GetOrdinal("IsRead")) && Convert.ToBoolean(r["IsRead"]),
                    ReadAt = r.IsDBNull(r.GetOrdinal("ReadAt")) ? null : r.GetDateTime(r.GetOrdinal("ReadAt"))
                });

                if (total == 0 && !r.IsDBNull(r.GetOrdinal("TotalRecords")))
                {
                    total = Convert.ToInt32(r["TotalRecords"]);
                }
            }

            return (list, total > 0 ? total : list.Count);
        }

        public async Task<bool> MarkAnnouncementReadAsync(Guid announcementId, int userId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Announcements_MarkRead", c);
            cmd.Parameters.AddWithValue("@AnnouncementId", announcementId);
            cmd.Parameters.AddWithValue("@UserId", userId);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }

        public async Task<IEnumerable<PendingDeliveryItemDto>> GetPendingAnnouncementDeliveriesAsync(int batchSize = 200)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Announcements_GetPendingDeliveries", c);
            cmd.Parameters.AddWithValue("@BatchSize", batchSize);

            var list = new List<PendingDeliveryItemDto>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new PendingDeliveryItemDto
                {
                    RecipientId = r.GetInt64(r.GetOrdinal("RecipientId")),
                    AnnouncementId = r.GetGuid(r.GetOrdinal("AnnouncementId")),
                    UserId = r.GetInt32(r.GetOrdinal("UserId")),
                    Channel = r.GetString(r.GetOrdinal("Channel")),
                    RetryCount = r.GetInt32(r.GetOrdinal("RetryCount")),
                    Title = r.GetString(r.GetOrdinal("Title")),
                    Body = r.GetString(r.GetOrdinal("Body")),
                    Type = r.GetString(r.GetOrdinal("Type")),
                    UserEmail = r.IsDBNull(r.GetOrdinal("UserEmail")) ? null : r.GetString(r.GetOrdinal("UserEmail")),
                    UserName = r.IsDBNull(r.GetOrdinal("UserName")) ? null : r.GetString(r.GetOrdinal("UserName"))
                });
            }

            return list;
        }

        public async Task UpdateAnnouncementDeliveryStatusAsync(long recipientId, string status, int retryCount)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Announcements_UpdateDeliveryStatus", c);
            cmd.Parameters.AddWithValue("@RecipientId", recipientId);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@RetryCount", retryCount);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task CheckAndUpdateAnnouncementTerminalStatusAsync(Guid announcementId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Announcements_CheckTerminalStatus", c);
            cmd.Parameters.AddWithValue("@AnnouncementId", announcementId);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RegisterDeviceTokenAsync(int userId, string token, string platform)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_DeviceTokens_Upsert", c);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@Token", token);
            cmd.Parameters.AddWithValue("@Platform", platform);

            await cmd.ExecuteNonQueryAsync();
        }

        private static AnnouncementSummaryDto MapAnnouncementSummary(SqlDataReader r)
        {
            return new AnnouncementSummaryDto
            {
                Id = r.GetGuid(r.GetOrdinal("Id")),
                Title = r.GetString(r.GetOrdinal("Title")),
                Body = r.GetString(r.GetOrdinal("Body")),
                Type = r.GetString(r.GetOrdinal("Type")),
                TargetScope = r.GetString(r.GetOrdinal("TargetScope")),
                LocationId = r.IsDBNull(r.GetOrdinal("LocationId")) ? null : r.GetInt32(r.GetOrdinal("LocationId")),
                LocationName = r.IsDBNull(r.GetOrdinal("LocationName")) ? null : r.GetString(r.GetOrdinal("LocationName")),
                SpaceId = r.IsDBNull(r.GetOrdinal("SpaceId")) ? null : r.GetInt32(r.GetOrdinal("SpaceId")),
                SpaceName = r.IsDBNull(r.GetOrdinal("SpaceName")) ? null : r.GetString(r.GetOrdinal("SpaceName")),
                ScheduledAt = r.IsDBNull(r.GetOrdinal("ScheduledAt")) ? null : r.GetDateTime(r.GetOrdinal("ScheduledAt")),
                SentAt = r.IsDBNull(r.GetOrdinal("SentAt")) ? null : r.GetDateTime(r.GetOrdinal("SentAt")),
                Status = r.GetString(r.GetOrdinal("Status")),
                CreatedBy = r.GetInt32(r.GetOrdinal("CreatedBy")),
                CreatedByName = r.IsDBNull(r.GetOrdinal("CreatedByName")) ? null : r.GetString(r.GetOrdinal("CreatedByName")),
                CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
                TotalRecipients = r.IsDBNull(r.GetOrdinal("TotalRecipients")) ? 0 : Convert.ToInt32(r["TotalRecipients"]),
                TotalUsers = r.IsDBNull(r.GetOrdinal("TotalUsers")) ? 0 : Convert.ToInt32(r["TotalUsers"]),
                SentCount = r.IsDBNull(r.GetOrdinal("SentCount")) ? 0 : Convert.ToInt32(r["SentCount"]),
                FailedCount = r.IsDBNull(r.GetOrdinal("FailedCount")) ? 0 : Convert.ToInt32(r["FailedCount"]),
                ReadCount = r.IsDBNull(r.GetOrdinal("ReadCount")) ? 0 : Convert.ToInt32(r["ReadCount"]),
                PendingCount = r.IsDBNull(r.GetOrdinal("PendingCount")) ? 0 : Convert.ToInt32(r["PendingCount"]),
                PushSentCount = r.IsDBNull(r.GetOrdinal("PushSentCount")) ? 0 : Convert.ToInt32(r["PushSentCount"]),
                EmailSentCount = r.IsDBNull(r.GetOrdinal("EmailSentCount")) ? 0 : Convert.ToInt32(r["EmailSentCount"])
            };
        }

        private static AnnouncementDetailDto MapAnnouncementDetail(SqlDataReader r)
        {
            var summary = MapAnnouncementSummary(r);
            return new AnnouncementDetailDto
            {
                Id = summary.Id,
                Title = summary.Title,
                Body = summary.Body,
                Type = summary.Type,
                TargetScope = summary.TargetScope,
                LocationId = summary.LocationId,
                LocationName = summary.LocationName,
                SpaceId = summary.SpaceId,
                SpaceName = summary.SpaceName,
                ScheduledAt = summary.ScheduledAt,
                SentAt = summary.SentAt,
                Status = summary.Status,
                CreatedBy = summary.CreatedBy,
                CreatedByName = summary.CreatedByName,
                CreatedAt = summary.CreatedAt,
                TotalRecipients = summary.TotalRecipients,
                TotalUsers = summary.TotalUsers,
                SentCount = summary.SentCount,
                FailedCount = summary.FailedCount,
                ReadCount = summary.ReadCount,
                PendingCount = summary.PendingCount,
                PushSentCount = summary.PushSentCount,
                EmailSentCount = summary.EmailSentCount,
                Recipients = new List<AnnouncementRecipientDto>()
            };
        }

        private static AnnouncementRecipientDto MapAnnouncementRecipient(SqlDataReader r)
        {
            return new AnnouncementRecipientDto
            {
                Id = r.GetInt64(r.GetOrdinal("Id")),
                AnnouncementId = r.GetGuid(r.GetOrdinal("AnnouncementId")),
                UserId = r.GetInt32(r.GetOrdinal("UserId")),
                UserName = r.IsDBNull(r.GetOrdinal("UserName")) ? null : r.GetString(r.GetOrdinal("UserName")),
                UserEmail = r.IsDBNull(r.GetOrdinal("UserEmail")) ? null : r.GetString(r.GetOrdinal("UserEmail")),
                Channel = r.GetString(r.GetOrdinal("Channel")),
                Status = r.GetString(r.GetOrdinal("Status")),
                RetryCount = r.GetInt32(r.GetOrdinal("RetryCount")),
                SentAt = r.IsDBNull(r.GetOrdinal("SentAt")) ? null : r.GetDateTime(r.GetOrdinal("SentAt"))
            };
        }

        // --- Hikvision Devices & Cache Snapshots Implementation ---

        public async Task<IEnumerable<HikDeviceDto>> GetHikDevicesAsync(string? location = null)
        {
            await using var conn = await Open();
            var sql = @"SELECT 
                            d.Id AS id, 
                            d.Device_Name AS name, 
                            g.Name AS grp, 
                            d.Location AS location, 
                            d.Online AS online, 
                            d.Last_seen AS last_seen
                        FROM dbo.WN_HIK_Devices d WITH (NOLOCK)
                        LEFT JOIN dbo.WN_HIK_Groups g WITH (NOLOCK) ON g.Id = d.Group_id";
            if (!string.IsNullOrWhiteSpace(location))
            {
                sql += " WHERE d.Location = @Location";
            }
            sql += " ORDER BY d.Device_Name";

            await using var cmd = new SqlCommand(sql, conn);
            if (!string.IsNullOrWhiteSpace(location))
            {
                cmd.Parameters.AddWithValue("@Location", location);
            }

            var devices = new List<HikDeviceDto>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                devices.Add(new HikDeviceDto
                {
                    Id = r.GetInt32(r.GetOrdinal("id")),
                    Name = r.GetString(r.GetOrdinal("name")),
                    Grp = r.IsDBNull(r.GetOrdinal("grp")) ? null : r.GetString(r.GetOrdinal("grp")),
                    Code = null,
                    Location = r.IsDBNull(r.GetOrdinal("location")) ? null : r.GetString(r.GetOrdinal("location")),
                    Online = !r.IsDBNull(r.GetOrdinal("online")) && r.GetBoolean(r.GetOrdinal("online")) ? 1 : 0,
                    LastSeen = r.IsDBNull(r.GetOrdinal("last_seen")) ? null : r.GetDateTime(r.GetOrdinal("last_seen")).ToString("yyyy-MM-ddTHH:mm:ss")
                });
            }
            return devices;
        }

        public async Task<IEnumerable<(int DeviceId, string? RosterJson)>> GetHikDeviceSnapshotsAsync(string? location = null)
        {
            await using var conn = await Open();
            var sql = @"SELECT 
                            d.Id AS device_id,
                            e.employee_no,
                            e.name,
                            e.card_no,
                            e.valid_begin,
                            e.valid_end,
                            e.status,
                            e.kind
                        FROM dbo.WN_HIK_Devices d WITH (NOLOCK)
                        LEFT JOIN dbo.WN_HIK_AccessGrants g WITH (NOLOCK) ON g.device_id = d.Id
                        LEFT JOIN dbo.WN_HIK_Employees e WITH (NOLOCK) ON e.id = g.employee_id";
            if (!string.IsNullOrWhiteSpace(location))
            {
                sql += " WHERE d.Location = @Location";
            }
            sql += " ORDER BY d.Id, e.employee_no";

            await using var cmd = new SqlCommand(sql, conn);
            if (!string.IsNullOrWhiteSpace(location))
            {
                cmd.Parameters.AddWithValue("@Location", location);
            }

            var deviceMap = new Dictionary<int, List<object>>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var deviceId = r.GetInt32(r.GetOrdinal("device_id"));
                if (!deviceMap.ContainsKey(deviceId))
                {
                    deviceMap[deviceId] = new List<object>();
                }

                if (!r.IsDBNull(r.GetOrdinal("employee_no")))
                {
                    var empNo = r.GetString(r.GetOrdinal("employee_no"));
                    var name = r.IsDBNull(r.GetOrdinal("name")) ? "" : r.GetString(r.GetOrdinal("name"));
                    var cardNo = r.IsDBNull(r.GetOrdinal("card_no")) ? null : r.GetString(r.GetOrdinal("card_no"));
                    var validBegin = r.IsDBNull(r.GetOrdinal("valid_begin")) ? (DateTime?)null : r.GetDateTime(r.GetOrdinal("valid_begin"));
                    var validEnd = r.IsDBNull(r.GetOrdinal("valid_end")) ? (DateTime?)null : r.GetDateTime(r.GetOrdinal("valid_end"));
                    var status = r.IsDBNull(r.GetOrdinal("status")) ? "active" : r.GetString(r.GetOrdinal("status"));
                    var kind = r.IsDBNull(r.GetOrdinal("kind")) ? "normal" : r.GetString(r.GetOrdinal("kind"));

                    deviceMap[deviceId].Add(new
                    {
                        employeeNo = empNo,
                        name = name,
                        userType = kind == "admin" ? "admin" : "normal",
                        numOfCard = string.IsNullOrEmpty(cardNo) ? 0 : 1,
                        Valid = new
                        {
                            enable = status == "active",
                            beginTime = validBegin?.ToString("yyyy-MM-ddTHH:mm:ss"),
                            endTime = validEnd?.ToString("yyyy-MM-ddTHH:mm:ss")
                        }
                    });
                }
            }

            return deviceMap.Select(kv => (kv.Key, (string?)System.Text.Json.JsonSerializer.Serialize(kv.Value)));
        }

        public async Task<Dictionary<string, string>> GetHikCnicMapAsync()
        {
            await using var conn = await Open();
            var cnics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using var cmd = new SqlCommand(
                @"SELECT employee_no, name 
                  FROM dbo.WN_HIK_Employees WITH (NOLOCK)
                  WHERE employee_no IS NOT NULL", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var empNo = r.GetString(0);
                var name = r.IsDBNull(1) ? "" : r.GetString(1).Trim().ToLowerInvariant();
                cnics[$"{empNo}||{name}"] = "";
            }
            return cnics;
        }

        public async Task<string?> GetHikDeviceSnapshotByIdAsync(int deviceId)
        {
            var snapshots = await GetHikDeviceSnapshotsAsync();
            var match = snapshots.FirstOrDefault(s => s.DeviceId == deviceId);
            return match.RosterJson;
        }

        public async Task<int> GetNextHikEmployeeNoAsync()
        {
            await using var conn = await Open();
            await using var cmd = new SqlCommand(
                @"SELECT MAX(n) FROM (
                    SELECT TRY_CAST(employee_no AS INT) AS n FROM dbo.WN_HIK_Employees WITH (NOLOCK)
                  ) t WHERE n IS NOT NULL AND n < 8500", conn);
            var max = await cmd.ExecuteScalarAsync();
            var next = (max is int m ? m : 999) + 1;
            return next;
        }

        // --- KYC Portal ---

        public async Task<IEnumerable<WorkNest.Domain.Entities.KYCDocumentType>> GetActiveKycDocumentTypesDbAsync(string? category = null)
        {
            await using var conn = await Open();
            await using var cmd = SP("dbo.WN_KYCDocumentTypes_GetActive", conn);
            cmd.Parameters.AddWithValue("@Category", (object?)category ?? DBNull.Value);

            var list = new List<WorkNest.Domain.Entities.KYCDocumentType>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new WorkNest.Domain.Entities.KYCDocumentType
                {
                    Id = r.GetInt32(r.GetOrdinal("Id")),
                    Code = r.GetString(r.GetOrdinal("Code")),
                    Name = r.GetString(r.GetOrdinal("Name")),
                    IndividualRequirement = r.GetByte(r.GetOrdinal("IndividualRequirement")),
                    BusinessRequirement = r.GetByte(r.GetOrdinal("BusinessRequirement")),
                    GroupCode = r.IsDBNull(r.GetOrdinal("GroupCode")) ? null : r.GetString(r.GetOrdinal("GroupCode")),
                    AlternativeCode = r.IsDBNull(r.GetOrdinal("AlternativeCode")) ? null : r.GetString(r.GetOrdinal("AlternativeCode")),
                    AllowMultiple = r.GetBoolean(r.GetOrdinal("AllowMultiple")),
                    RequiresHolderName = r.GetBoolean(r.GetOrdinal("RequiresHolderName")),
                    RequiresExpiry = r.GetBoolean(r.GetOrdinal("RequiresExpiry")),
                    SortOrder = r.GetInt32(r.GetOrdinal("SortOrder")),
                    IsActive = r.GetBoolean(r.GetOrdinal("IsActive"))
                });
            }
            return list;
        }

        public async Task<IEnumerable<WorkNest.Domain.Entities.CustomerKYCDocument>> GetCustomerKycDocumentsDbAsync(int customerId, bool includeInactive = false)
        {
            await using var conn = await Open();
            await using var cmd = SP("dbo.WN_CustomerKYCDocuments_GetByCustomerId", conn);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@IncludeInactive", includeInactive ? 1 : 0);

            var list = new List<WorkNest.Domain.Entities.CustomerKYCDocument>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new WorkNest.Domain.Entities.CustomerKYCDocument
                {
                    Id = r.GetInt32(r.GetOrdinal("Id")),
                    CustomerId = r.GetInt32(r.GetOrdinal("CustomerId")),
                    FolderName = r.GetString(r.GetOrdinal("FolderName")),
                    DocumentTypeId = r.GetInt32(r.GetOrdinal("DocumentTypeId")),
                    DocumentTypeCode = r.IsDBNull(r.GetOrdinal("DocumentTypeCode")) ? null : r.GetString(r.GetOrdinal("DocumentTypeCode")),
                    DocumentTypeName = r.IsDBNull(r.GetOrdinal("DocumentTypeName")) ? null : r.GetString(r.GetOrdinal("DocumentTypeName")),
                    GroupCode = r.IsDBNull(r.GetOrdinal("GroupCode")) ? null : r.GetString(r.GetOrdinal("GroupCode")),
                    AlternativeCode = r.IsDBNull(r.GetOrdinal("AlternativeCode")) ? null : r.GetString(r.GetOrdinal("AlternativeCode")),
                    AllowMultiple = !r.IsDBNull(r.GetOrdinal("AllowMultiple")) && r.GetBoolean(r.GetOrdinal("AllowMultiple")),
                    RequiresHolderName = !r.IsDBNull(r.GetOrdinal("RequiresHolderName")) && r.GetBoolean(r.GetOrdinal("RequiresHolderName")),
                    RequiresExpiry = !r.IsDBNull(r.GetOrdinal("RequiresExpiry")) && r.GetBoolean(r.GetOrdinal("RequiresExpiry")),
                    SlotNo = r.GetByte(r.GetOrdinal("SlotNo")),
                    HolderName = r.IsDBNull(r.GetOrdinal("HolderName")) ? null : r.GetString(r.GetOrdinal("HolderName")),
                    StoredPath = r.GetString(r.GetOrdinal("StoredPath")),
                    OriginalFileName = r.GetString(r.GetOrdinal("OriginalFileName")),
                    FileHash = r.GetString(r.GetOrdinal("FileHash")).Trim(),
                    ExpiryDate = r.IsDBNull(r.GetOrdinal("ExpiryDate")) ? null : r.GetDateTime(r.GetOrdinal("ExpiryDate")),
                    VersionNo = r.GetInt32(r.GetOrdinal("VersionNo")),
                    IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
                    Status = r.GetByte(r.GetOrdinal("Status")),
                    Remarks = r.IsDBNull(r.GetOrdinal("Remarks")) ? null : r.GetString(r.GetOrdinal("Remarks")),
                    VerifiedBy = r.IsDBNull(r.GetOrdinal("VerifiedBy")) ? null : r.GetInt32(r.GetOrdinal("VerifiedBy")),
                    VerifiedByName = r.IsDBNull(r.GetOrdinal("VerifiedByName")) ? null : r.GetString(r.GetOrdinal("VerifiedByName")),
                    VerifiedOn = r.IsDBNull(r.GetOrdinal("VerifiedOn")) ? null : r.GetDateTime(r.GetOrdinal("VerifiedOn")),
                    UploadedBy = r.GetInt32(r.GetOrdinal("UploadedBy")),
                    UploadedByName = r.IsDBNull(r.GetOrdinal("UploadedByName")) ? null : r.GetString(r.GetOrdinal("UploadedByName")),
                    UploadedOn = r.GetDateTime(r.GetOrdinal("UploadedOn")),
                    ReplacedBy = r.IsDBNull(r.GetOrdinal("ReplacedBy")) ? null : r.GetInt32(r.GetOrdinal("ReplacedBy")),
                    ReplacedOn = r.IsDBNull(r.GetOrdinal("ReplacedOn")) ? null : r.GetDateTime(r.GetOrdinal("ReplacedOn"))
                });
            }
            return list;
        }

        public async Task<int> InsertOrReplaceCustomerKycDocumentDbAsync(
            int customerId,
            string folderName,
            int documentTypeId,
            byte slotNo,
            string? holderName,
            string storedPath,
            string originalFileName,
            string fileHash,
            DateTime? expiryDate,
            int uploadedBy)
        {
            await using var conn = await Open();
            await using var cmd = SP("dbo.WN_CustomerKYCDocuments_InsertOrReplace", conn);
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@FolderName", folderName);
            cmd.Parameters.AddWithValue("@DocumentTypeId", documentTypeId);
            cmd.Parameters.AddWithValue("@SlotNo", slotNo);
            cmd.Parameters.AddWithValue("@HolderName", (object?)holderName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@StoredPath", storedPath);
            cmd.Parameters.AddWithValue("@OriginalFileName", originalFileName);
            cmd.Parameters.AddWithValue("@FileHash", fileHash);
            cmd.Parameters.AddWithValue("@ExpiryDate", (object?)expiryDate?.Date ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UploadedBy", uploadedBy);

            var outParam = new SqlParameter("@NewDocId", System.Data.SqlDbType.Int)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            cmd.Parameters.Add(outParam);

            await cmd.ExecuteNonQueryAsync();
            return (int)(outParam.Value ?? 0);
        }

        public async Task SetCustomerKycDocumentStatusDbAsync(int documentId, byte status, string? remarks, int verifiedBy)
        {
            await using var conn = await Open();
            await using var cmd = SP("dbo.WN_CustomerKYCDocuments_SetStatus", conn);
            cmd.Parameters.AddWithValue("@DocumentId", documentId);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Remarks", (object?)remarks ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@VerifiedBy", verifiedBy);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<WorkNest.Domain.Entities.CustomerKYCDocument?> GetCustomerKycDocumentByIdDbAsync(int documentId)
        {
            await using var conn = await Open();
            await using var cmd = SP("dbo.WN_CustomerKYCDocuments_GetById", conn);
            cmd.Parameters.AddWithValue("@Id", documentId);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return new WorkNest.Domain.Entities.CustomerKYCDocument
                {
                    Id = r.GetInt32(r.GetOrdinal("Id")),
                    CustomerId = r.GetInt32(r.GetOrdinal("CustomerId")),
                    FolderName = r.GetString(r.GetOrdinal("FolderName")),
                    DocumentTypeId = r.GetInt32(r.GetOrdinal("DocumentTypeId")),
                    DocumentTypeCode = r.IsDBNull(r.GetOrdinal("DocumentTypeCode")) ? null : r.GetString(r.GetOrdinal("DocumentTypeCode")),
                    DocumentTypeName = r.IsDBNull(r.GetOrdinal("DocumentTypeName")) ? null : r.GetString(r.GetOrdinal("DocumentTypeName")),
                    GroupCode = r.IsDBNull(r.GetOrdinal("GroupCode")) ? null : r.GetString(r.GetOrdinal("GroupCode")),
                    AlternativeCode = r.IsDBNull(r.GetOrdinal("AlternativeCode")) ? null : r.GetString(r.GetOrdinal("AlternativeCode")),
                    AllowMultiple = !r.IsDBNull(r.GetOrdinal("AllowMultiple")) && r.GetBoolean(r.GetOrdinal("AllowMultiple")),
                    RequiresHolderName = !r.IsDBNull(r.GetOrdinal("RequiresHolderName")) && r.GetBoolean(r.GetOrdinal("RequiresHolderName")),
                    RequiresExpiry = !r.IsDBNull(r.GetOrdinal("RequiresExpiry")) && r.GetBoolean(r.GetOrdinal("RequiresExpiry")),
                    SlotNo = r.GetByte(r.GetOrdinal("SlotNo")),
                    HolderName = r.IsDBNull(r.GetOrdinal("HolderName")) ? null : r.GetString(r.GetOrdinal("HolderName")),
                    StoredPath = r.GetString(r.GetOrdinal("StoredPath")),
                    OriginalFileName = r.GetString(r.GetOrdinal("OriginalFileName")),
                    FileHash = r.GetString(r.GetOrdinal("FileHash")).Trim(),
                    ExpiryDate = r.IsDBNull(r.GetOrdinal("ExpiryDate")) ? null : r.GetDateTime(r.GetOrdinal("ExpiryDate")),
                    VersionNo = r.GetInt32(r.GetOrdinal("VersionNo")),
                    IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
                    Status = r.GetByte(r.GetOrdinal("Status")),
                    Remarks = r.IsDBNull(r.GetOrdinal("Remarks")) ? null : r.GetString(r.GetOrdinal("Remarks")),
                    VerifiedBy = r.IsDBNull(r.GetOrdinal("VerifiedBy")) ? null : r.GetInt32(r.GetOrdinal("VerifiedBy")),
                    VerifiedOn = r.IsDBNull(r.GetOrdinal("VerifiedOn")) ? null : r.GetDateTime(r.GetOrdinal("VerifiedOn")),
                    UploadedBy = r.GetInt32(r.GetOrdinal("UploadedBy")),
                    UploadedOn = r.GetDateTime(r.GetOrdinal("UploadedOn")),
                    ReplacedBy = r.IsDBNull(r.GetOrdinal("ReplacedBy")) ? null : r.GetInt32(r.GetOrdinal("ReplacedBy")),
                    ReplacedOn = r.IsDBNull(r.GetOrdinal("ReplacedOn")) ? null : r.GetDateTime(r.GetOrdinal("ReplacedOn"))
                };
            }
            return null;
        }

        public async Task<(IEnumerable<WorkNest.Application.DTOs.Kyc.KycCustomerListItemViewModel> Rows, int Total)> GetCustomersKycListDbAsync(int page, int limit, string? search, int? locationId = null)
        {
            await using var conn = await Open();
            await using var cmd = SP("dbo.WN_Customers_GetKycList", conn);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);

            var list = new List<WorkNest.Application.DTOs.Kyc.KycCustomerListItemViewModel>();
            int total = 0;

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                if (total == 0 && !r.IsDBNull(r.GetOrdinal("TotalRecords")))
                {
                    total = r.GetInt32(r.GetOrdinal("TotalRecords"));
                }

                var company = r.IsDBNull(r.GetOrdinal("Company")) ? null : r.GetString(r.GetOrdinal("Company"));
                var (category, _, _) = WorkNest.Application.DTOs.Kyc.KycCategoryMapper.ResolveCategory(company);
                var totalDocs = r.GetInt32(r.GetOrdinal("TotalActiveDocs"));
                var verifiedDocs = r.GetInt32(r.GetOrdinal("VerifiedDocsCount"));
                var pendingDocs = r.GetInt32(r.GetOrdinal("PendingDocsCount"));
                var rejectedDocs = r.GetInt32(r.GetOrdinal("RejectedDocsCount"));

                string overallStatus = "Not Uploaded";
                string badgeClass = "badge-secondary";

                if (totalDocs > 0)
                {
                    if (rejectedDocs > 0)
                    {
                        overallStatus = "Action Required";
                        badgeClass = "badge-danger";
                    }
                    else if (pendingDocs > 0)
                    {
                        overallStatus = "Under Review";
                        badgeClass = "badge-warning";
                    }
                    else if (verifiedDocs > 0)
                    {
                        overallStatus = "Verified";
                        badgeClass = "badge-success";
                    }
                }

                list.Add(new WorkNest.Application.DTOs.Kyc.KycCustomerListItemViewModel
                {
                    Id = r.GetInt32(r.GetOrdinal("Id")),
                    IdGUID = r.GetGuid(r.GetOrdinal("IdGUID")),
                    Code = r.GetString(r.GetOrdinal("Code")),
                    FullName = r.GetString(r.GetOrdinal("FullName")),
                    Company = company,
                    Email = r.GetString(r.GetOrdinal("Email")),
                    PhoneNumber = r.IsDBNull(r.GetOrdinal("PhoneNumber")) ? null : r.GetString(r.GetOrdinal("PhoneNumber")),
                    Category = category,
                    TotalActiveDocs = totalDocs,
                    VerifiedDocsCount = verifiedDocs,
                    PendingDocsCount = pendingDocs,
                    RejectedDocsCount = rejectedDocs,
                    OverallStatus = overallStatus,
                    OverallBadgeClass = badgeClass,
                    CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt"))
                });
            }

            return (list, total);
        }

        public async Task<IDictionary<string, object?>?> GetCustomerByIdOrGuidDbAsync(string idOrGuid)
        {
            await using var conn = await Open();
            await using var cmd = new SqlCommand(@"
                SELECT 
                    c.Id, 
                    c.IdGUID, 
                    c.Code, 
                    c.FirstName, 
                    c.LastName, 
                    LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))) AS FullName,
                    c.Company, 
                    c.Email, 
                    c.PhoneNumber, 
                    c.CnicOrPassport, 
                    c.Address, 
                    c.CityId, 
                    ci.Description AS CityName,
                    c.IsActive, 
                    c.Notes, 
                    c.CreatedAt,
                    c.UserId
                FROM dbo.WN_Customers c WITH (NOLOCK)
                LEFT JOIN dbo.City ci WITH (NOLOCK) ON ci.Id = c.CityId
                WHERE (@IsGuid = 1 AND c.IdGUID = @GuidVal)
                   OR (@IsInt = 1 AND c.Id = @IntVal)
                   OR (c.Code = @CodeVal)", conn);

            bool isGuid = Guid.TryParse(idOrGuid, out var g);
            bool isInt = int.TryParse(idOrGuid, out var id);

            cmd.Parameters.AddWithValue("@IsGuid", isGuid ? 1 : 0);
            cmd.Parameters.AddWithValue("@GuidVal", isGuid ? g : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@IsInt", isInt ? 1 : 0);
            cmd.Parameters.AddWithValue("@IntVal", isInt ? id : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@CodeVal", idOrGuid);

            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task<bool> CustomerBelongsToLocationDbAsync(int customerId, int locationId)
        {
            await using var conn = await Open();
            await using var cmd = new SqlCommand(@"
                SELECT TOP 1 1
                FROM dbo.WN_Customers c WITH (NOLOCK)
                LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = c.UserId
                LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.CustomerCode = c.Code OR (c.UserId IS NOT NULL AND b.UserId = c.UserId)
                LEFT JOIN dbo.WN_Spaces sb WITH (NOLOCK) ON sb.Id = b.SpaceId
                WHERE c.Id = @CustomerId
                  AND (sb.LocationId = @LocationId OR u.LocationId = @LocationId)", conn);

            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@LocationId", locationId);

            var res = await cmd.ExecuteScalarAsync();
            return res != null && res != DBNull.Value;
        }

        public async Task<WorkNest.Application.Services.InvoiceCalculationResult> CalculateInvoiceAmountsDbAsync(WorkNest.Application.Services.InvoiceCalculationRequest request)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_CalculateInvoiceAmounts", c);
            
            decimal monthlyRent = request.MonthlyRent;
            if (monthlyRent <= 0)
            {
                if (request.RoomPrice > 0) monthlyRent = request.RoomPrice;
                else if (request.SeatPrice > 0) monthlyRent = request.SeatPrice * (request.Capacity > 0 ? request.Capacity : 1);
                else if (request.SubtotalAmount > 0 && request.BillingPeriodMonths > 0) monthlyRent = request.SubtotalAmount / request.BillingPeriodMonths;
            }

            decimal discountPct = request.DiscountPercentage > 0 ? request.DiscountPercentage :
                (string.Equals(request.DiscountType, "Percentage", StringComparison.OrdinalIgnoreCase) ? request.DiscountValue : 0m);

            decimal discountAmt = request.DiscountAmount > 0 ? request.DiscountAmount :
                (!string.Equals(request.DiscountType, "Percentage", StringComparison.OrdinalIgnoreCase) ? request.DiscountValue : 0m);

            cmd.Parameters.AddWithValue("@MonthlyRent", monthlyRent);
            cmd.Parameters.AddWithValue("@SeatCapacity", (decimal)(request.Capacity > 0 ? request.Capacity : 1));
            cmd.Parameters.AddWithValue("@BillingMonths", request.BillingPeriodMonths > 0 ? request.BillingPeriodMonths : 1);
            cmd.Parameters.AddWithValue("@DiscountPercentage", discountPct);
            cmd.Parameters.AddWithValue("@DiscountAmount", discountAmt);
            cmd.Parameters.AddWithValue("@SecurityDepositMonths", request.SecurityDepositMonths);
            cmd.Parameters.AddWithValue("@SecurityDepositOverride", request.SecurityDeposit > 0 ? (object)request.SecurityDeposit : DBNull.Value);
            cmd.Parameters.AddWithValue("@StartDateTime", (object?)request.StartOn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsDepositInstallmentEnabled", request.SecurityDepositInstallments > 1);
            cmd.Parameters.AddWithValue("@DepositInstallmentsCount", request.SecurityDepositInstallments > 0 ? request.SecurityDepositInstallments : 1);
            cmd.Parameters.AddWithValue("@ReturnResultSet", true);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                int proratedDays = Convert.ToInt32(r.GetValue(r.GetOrdinal("ProrationDays")));
                int daysInMonth = Convert.ToInt32(r.GetValue(r.GetOrdinal("TotalDaysInStartMonth")));

                DateTime pStart = request.StartOn ?? DateTime.Today;
                int bMonths = request.BillingPeriodMonths > 0 ? request.BillingPeriodMonths : 1;
                DateTime pEnd;
                if (proratedDays > 0)
                {
                    int startDay = pStart.Day;
                    if (startDay < 15)
                        pEnd = new DateTime(pStart.Year, pStart.Month, 1).AddMonths(bMonths).AddDays(-1);
                    else
                        pEnd = new DateTime(pStart.Year, pStart.Month, 1).AddMonths(bMonths + 1).AddDays(-1);
                }
                else
                {
                    pEnd = pStart.AddMonths(bMonths).AddDays(-1);
                }

                return new WorkNest.Application.Services.InvoiceCalculationResult
                {
                    MonthlyRent = monthlyRent,
                    BillingPeriodMonths = bMonths,
                    ContractPeriodMonths = request.ContractPeriodMonths > 0 ? request.ContractPeriodMonths : 12,
                    BillingPeriodStart = pStart,
                    BillingPeriodEnd = pEnd,
                    Rent = Convert.ToDecimal(r.GetValue(r.GetOrdinal("Rent"))),
                    Discount = Convert.ToDecimal(r.GetValue(r.GetOrdinal("Discount"))),
                    ServiceCharge = Convert.ToDecimal(r.GetValue(r.GetOrdinal("ServiceCharges"))),
                    Tax = Convert.ToDecimal(r.GetValue(r.GetOrdinal("TaxOnServiceCharges"))),
                    DepositBase = Convert.ToDecimal(r.GetValue(r.GetOrdinal("BaseDeposit"))),
                    DepositDiscount = Convert.ToDecimal(r.GetValue(r.GetOrdinal("DepositDiscount"))),
                    DepositAfterDiscount = Convert.ToDecimal(r.GetValue(r.GetOrdinal("DepositAfterDiscount"))),
                    DepositFirstInstallment = Convert.ToDecimal(r.GetValue(r.GetOrdinal("FirstInvoiceDeposit"))),
                    DepositInstallments = request.SecurityDepositInstallments,
                    GrandTotal = Convert.ToDecimal(r.GetValue(r.GetOrdinal("GrandTotal"))),
                    IsProrated = proratedDays > 0,
                    ProratedDays = proratedDays,
                    DaysInStartMonth = daysInMonth,
                    ProratedCurrentMonthAmount = proratedDays > 0 && daysInMonth > 0 ? Math.Round(((decimal)proratedDays / daysInMonth) * monthlyRent, 2, MidpointRounding.AwayFromZero) : 0m
                };
            }

            return WorkNest.Application.Services.InvoiceCalculationEngine.CalculateInvoice(request);
        }
    }
}




