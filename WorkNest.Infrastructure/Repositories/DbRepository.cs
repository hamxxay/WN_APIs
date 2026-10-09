
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
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
        private readonly IBusinessClock _clock;

        public DbRepository(IConfiguration configuration, IHttpContextAccessor? httpContextAccessor = null, IBusinessClock? clock = null)
        {
            _clock = clock ?? BusinessClock.Default;
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _httpContextAccessor = httpContextAccessor;
        }

        private static object? N(object? v) => v is DBNull ? null : v;

        private static IDictionary<string, object?> ToDict(SqlDataReader r)
        {
            var d = new Dictionary<string, object?>(r.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < r.FieldCount; i++)
            {
                var name = r.GetName(i);
                d[name] = DbDateTimeKinds.Normalize(name, N(r.GetValue(i))); // UTC audit columns get Kind = Utc (JSON "Z")
            }
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

                    // Identity for the audit session context comes only from the JWT; the x-user-email / x-user-id
                    // request headers are client-controlled and are no longer used.
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
            decimal? withholdingTaxRate = null,
            bool? sendWhtInvoice = null)
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
                // WHT columns are only written when a value is passed (Create Quotation no longer passes one: WHT is
                // chosen on the Initial Invoice Preview), so WN_Quotations.WHTaxId keeps its default.
                if (withholdingTaxRate.HasValue)
                    updateParts.Add($"{await WhtIdColumnAsync(c, "dbo.WN_Quotations", "WithholdingTaxRate")} = @WTR");
                if (sendWhtInvoice == true) updateParts.Add("SendWhtInvoice = 1"); // only when ticked: untouched quotations keep the default 0

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
                if (sendWhtInvoice == true) result["SendWhtInvoice"] = true;
            }

            return result;
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetOfferingTypesAsync(bool? activeOnly = null, int? locationId = null)
        {
            await using var c = await Open();
            string sql = "SELECT Id, Description, DiscountCap, ISNULL(Status, 1) AS Status, LocationId FROM dbo.WN_OfferingType WHERE 1=1";
            if (activeOnly.HasValue)
            {
                sql += activeOnly.Value ? " AND ISNULL(Status, 1) = 1" : " AND ISNULL(Status, 1) = 0";
            }
            if (locationId.HasValue && locationId.Value > 0)
            {
                sql += $" AND (LocationId = {locationId.Value} OR LocationId IS NULL)";
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

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetQuotationResponsesAsync(
            int page,
            int limit,
            string? search)
        {
            await using var c = await Open();
            int offset = Math.Max(0, (page - 1) * limit);

            string whereClause = "";
            if (!string.IsNullOrWhiteSpace(search))
            {
                string escaped = search.Replace("'", "''");
                whereClause = $"WHERE (q.QuotationNumber LIKE '%{escaped}%' OR c.FirstName LIKE '%{escaped}%' OR c.LastName LIKE '%{escaped}%' OR c.Company LIKE '%{escaped}%' OR c.Email LIKE '%{escaped}%' OR qr.Note LIKE '%{escaped}%' OR qr.ResponseType LIKE '%{escaped}%')";
            }

            string countSql = $@"
                SELECT COUNT(1)
                FROM dbo.WN_QuotationResponses qr WITH (NOLOCK)
                LEFT JOIN dbo.WN_Quotations q WITH (NOLOCK) ON q.Id = qr.QuotationId
                LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.Id = COALESCE(qr.RespondedByCustomerId, q.CustomerId)
                {whereClause};";

            await using var countCmd = new SqlCommand(countSql, c);
            int total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            string sql = $@"
                SELECT 
                    qr.Id,
                    qr.QuotationId,
                    qr.Version,
                    qr.ResponseType,
                    qr.Note,
                    qr.RespondedByUserId,
                    qr.RespondedByCustomerId,
                    qr.RespondedDate,
                    q.QuotationNumber,
                    q.Status AS QuotationStatus,
                    COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), c.Company, 'Customer') AS CustomerName,
                    c.Email AS CustomerEmail,
                    c.PhoneNumber AS CustomerPhone,
                    s.Name AS SpaceName
                FROM dbo.WN_QuotationResponses qr WITH (NOLOCK)
                LEFT JOIN dbo.WN_Quotations q WITH (NOLOCK) ON q.Id = qr.QuotationId
                LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.Id = COALESCE(qr.RespondedByCustomerId, q.CustomerId)
                LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = q.SpaceId
                {whereClause}
                ORDER BY qr.RespondedDate DESC
                OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY;";

            await using var cmd = new SqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            var resRows = await ReadAll(r);
            return (resRows, total);
        }

        public async Task<IDictionary<string, object?>> ConvertQuotationToBookingAsync(
            int quotationId,
            int? createdById)
        {
            await using var c = await Open();
            
            var quotation = await GetQuotationByIdAsync(quotationId);

            await using var cmd = SP("dbo.WN_Quotations_ConvertToBooking", c);
            cmd.Parameters.AddWithValue("@QuotationId", quotationId);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();
            var result = new Dictionary<string, object?>();
            if (await r.ReadAsync())
                result = new Dictionary<string, object?>(ToDict(r), StringComparer.OrdinalIgnoreCase);
            await r.CloseAsync();

            if (result.TryGetValue("BookingId", out var bIdObj) && bIdObj != null && Convert.ToInt32(bIdObj) > 0 && quotation != null)
            {
                int bookingId = Convert.ToInt32(bIdObj);
                
                int capacity = quotation.TryGetValue("Capacity", out var capObj) && capObj != null ? Convert.ToInt32(capObj) : 1;
                decimal perSeatPrice = quotation.TryGetValue("PerSeatBasePrice", out var psObj) && psObj != null ? Convert.ToDecimal(psObj) : 0m;
                decimal monthlyPrice = quotation.TryGetValue("MonthlyBasePrice", out var mbObj) && mbObj != null ? Convert.ToDecimal(mbObj) : 0m;
                int bpm = quotation.TryGetValue("BillingPeriodMonths", out var bpmObj) && bpmObj != null ? Convert.ToInt32(bpmObj) : 3;
                int secMonths = quotation.TryGetValue("SecurityDepositMonths", out var smObj) && smObj != null ? Convert.ToInt32(smObj) : 2;
                decimal secDeposit = quotation.TryGetValue("SecurityDeposit", out var sdObj) && sdObj != null ? Convert.ToDecimal(sdObj) : 0m;
                string discountType = quotation.TryGetValue("DiscountType", out var dtObj) && dtObj != null ? dtObj.ToString()! : "Percentage";
                decimal discountPct = quotation.TryGetValue("DiscountPercentage", out var dpObj) && dpObj != null ? Convert.ToDecimal(dpObj) : 0m;
                decimal discountVal = quotation.TryGetValue("DiscountValue", out var dvObj) && dvObj != null && Convert.ToDecimal(dvObj) > 0
                    ? Convert.ToDecimal(dvObj)
                    : (quotation.TryGetValue("DiscountAmount", out var daObj) && daObj != null ? Convert.ToDecimal(daObj) : 0m);
                decimal subtotal = quotation.TryGetValue("SubtotalAmount", out var stObj) && stObj != null ? Convert.ToDecimal(stObj) : 0m;
                // The booking gets the quotation's WN_WHTaxRate Id only when the quotation has WHT ticked; otherwise no WHT rate.
                bool quotationSendsWht = await GetQuotationSendWhtInvoiceAsync(quotationId);
                var whtObj = WhtIdValue(quotation, "WithholdingTaxRate");
                decimal? whtRate = quotationSendsWht && whtObj is not null ? Convert.ToDecimal(whtObj) : null;

                decimal resolvedMonthlyRent = monthlyPrice > 0 ? monthlyPrice : (perSeatPrice > 0 ? perSeatPrice * Math.Max(1, capacity) : (subtotal > 0 && bpm > 0 ? subtotal / bpm : 0m));
                if (resolvedMonthlyRent <= 0 && subtotal > 0) resolvedMonthlyRent = subtotal;

                decimal baseDeposit = secMonths * resolvedMonthlyRent;
                decimal discPctForDeposit = (discountType.Equals("Percentage", StringComparison.OrdinalIgnoreCase) || discountType.Equals("Percent", StringComparison.OrdinalIgnoreCase))
                    ? discountPct
                    : (resolvedMonthlyRent > 0 && discountVal > 0 ? (discountVal / resolvedMonthlyRent) * 100m : 0m);
                decimal resolvedSecDeposit = secDeposit > 0 ? secDeposit : (baseDeposit > 0 ? Math.Max(0m, Math.Round(baseDeposit * (1 - (discPctForDeposit / 100m)), 2)) : 0m);

                string syncSql = @"
                    UPDATE dbo.WN_Bookings
                    SET MonthlyRent = @MonthlyRent,
                        SubtotalAmount = @SubtotalAmount,
                        BillingPeriodMonths = @BPM,
                        AdvanceRentMonths = @BPM,
                        SecurityDepositMonths = @SecMonths,
                        SecurityDepositRequired = @SecurityDeposit,
                        DiscountType = @DiscountType,
                        DiscountPercentage = @DiscountPct,
                        DiscountValue = @DiscountVal,
                        DiscountAmount = @DiscountVal,
                        WHTRate = @WhtRate
                    WHERE Id = @BookingId;

                    -- WHT invoice flag travels with the quotation (rate already copied above as WHTRate).
                    IF COL_LENGTH('dbo.WN_Bookings', 'SendWhtInvoice') IS NOT NULL AND COL_LENGTH('dbo.WN_Quotations', 'SendWhtInvoice') IS NOT NULL
                        EXEC sp_executesql
                            N'UPDATE b SET SendWhtInvoice = ISNULL(q.SendWhtInvoice, 0)
                                FROM dbo.WN_Bookings b JOIN dbo.WN_Quotations q ON q.Id = @QID
                               WHERE b.Id = @BID',
                            N'@QID INT, @BID INT', @QID = @QuotationId, @BID = @BookingId;

                    UPDATE dbo.WN_BookingDetails
                    SET SecurityDeposit = @SecurityDeposit
                    WHERE BookingGuid = (SELECT IdGUID FROM dbo.WN_Bookings WHERE Id = @BookingId);

                    UPDATE dbo.WN_Invoices
                    SET AdvanceRentMonths = @BPM,
                        BillingPeriodMonths = @BPM,
                        SecurityDepositMonths = @SecMonths,
                        SecurityDepositAmount = @SecurityDeposit
                    WHERE BookingId = @BookingId;";

                syncSql = syncSql.Replace("WHTRate = @WhtRate", (await WhtIdColumnAsync(c, "dbo.WN_Bookings", "WHTRate")) + " = @WhtRate");
                await using var syncCmd = new SqlCommand(syncSql, c);
                syncCmd.Parameters.AddWithValue("@MonthlyRent", resolvedMonthlyRent);
                syncCmd.Parameters.AddWithValue("@SubtotalAmount", subtotal > 0 ? subtotal : (resolvedMonthlyRent * bpm));
                syncCmd.Parameters.AddWithValue("@BPM", bpm);
                syncCmd.Parameters.AddWithValue("@SecMonths", secMonths);
                syncCmd.Parameters.AddWithValue("@SecurityDeposit", resolvedSecDeposit);
                syncCmd.Parameters.AddWithValue("@DiscountType", discountType);
                syncCmd.Parameters.AddWithValue("@DiscountPct", discountPct);
                syncCmd.Parameters.AddWithValue("@DiscountVal", discountVal);
                syncCmd.Parameters.AddWithValue("@WhtRate", (object?)whtRate ?? DBNull.Value);
                syncCmd.Parameters.AddWithValue("@BookingId", bookingId);
                syncCmd.Parameters.AddWithValue("@QuotationId", quotationId);
                await syncCmd.ExecuteNonQueryAsync();
            }

            return result;
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
            string? shiftType = "24_7", int? capacity = null, decimal? perSeatBasePrice = null, bool sendWhtInvoice = false, decimal? whtRate = null)
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
            // Only sent when set, so the call still works before the WN_Bookings_Insert update adds @PerSeatBasePrice.
            if (capacity is > 0) cmd.Parameters.AddWithValue("@Capacity", capacity.Value);
            if (perSeatBasePrice is > 0) cmd.Parameters.AddWithValue("@PerSeatBasePrice", perSeatBasePrice.Value);
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
                decimal baseDeposit = secDepM * monthlyRent;
                decimal discPct = (discountType == "Percentage" || discountType == "Percent")
                    ? (discountValue > 0 ? discountValue : discountPercentage)
                    : (monthlyRent > 0 && discountValue > 0 ? (discountValue / monthlyRent) * 100m : 0m);
                decimal secDepReq = securityDepositOverride.HasValue
                    ? securityDepositOverride.Value
                    : (baseDeposit > 0 ? Math.Max(0, Math.Round(baseDeposit * (1 - (discPct / 100m)), 2)) : 0m);

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
                if (sendWhtInvoice && whtRate is > 0) { updateParts.Add("SendWhtInvoice = 1"); updateParts.Add((await WhtIdColumnAsync(c, "dbo.WN_Bookings", "WHTRate")) + " = @WHTR"); } // rate snapshot

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
                if (sendWhtInvoice && whtRate is > 0) upd.Parameters.AddWithValue("@WHTR", whtRate.Value);
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
                    var validity = result.TryGetValue("ChallanValidUntil", out var vu) && vu is not null ? Convert.ToDateTime(vu) : _clock.Today.AddDays(5);

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

        public async Task<List<int>> GetPendingBookingsWithPaidFirstInvoiceDbAsync(IEnumerable<int> pendingStatusIds, IEnumerable<int> paidStatusIds, IEnumerable<int> voidStatusIds)
        {
            // IDs are ints from code (OrderStatus / booking status lookups), never user input.
            static string List(IEnumerable<int> ids) { var l = ids.Distinct().ToList(); return l.Count == 0 ? "-1" : string.Join(",", l); }
            await using var c = await Open();
            var sql = $@"
                SELECT b.Id
                FROM dbo.WN_Bookings b WITH (NOLOCK)
                CROSS APPLY (SELECT TOP 1 i.StatusId
                               FROM dbo.WN_Invoices i WITH (NOLOCK)
                              WHERE i.BookingId = b.Id AND i.StatusId NOT IN ({List(voidStatusIds)})
                              ORDER BY i.IssuedOn, i.Id) fi
                WHERE ISNULL(b.IsDeleted, 0) = 0
                  AND b.BookingStatusId IN ({List(pendingStatusIds)})
                  AND fi.StatusId IN ({List(paidStatusIds)});";
            await using var cmd = new SqlCommand(sql, c);
            var ids = new List<int>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
            return ids;
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetCustomerAgreementsDbAsync(int customerId)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT a.Id, a.QuotationId, q.QuotationNumber, a.BookingId, a.Status, a.SentDate, a.SignedDate,
                       a.SignedPdfUploadedAt, CAST(CASE WHEN a.SignedPdfPath IS NULL OR a.SignedPdfPath = '' THEN 0 ELSE 1 END AS BIT) AS HasSignedCopy,
                       a.FeeAmount, a.SecurityDeposit, a.CustomerName, a.CompanyName,
                       COALESCE(NULLIF(s.Name, ''), s.Code) AS SpaceName
                  FROM dbo.WN_Agreements a WITH (NOLOCK)
                  JOIN dbo.WN_Quotations q WITH (NOLOCK) ON q.Id = a.QuotationId
                  LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = q.SpaceId
                 WHERE q.CustomerId = @CustomerId
                 ORDER BY a.Id DESC;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task SetAgreementStatusDbAsync(int agreementId, string status, DateTime? signedDate = null)
        {
            await using var c = await Open();
            const string sql = @"
                UPDATE dbo.WN_Agreements
                   SET Status = @Status, SignedDate = COALESCE(@Signed, SignedDate)
                 WHERE Id = @Id AND BookingId IS NULL;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = agreementId;
            cmd.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = status;
            cmd.Parameters.Add("@Signed", SqlDbType.DateTime2).Value = (object?)signedDate?.Date ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> IsInvoiceOwnedByDbAsync(int invoiceId, string email)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.WN_Invoices i WITH (NOLOCK)
                    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
                    JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = ISNULL(i.UserId, b.UserId)
                    WHERE i.Id = @Id AND u.Email = @Email) THEN 1 ELSE 0 END;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = invoiceId;
            cmd.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = email;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        public async Task<bool> IsBookingOwnedByDbAsync(int bookingId, string email)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.WN_Bookings b WITH (NOLOCK)
                    JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = b.UserId
                    WHERE b.Id = @Id AND u.Email = @Email) THEN 1 ELSE 0 END;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = bookingId;
            cmd.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = email;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        public async Task<IDictionary<string, object?>?> GetBookingSummaryRowDbAsync(int bookingId)
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand("SELECT v.* FROM dbo.WN_vw_BookingSummary v WITH (NOLOCK) WHERE v.BookingId = @Id;", c);
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = bookingId;
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        public async Task ApplyAgreementSignedDateDbAsync(int agreementId, int? bookingId, DateTime signedDate)
        {
            await using var c = await Open();
            const string sql = @"
                UPDATE dbo.WN_Agreements SET SignedDate = @Signed WHERE Id = @AgreementId;
                IF @BookingId IS NOT NULL
                BEGIN
                    -- The booking is dated on the agreement; the challan is valid for 7 days from that date.
                    UPDATE dbo.WN_Bookings
                       SET BookingDate = @Signed, TransactionDate = @Signed, ValidityDate = DATEADD(DAY, 7, @Signed)
                     WHERE Id = @BookingId;
                    UPDATE dbo.WN_Challans
                       SET IssuedOn = @Signed, ValidUntil = DATEADD(DAY, 7, @Signed)
                     WHERE BookingId = @BookingId;
                END";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@AgreementId", SqlDbType.Int).Value = agreementId;
            cmd.Parameters.Add("@BookingId", SqlDbType.Int).Value = (object?)bookingId ?? DBNull.Value;
            cmd.Parameters.Add("@Signed", SqlDbType.DateTime2).Value = signedDate.Date;
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

        // ---- Tour inquiry feedback (dbo.WN_ContactFeedback). SQL error 208 = table not created yet. ----

        public async Task<bool> InsertContactFeedbackAsync(int contactId, string outcome, string? reason, DateTime? followUpOn, int? quotationId, int? createdById)
        {
            try
            {
                await using var c = await Open();
                await using var cmd = new SqlCommand(@"
                    INSERT INTO dbo.WN_ContactFeedback (ContactId, Outcome, Reason, FollowUpOn, QuotationId, CreatedById)
                    VALUES (@ContactId, @Outcome, @Reason, @FollowUpOn, @QuotationId, @CreatedById);", c);
                cmd.Parameters.AddWithValue("@ContactId", contactId);
                cmd.Parameters.AddWithValue("@Outcome", outcome);
                cmd.Parameters.AddWithValue("@Reason", (object?)reason ?? DBNull.Value);
                cmd.Parameters.Add("@FollowUpOn", SqlDbType.Date).Value = (object?)followUpOn?.Date ?? DBNull.Value;
                cmd.Parameters.AddWithValue("@QuotationId", (object?)quotationId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
                return true;
            }
            catch (SqlException ex) when (ex.Number == 208) { return false; }
        }

        public async Task<Dictionary<int, IDictionary<string, object?>>> GetLatestContactFeedbackAsync(IEnumerable<int> contactIds)
        {
            var result = new Dictionary<int, IDictionary<string, object?>>();
            var ids = contactIds.Where(i => i > 0).Distinct().ToList();
            if (ids.Count == 0) return result;
            try
            {
                await using var c = await Open();
                // Ids are ints from the inquiry list, never user text.
                await using var cmd = new SqlCommand($@"
                    SELECT f.ContactId, f.Outcome, f.Reason, f.FollowUpOn, f.QuotationId, f.CreatedOn
                    FROM (SELECT *, ROW_NUMBER() OVER (PARTITION BY ContactId ORDER BY Id DESC) AS rn
                          FROM dbo.WN_ContactFeedback WITH (NOLOCK)
                          WHERE ContactId IN ({string.Join(",", ids)})) f
                    WHERE f.rn = 1;", c);
                await using var r = await cmd.ExecuteReaderAsync();
                foreach (var row in await ReadAll(r))
                    result[Convert.ToInt32(row["ContactId"])] = row;
            }
            catch (SqlException ex) when (ex.Number == 208) { }
            return result;
        }

        public async Task<List<(int ContactId, DateTime FollowUpOn)>> GetDueContactFollowUpsAsync(DateTime today)
        {
            var list = new List<(int, DateTime)>();
            try
            {
                await using var c = await Open();
                await using var cmd = new SqlCommand(@"
                    SELECT f.ContactId, f.FollowUpOn
                    FROM (SELECT ContactId, Outcome, FollowUpOn, ROW_NUMBER() OVER (PARTITION BY ContactId ORDER BY Id DESC) AS rn
                          FROM dbo.WN_ContactFeedback WITH (NOLOCK)) f
                    JOIN dbo.WN_Contacts ct WITH (NOLOCK) ON ct.Id = f.ContactId
                    WHERE f.rn = 1 AND f.Outcome = N'future_prospect' AND f.FollowUpOn <= @Today;", c);
                cmd.Parameters.Add("@Today", SqlDbType.Date).Value = today.Date;
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) list.Add((r.GetInt32(0), r.GetDateTime(1)));
            }
            catch (SqlException ex) when (ex.Number == 208) { }
            return list;
        }

        // --- Dashboard ---

        /// <summary>
        /// Admin dashboard overview (read-only): [0] headline numbers, [1] last 6 months,
        /// [2] overdue invoices, [3] leases ending soon, [4] bookings waiting for confirmation,
        /// [5] receivables aging, [6] occupancy by space type, [7] top customers, [8] lease expiries next 6 months.
        /// Live bookings = BookingStatusId 1, 2, 5, 33 (old + WN_BookingStatuses pending / confirmed).
        /// </summary>
        public async Task<List<List<IDictionary<string, object?>>>> GetDashboardOverviewDbAsync(IReadOnlyCollection<int>? locationIds, int endingSoonDays,
            IEnumerable<int> openInvoiceStatusIds, IEnumerable<int> paidStatusIds, IEnumerable<int> voidStatusIds, string period = "month", DateTime? businessNow = null)
        {
            // Period: this month / quarter / year (calendar, to date). Comparisons are against the previous period;
            // trend charts cover 6 / 12 / 24 months and lease expiries look 3 / 6 / 12 months ahead.
            var months = period == "year" ? 12 : period == "quarter" ? 3 : 1;
            var seriesMonths = period == "year" ? 24 : period == "quarter" ? 12 : 6;
            var aheadMonths = period == "year" ? 12 : period == "quarter" ? 6 : 3;
            // Status IDs are ints from code (OrderStatus lookups), never user input.
            static string List(IEnumerable<int> ids) { var l = ids.Distinct().ToList(); return l.Count == 0 ? "-1" : string.Join(",", l); }
            var open = List(openInvoiceStatusIds); var paid = List(paidStatusIds); var voids = List(voidStatusIds);
            // Location filter: null = all locations; otherwise only spaces in those locations (ints, never user text).
            var spaceLocFilter = locationIds is null ? "1 = 1" : $"s.LocationId IN ({List(locationIds)})";
            var invLocFilter = locationIds is null ? "1 = 1" : "b.SpaceId IN (SELECT Id FROM @Spaces)";
            await using var c = await Open();
            var sql = $@"
                DECLARE @Now DATETIME2(0) = @BizNow;   -- business (Pakistan) wall-clock time from the app clock
                DECLARE @MonthAgo DATETIME2(0) = DATEADD(MONTH, -@PMonths, @Now);   -- same point, one period ago
                DECLARE @Today DATE = CAST(@Now AS DATE);
                DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 1);
                DECLARE @PStart DATE = CASE @PMonths
                    WHEN 12 THEN DATEFROMPARTS(YEAR(@Today), 1, 1)
                    WHEN 3  THEN DATEFROMPARTS(YEAR(@Today), ((MONTH(@Today) - 1) / 3) * 3 + 1, 1)
                    ELSE @MonthStart END;
                DECLARE @PrevMonthStart DATE = DATEADD(MONTH, -@PMonths, @PStart);
                DECLARE @SoonEnd DATETIME2(0) = DATEADD(DAY, @Days, @Now);

                DECLARE @Spaces TABLE (Id INT PRIMARY KEY, Name NVARCHAR(200));
                INSERT INTO @Spaces (Id, Name)
                SELECT s.Id, COALESCE(NULLIF(s.Name, ''), s.Code)
                  FROM dbo.WN_Spaces s WITH (NOLOCK)
                 WHERE s.Status = 1 AND {spaceLocFilter};

                DECLARE @Live TABLE (Id INT PRIMARY KEY, SpaceId INT, StartOn DATETIME2(0), EndOn DATETIME2(0), StatusId INT, UserId INT NULL, CustomerCode NVARCHAR(50) NULL, TotalAmount DECIMAL(18,2) NULL);
                INSERT INTO @Live
                SELECT b.Id, b.SpaceId, b.StartOn, b.EndOn, b.BookingStatusId, b.UserId, b.CustomerCode, b.TotalAmount
                  FROM dbo.WN_Bookings b WITH (NOLOCK)
                  JOIN @Spaces sp ON sp.Id = b.SpaceId
                 WHERE ISNULL(b.IsDeleted, 0) = 0 AND b.BookingStatusId IN (1, 2, 5, 33);

                DECLARE @Inv TABLE (Id INT PRIMARY KEY, InvoiceNumber NVARCHAR(50), BookingId INT NULL, UserId INT NULL, IssuedOn DATETIME NULL, DueOn DATE NULL, GrandTotal DECIMAL(18,4), PaidTotal DECIMAL(18,4), StatusId INT, WhtAmount DECIMAL(18,4));
                INSERT INTO @Inv
                SELECT i.Id, i.InvoiceNumber, i.BookingId, i.UserId, i.IssuedOn, i.DueOn, ISNULL(i.GrandTotal, 0), ISNULL(i.PaidTotal, 0), i.StatusId, ISNULL(i.WHTAmount, 0)
                  FROM dbo.WN_Invoices i WITH (NOLOCK)
                  LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
                 WHERE i.StatusId NOT IN ({voids})
                   AND {invLocFilter};

                -- [0] headline numbers
                SELECT
                    (SELECT COUNT(*) FROM @Spaces) AS TotalSpaces,
                    (SELECT COUNT(DISTINCT SpaceId) FROM @Live WHERE StartOn <= @Now AND EndOn >= @Now) AS OccupiedSpaces,
                    (SELECT COUNT(DISTINCT SpaceId) FROM @Live WHERE StartOn <= @MonthAgo AND EndOn >= @MonthAgo) AS OccupiedSpacesLastMonth,
                    (SELECT COUNT(*) FROM @Live WHERE StartOn <= @Now AND EndOn >= @Now) AS ActiveBookings,
                    (SELECT COUNT(*) FROM @Live WHERE StartOn <= @MonthAgo AND EndOn >= @MonthAgo) AS ActiveBookingsLastMonth,
                    (SELECT COUNT(*) FROM @Live WHERE StatusId IN (1, 5) AND EndOn >= @Now) AS PendingConfirmations,
                    (SELECT ISNULL(SUM(GrandTotal - WhtAmount - PaidTotal), 0) FROM @Inv WHERE StatusId IN ({open})) AS Outstanding, -- WHT is withheld by the customer, not owed
                    (SELECT COUNT(*) FROM @Inv WHERE StatusId IN ({open})) AS OutstandingCount,
                    (SELECT ISNULL(SUM(GrandTotal - WhtAmount - PaidTotal), 0) FROM @Inv WHERE StatusId IN ({open}) AND DueOn < @Today) AS OverdueAmount,
                    (SELECT COUNT(*) FROM @Inv WHERE StatusId IN ({open}) AND DueOn < @Today) AS OverdueCount,
                    (SELECT ISNULL(SUM(GrandTotal), 0) FROM @Inv WHERE IssuedOn >= @PStart) AS InvoicedThisMonth,
                    (SELECT ISNULL(SUM(GrandTotal), 0) FROM @Inv WHERE IssuedOn >= @PrevMonthStart AND IssuedOn < DATEADD(DAY, DATEDIFF(DAY, @PStart, @Today) + 1, @PrevMonthStart)) AS InvoicedLastMonth,
                    (SELECT ISNULL(SUM(GrandTotal), 0) FROM @Inv WHERE IssuedOn >= @PStart AND StatusId IN ({paid})) AS PaidThisPeriod,
                    (SELECT COUNT(*) FROM @Live WHERE StatusId IN (2, 33) AND EndOn >= @Now AND EndOn <= @SoonEnd) AS LeasesEndingSoon;

                -- [1] last 12 months (occupancy measured at month end, or now for the current month)
                ;WITH m AS (
                    SELECT n, DATEADD(MONTH, -n, @MonthStart) AS MStart
                      FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14),(15),(16),(17),(18),(19),(20),(21),(22),(23)) v(n)
                     WHERE n < @Series
                )
                SELECT CONVERT(VARCHAR(7), m.MStart, 126) AS Month,
                       (SELECT COUNT(*) FROM dbo.WN_Bookings nb WITH (NOLOCK)
                         WHERE ISNULL(nb.IsDeleted, 0) = 0 AND nb.SpaceId IN (SELECT Id FROM @Spaces)
                           AND nb.CreatedOn >= m.MStart AND nb.CreatedOn < DATEADD(MONTH, 1, m.MStart)
                           AND nb.BookingStatusId NOT IN (3, 4, 6, 86)) AS NewBookings,
                       (SELECT ISNULL(SUM(GrandTotal), 0) FROM @Inv WHERE IssuedOn >= m.MStart AND IssuedOn < DATEADD(MONTH, 1, m.MStart)) AS Invoiced,
                       (SELECT ISNULL(SUM(GrandTotal), 0) FROM @Inv WHERE IssuedOn >= m.MStart AND IssuedOn < DATEADD(MONTH, 1, m.MStart) AND StatusId IN ({paid})) AS Paid,
                       (SELECT COUNT(DISTINCT SpaceId) FROM @Live
                         WHERE StartOn <= x.At AND EndOn >= x.At) AS Occupied
                  FROM m
                 CROSS APPLY (SELECT CASE WHEN m.n = 0 THEN @Now ELSE DATEADD(SECOND, -1, CAST(DATEADD(MONTH, 1, m.MStart) AS DATETIME2(0))) END AS At) x
                 ORDER BY m.MStart;

                -- [2] overdue invoices
                SELECT TOP 6 i.BookingId, i.InvoiceNumber AS Reference, i.DueOn AS [Date], (i.GrandTotal - i.WhtAmount - i.PaidTotal) AS Amount,
                       sp.Name AS Space, cust.Name AS Customer
                  FROM @Inv i
                  LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
                  LEFT JOIN @Spaces sp ON sp.Id = b.SpaceId
                 OUTER APPLY (SELECT TOP 1 COALESCE(NULLIF(cu.Company, ''), NULLIF(LTRIM(RTRIM(CONCAT(cu.FirstName, ' ', cu.LastName))), ''), cu.Email) AS Name
                                FROM dbo.WN_Customers cu WITH (NOLOCK)
                               WHERE cu.Code = b.CustomerCode OR cu.UserId = ISNULL(b.UserId, i.UserId)) cust
                 WHERE i.StatusId IN ({open}) AND i.DueOn < @Today
                 ORDER BY i.DueOn, i.Id;

                -- [3] leases ending soon
                SELECT TOP 6 l.Id AS BookingId, CONCAT('#', l.Id) AS Reference, l.EndOn AS [Date], l.TotalAmount AS Amount,
                       sp.Name AS Space, cust.Name AS Customer
                  FROM @Live l
                  JOIN @Spaces sp ON sp.Id = l.SpaceId
                 OUTER APPLY (SELECT TOP 1 COALESCE(NULLIF(cu.Company, ''), NULLIF(LTRIM(RTRIM(CONCAT(cu.FirstName, ' ', cu.LastName))), ''), cu.Email) AS Name
                                FROM dbo.WN_Customers cu WITH (NOLOCK)
                               WHERE cu.Code = l.CustomerCode OR cu.UserId = l.UserId) cust
                 WHERE l.StatusId IN (2, 33) AND l.EndOn >= @Now AND l.EndOn <= @SoonEnd
                 ORDER BY l.EndOn;

                -- [4] bookings waiting for confirmation
                SELECT TOP 6 l.Id AS BookingId, CONCAT('#', l.Id) AS Reference, l.StartOn AS [Date], l.TotalAmount AS Amount,
                       sp.Name AS Space, cust.Name AS Customer
                  FROM @Live l
                  JOIN @Spaces sp ON sp.Id = l.SpaceId
                 OUTER APPLY (SELECT TOP 1 COALESCE(NULLIF(cu.Company, ''), NULLIF(LTRIM(RTRIM(CONCAT(cu.FirstName, ' ', cu.LastName))), ''), cu.Email) AS Name
                                FROM dbo.WN_Customers cu WITH (NOLOCK)
                               WHERE cu.Code = l.CustomerCode OR cu.UserId = l.UserId) cust
                 WHERE l.StatusId IN (1, 5) AND l.EndOn >= @Now
                 ORDER BY l.StartOn;

                -- [5] receivables aging (open balance by days past due)
                SELECT b.Bucket, b.SortOrder, ISNULL(SUM(x.Balance), 0) AS Amount, COUNT(x.Id) AS Invoices
                  FROM (VALUES ('Not due', 0), ('1-30 days', 1), ('31-60 days', 2), ('61-90 days', 3), ('90+ days', 4)) b(Bucket, SortOrder)
                  LEFT JOIN (
                        SELECT i.Id, (i.GrandTotal - i.WhtAmount - i.PaidTotal) AS Balance,
                               CASE WHEN i.DueOn IS NULL OR i.DueOn >= @Today THEN 0
                                    WHEN DATEDIFF(DAY, i.DueOn, @Today) <= 30 THEN 1
                                    WHEN DATEDIFF(DAY, i.DueOn, @Today) <= 60 THEN 2
                                    WHEN DATEDIFF(DAY, i.DueOn, @Today) <= 90 THEN 3
                                    ELSE 4 END AS SortOrder
                          FROM @Inv i WHERE i.StatusId IN ({open})
                  ) x ON x.SortOrder = b.SortOrder
                 GROUP BY b.Bucket, b.SortOrder
                 ORDER BY b.SortOrder;

                -- [6] occupancy by space type (right now)
                SELECT COALESCE(NULLIF(st.Description, ''), 'Other') AS SpaceType,
                       COUNT(*) AS Total,
                       SUM(occ.IsOccupied) AS Occupied
                  FROM dbo.WN_Spaces s WITH (NOLOCK)
                  JOIN @Spaces sp ON sp.Id = s.Id
                  LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
                 OUTER APPLY (SELECT CASE WHEN EXISTS (SELECT 1 FROM @Live l WHERE l.SpaceId = s.Id AND l.StartOn <= @Now AND l.EndOn >= @Now)
                                          THEN 1 ELSE 0 END AS IsOccupied) occ
                 GROUP BY COALESCE(NULLIF(st.Description, ''), 'Other')
                 ORDER BY COUNT(*) DESC;

                -- [7] top customers by invoiced amount, last 12 months
                SELECT TOP 5 t.Customer, SUM(t.GrandTotal) AS Amount, COUNT(*) AS Invoices
                  FROM (
                        SELECT i.GrandTotal,
                               COALESCE(cust.Name, CONCAT('Customer #', ISNULL(b.UserId, i.UserId))) AS Customer
                          FROM @Inv i
                          LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
                         OUTER APPLY (SELECT TOP 1 COALESCE(NULLIF(cu.Company, ''), NULLIF(LTRIM(RTRIM(CONCAT(cu.FirstName, ' ', cu.LastName))), ''), cu.Email) AS Name
                                        FROM dbo.WN_Customers cu WITH (NOLOCK)
                                       WHERE cu.Code = b.CustomerCode OR cu.UserId = ISNULL(b.UserId, i.UserId)) cust
                         WHERE i.IssuedOn >= @PStart
                  ) t
                 GROUP BY t.Customer
                 ORDER BY SUM(t.GrandTotal) DESC;

                -- [8] leases expiring, next 6 months
                ;WITH f AS (
                    SELECT n, DATEADD(MONTH, n, @MonthStart) AS MStart FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11)) v(n) WHERE n < @Ahead
                )
                SELECT CONVERT(VARCHAR(7), f.MStart, 126) AS Month,
                       (SELECT COUNT(*) FROM @Live l WHERE l.EndOn >= f.MStart AND l.EndOn < DATEADD(MONTH, 1, f.MStart) AND l.EndOn >= @Now) AS Leases,
                       (SELECT ISNULL(SUM(l.TotalAmount), 0) FROM @Live l WHERE l.EndOn >= f.MStart AND l.EndOn < DATEADD(MONTH, 1, f.MStart) AND l.EndOn >= @Now) AS Value
                  FROM f
                 ORDER BY f.MStart;";
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 60 };
            cmd.Parameters.Add("@Days", SqlDbType.Int).Value = endingSoonDays;
            cmd.Parameters.Add("@PMonths", SqlDbType.Int).Value = months;
            cmd.Parameters.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow ?? _clock.Now;
            cmd.Parameters.Add("@Series", SqlDbType.Int).Value = seriesMonths;
            cmd.Parameters.Add("@Ahead", SqlDbType.Int).Value = aheadMonths;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadResultSets(r);
        }

        /// <summary>
        /// Admin sidebar badges (read-only, polled every 60 s by every open admin tab): one row of scalar counts of
        /// work waiting on staff right now. @Loc limits the counts that have a location (via the space) to that
        /// location; contacts and machines stay global. Status values are the ones the screens and services write:
        /// agreements 'EmailFailed' / signed copy on file (WN_Agreements.SignedPdfPath), contacts StatusId 1 = New,
        /// bookings 1 / 5 = old / new Pending, quotations 'Accepted' / 'Declined', KYC document Status 0 = Pending.
        /// </summary>
        public async Task<List<(string Key, string ItemKey)>> GetNavBadgeItemsDbAsync(int? locationId, IEnumerable<int> openInvoiceStatusIds, DateTime businessToday, DateTime businessNow)
        {
            // One row per item waiting on staff: K = badge, ItemKey = what "read" remembers. The key includes the
            // part that makes an item new again (e.g. agreement status, quotation status, KYC document count).
            // Status IDs are ints from code (OrderStatus lookups), never user input.
            var open = openInvoiceStatusIds.Distinct().ToList();
            var openList = open.Count == 0 ? "-1" : string.Join(",", open);
            await using var c = await Open();
            var sql = $@"
                DECLARE @Items TABLE (K VARCHAR(20) NOT NULL, ItemKey NVARCHAR(100) NOT NULL);

                -- KYC: the documents table is read only by stored procedures elsewhere; skip it if it is not there.
                IF OBJECT_ID(N'dbo.WN_CustomerKYCDocuments', N'U') IS NOT NULL
                    INSERT INTO @Items (K, ItemKey)
                    SELECT 'Kyc', CONCAT(d.CustomerId, ':', COUNT(*))
                      FROM dbo.WN_CustomerKYCDocuments d WITH (NOLOCK)
                     WHERE d.IsActive = 1 AND d.Status = 0   -- KycDocumentStatus.Pending
                       AND (@Loc IS NULL OR EXISTS (
                            SELECT 1
                              FROM dbo.WN_Customers cu WITH (NOLOCK)
                              LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = cu.UserId
                             WHERE cu.Id = d.CustomerId
                               AND (u.LocationId = @Loc OR EXISTS (
                                    SELECT 1
                                      FROM dbo.WN_Bookings kb WITH (NOLOCK)
                                      JOIN dbo.WN_Spaces ks WITH (NOLOCK) ON ks.Id = kb.SpaceId
                                     WHERE ks.LocationId = @Loc
                                       AND (kb.CustomerCode = cu.Code OR (cu.UserId IS NOT NULL AND kb.UserId = cu.UserId))))))
                     GROUP BY d.CustomerId;

                INSERT INTO @Items (K, ItemKey)
                -- signed copy waiting for verification (customer upload or admin upload not yet confirmed) + send failures
                SELECT 'Agreements', CONCAT(a.Id, ':', a.Status)
                  FROM dbo.WN_Agreements a WITH (NOLOCK)
                  JOIN dbo.WN_Quotations q WITH (NOLOCK) ON q.Id = a.QuotationId
                 WHERE a.BookingId IS NULL
                   AND (a.Status = 'EmailFailed' OR (a.SignedPdfPath IS NOT NULL AND a.SignedPdfPath <> ''))
                   AND ISNULL(q.Status, '') NOT IN ('Declined', 'Rejected', 'Cancelled', 'Expired')
                   AND NOT EXISTS (SELECT 1 FROM dbo.WN_Agreements a2 WITH (NOLOCK)
                                    WHERE a2.QuotationId = a.QuotationId AND a2.Id > a.Id)
                   AND (@Loc IS NULL OR EXISTS (SELECT 1 FROM dbo.WN_Spaces s WITH (NOLOCK) WHERE s.Id = q.SpaceId AND s.LocationId = @Loc))
                UNION ALL
                -- contact + book-tour messages still New (WN_Contacts_Insert writes StatusId 1)
                SELECT 'Contacts', CAST(ct.Id AS NVARCHAR(20)) FROM dbo.WN_Contacts ct WITH (NOLOCK) WHERE ct.StatusId = 1
                UNION ALL
                -- bookings waiting for confirmation (same rule as the dashboard's PendingConfirmations)
                SELECT 'Bookings', CAST(b.Id AS NVARCHAR(20))
                  FROM dbo.WN_Bookings b WITH (NOLOCK)
                 WHERE ISNULL(b.IsDeleted, 0) = 0 AND b.BookingStatusId IN (1, 5) AND b.EndOn >= @Now
                   AND (@Loc IS NULL OR EXISTS (SELECT 1 FROM dbo.WN_Spaces s WITH (NOLOCK) WHERE s.Id = b.SpaceId AND s.LocationId = @Loc))
                UNION ALL
                -- accepted by the customer (agreement not sent yet) + declined in the last 7 days
                SELECT 'Quotations', CONCAT(q.Id, ':', q.Status)
                  FROM dbo.WN_Quotations q WITH (NOLOCK)
                 WHERE (q.Status = 'Accepted'
                        OR (q.Status = 'Declined' AND EXISTS (
                               SELECT 1 FROM dbo.WN_QuotationResponses qr WITH (NOLOCK)
                                WHERE qr.QuotationId = q.Id AND qr.ResponseType = 'Declined'
                                  AND qr.RespondedDate >= DATEADD(DAY, -7, GETUTCDATE()))))   -- RespondedDate is written with GETUTCDATE()
                   AND (@Loc IS NULL OR EXISTS (SELECT 1 FROM dbo.WN_Spaces s WITH (NOLOCK) WHERE s.Id = q.SpaceId AND s.LocationId = @Loc))
                UNION ALL
                -- overdue: still owed (Unpaid / Partial / Challan Expire, legacy + OrderStatus) and past the due date
                SELECT 'Invoices', CAST(i.Id AS NVARCHAR(20))
                  FROM dbo.WN_Invoices i WITH (NOLOCK)
                  LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
                 WHERE i.StatusId IN ({openList}) AND i.DueOn < @Today
                   AND (@Loc IS NULL OR EXISTS (SELECT 1 FROM dbo.WN_Spaces s WITH (NOLOCK) WHERE s.Id = b.SpaceId AND s.LocationId = @Loc))
                UNION ALL
                -- door access suspended right now: open suspension, no override running, booking still running
                -- (same conditions as GetHikAccessOverviewDbAsync)
                SELECT 'AccessSuspended', CAST(sus.Id AS NVARCHAR(20))
                  FROM dbo.WN_HIK_BookingAccessSuspensions sus WITH (NOLOCK)
                  JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = sus.BookingId
                 WHERE sus.ResolvedAt IS NULL
                   AND (sus.OverrideUntil IS NULL OR sus.OverrideUntil < @Today)
                   AND ISNULL(b.IsDeleted, 0) = 0
                   AND b.BookingStatusId NOT IN (3, 4, 6, 86) -- rejected / old cancelled / no show / cancelled
                   AND EXISTS (SELECT 1 FROM dbo.WN_BookingDetails d WITH (NOLOCK)
                                WHERE d.BookingGuid = b.IdGUID AND d.IsDeleted = 0
                                  AND d.EndDateTime >= @Today)   -- = MAX(EndDateTime) >= @Today
                   AND (@Loc IS NULL OR EXISTS (SELECT 1 FROM dbo.WN_Spaces s WITH (NOLOCK) WHERE s.Id = b.SpaceId AND s.LocationId = @Loc))
                UNION ALL
                -- offline door machines; Last_seen makes a machine that drops off again count as new
                SELECT 'MachinesOffline', CONCAT(hd.Id, ':', CONVERT(VARCHAR(19), hd.Last_seen, 126))
                  FROM dbo.WN_HIK_Devices hd WITH (NOLOCK) WHERE hd.Online = 0;

                SELECT K, ItemKey FROM @Items;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@Loc", SqlDbType.Int).Value = (object?)locationId ?? DBNull.Value;
            cmd.Parameters.Add("@Today", SqlDbType.Date).Value = businessToday.Date;
            cmd.Parameters.Add("@Now", SqlDbType.DateTime2).Value = businessNow;
            var list = new List<(string, string)>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add((r.GetString(0), r.GetString(1)));
            return list;
        }

        public async Task<Dictionary<string, HashSet<string>>> GetNavBadgeReadsDbAsync(string userEmail)
        {
            // Item keys each user marked as read, per sidebar route (WN_NAV_BadgeReads; created by the user's script).
            await using var c = await Open();
            const string sql = @"
                IF OBJECT_ID(N'dbo.WN_NAV_BadgeReads', N'U') IS NOT NULL
                    SELECT RouteKey, ReadItems FROM dbo.WN_NAV_BadgeReads WITH (NOLOCK) WHERE UserEmail = @Email;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@Email", SqlDbType.NVarChar, 200).Value = userEmail;
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var items = r.IsDBNull(1) ? null : System.Text.Json.JsonSerializer.Deserialize<List<string>>(r.GetString(1));
                result[r.GetString(0)] = new HashSet<string>(items ?? new List<string>());
            }
            return result;
        }

        public async Task<bool> SaveNavBadgeReadsDbAsync(string userEmail, IDictionary<string, List<string>> itemsByRoute)
        {
            await using var c = await Open();
            await using (var check = new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'dbo.WN_NAV_BadgeReads', N'U') IS NULL THEN 0 ELSE 1 END", c))
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 0) return false;

            const string sql = @"
                UPDATE dbo.WN_NAV_BadgeReads SET ReadItems = @Items, ReadAt = SYSUTCDATETIME()
                 WHERE UserEmail = @Email AND RouteKey = @Route;
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.WN_NAV_BadgeReads (UserEmail, RouteKey, ReadItems, ReadAt)
                    VALUES (@Email, @Route, @Items, SYSUTCDATETIME());";
            foreach (var (route, items) in itemsByRoute)
            {
                await using var cmd = new SqlCommand(sql, c);
                cmd.Parameters.Add("@Email", SqlDbType.NVarChar, 200).Value = userEmail;
                cmd.Parameters.Add("@Route", SqlDbType.NVarChar, 100).Value = route;
                cmd.Parameters.Add("@Items", SqlDbType.NVarChar, -1).Value = System.Text.Json.JsonSerializer.Serialize(items);
                await cmd.ExecuteNonQueryAsync();
            }
            return true;
        }

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

        public async Task<IEnumerable<IDictionary<string, object?>>> GetWhtRateOptionsAsync(bool includeInactive = false)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_WHTaxRate_GetList", c);
            if (includeInactive) cmd.Parameters.AddWithValue("@IncludeInactive", true);   // WHT_13 adds the parameter
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        private static (int Id, decimal Rate, bool Active)? WhtRow(IDictionary<string, object?> r)
        {
            object? Get(string k) => r.FirstOrDefault(kv => string.Equals(kv.Key, k, StringComparison.OrdinalIgnoreCase)).Value;
            var id = Get("Id"); var rate = Get("WHRate"); var st = Get("Status");
            if (id is null || id is DBNull || rate is null || rate is DBNull) return null;
            bool active = st is null || st is DBNull || Convert.ToInt32(st) != 0;
            return (Convert.ToInt32(id), Convert.ToDecimal(rate), active);
        }

        /// <summary>
        /// Column holding the chosen WN_WHTaxRate Id: WHTaxId (after rename) or WHTRate_ID or the old name
        /// (WN_Bookings.WHTRate / WN_Quotations.WithholdingTaxRate), so the API works across schema versions.
        /// </summary>
        private static async Task<string> WhtIdColumnAsync(SqlConnection c, string table, string oldName)
        {
            await using var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH(@T, 'WHTaxId') IS NOT NULL THEN 'WHTaxId' WHEN COL_LENGTH(@T, 'WHTRate_ID') IS NOT NULL THEN 'WHTRate_ID' ELSE @O END", c);
            cmd.Parameters.AddWithValue("@T", table);
            cmd.Parameters.AddWithValue("@O", oldName);
            return (string)(await cmd.ExecuteScalarAsync() ?? oldName);
        }

        /// <summary>The WHT rate Id from a quotation / booking row, under WHTaxId, WHTRate_ID, or the old column name.</summary>
        public static object? WhtIdValue(IDictionary<string, object?> row, string oldName)
        {
            if (row.TryGetValue("WHTaxId", out var t) && t is not null && t is not DBNull) return t;
            if (row.TryGetValue("WHTRate_ID", out var v) && v is not null && v is not DBNull) return v;
            return row.TryGetValue(oldName, out var o) && o is not null && o is not DBNull ? o : null;
        }

        public async Task<decimal> ResolveWhtRatePercentAsync(decimal? value)
        {
            if (value is not > 0) return value ?? 0m;
            if (value.Value != Math.Floor(value.Value)) return value.Value;   // a fraction is always a percentage
            try
            {
                var rows = await GetWhtRateOptionsAsync(includeInactive: true);
                var match = rows.Select(WhtRow).FirstOrDefault(x => x.HasValue && x.Value.Id == (int)value.Value);
                return match?.Rate ?? value.Value;
            }
            catch (SqlException)
            {
                return value.Value;   // list not installed yet: the stored value is a percentage
            }
        }

        public async Task<bool> IsActiveWhtTaxRateIdAsync(decimal? value)
        {
            if (value is not > 0 || value.Value != Math.Floor(value.Value)) return false;
            var rows = await GetWhtRateOptionsAsync();
            return rows.Select(WhtRow).Any(x => x.HasValue && x.Value.Id == (int)value.Value && x.Value.Active);
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

        public async Task<IDictionary<string, object?>> CreateCustomerAsync(string firstName, string? lastName, string email, string? phone, string? cnic, string? address, int? cityId, string? notes, string? createdBy, int? userId = null, string? company = null, string? ntn = null, string? secp = null)
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
            cmd.Parameters.AddWithValue("@NTN", (object?)ntn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SecpRegistrationNo", (object?)secp ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) return ToDict(r);
            return new Dictionary<string, object?>();
        }

        public async Task<IDictionary<string, object?>?> UpdateCustomerAsync(string guid, string? firstName, string? lastName, string? email, string? phone, string? cnic, string? address, int? cityId, string? notes, bool? isActive, string? company = null, string? ntn = null, string? secp = null)
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
            cmd.Parameters.AddWithValue("@NTN", (object?)ntn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SecpRegistrationNo", (object?)secp ?? DBNull.Value);
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
                    ba.SurchargeApplied,
                    hpm.MachineID AS MachineId,
                    (SELECT COUNT(1) FROM dbo.WN_HIK_PendingOps po WITH (NOLOCK) WHERE po.employee_no = hpm.MachineID) AS HikPendingOps
                FROM dbo.WN_BookingAttendants ba WITH (NOLOCK)
                JOIN dbo.WN_Persons p WITH (NOLOCK) ON p.PersonId = ba.PersonId
                LEFT JOIN dbo.WN_AccessStatus acc WITH (NOLOCK) ON acc.BookingDetailId = ba.BookingDetailId AND acc.PersonId = ba.PersonId
                LEFT JOIN dbo.WN_HIK_PersonMap hpm WITH (NOLOCK) ON hpm.PersonId = ba.PersonId
                WHERE ba.BookingDetailId = @BookingDetailId AND (ba.AssignedTo IS NULL OR ba.AssignedTo >= CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE))
                ORDER BY p.Name ASC;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@BookingDetailId", bookingDetailId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        /// <summary>
        /// Current booking assignments of attendants matching ANY of the given values (read-only; mobile access
        /// matching). Empty values are ignored; the caller scores how many fields actually match.
        /// </summary>
        public async Task<IEnumerable<IDictionary<string, object?>>> GetActiveAttendantAssignmentCandidatesDbAsync(string email, string idNumber, string phoneKey, string name)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT
                    p.PersonId, p.PersonGuid, p.Name, p.Email, p.Phone, p.IdType, p.IdNumber,
                    ba.BookingDetailId,
                    ISNULL(bd.SpaceName, '') AS SpaceName,
                    ISNULL(acc.IsEnabled, 1) AS IsEnabled
                FROM dbo.WN_Persons p WITH (NOLOCK)
                JOIN dbo.WN_BookingAttendants ba WITH (NOLOCK) ON ba.PersonId = p.PersonId
                LEFT JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.Id = ba.BookingDetailId
                LEFT JOIN dbo.WN_AccessStatus acc WITH (NOLOCK) ON acc.BookingDetailId = ba.BookingDetailId AND acc.PersonId = ba.PersonId
                WHERE (ba.AssignedTo IS NULL OR ba.AssignedTo >= CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE))
                  AND (
                        (@Email <> '' AND LOWER(LTRIM(RTRIM(p.Email))) = @Email)
                     OR (@IdNumber <> '' AND UPPER(REPLACE(REPLACE(p.IdNumber, '-', ''), ' ', '')) = @IdNumber)
                     OR (@Phone <> '' AND REPLACE(REPLACE(REPLACE(p.Phone, '-', ''), ' ', ''), '+', '') LIKE '%' + @Phone)
                     OR (@Name <> '' AND LOWER(LTRIM(RTRIM(p.Name))) = @Name)
                  );";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Email", (email ?? "").Trim().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@IdNumber", idNumber ?? "");
            cmd.Parameters.AddWithValue("@Phone", phoneKey ?? "");
            cmd.Parameters.AddWithValue("@Name", name ?? "");
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        /// <summary>WN_Quotations.SendWhtInvoice (false if the column doesn't exist yet: error 207).</summary>
        public async Task<bool> GetQuotationSendWhtInvoiceAsync(int quotationId)
        {
            try
            {
                await using var c = await Open();
                await using var cmd = new SqlCommand("SELECT SendWhtInvoice FROM dbo.WN_Quotations WITH (NOLOCK) WHERE Id = @Id", c);
                cmd.Parameters.AddWithValue("@Id", quotationId);
                var v = await cmd.ExecuteScalarAsync();
                return v is not null && v is not DBNull && Convert.ToBoolean(v);
            }
            catch (SqlException ex) when (ex.Number == 207) { return false; }
        }

        public async Task<(bool SendWht, decimal? WhTaxId)> GetBookingWhtAsync(int bookingId)
        {
            await using var c = await Open();
            var col = await WhtIdColumnAsync(c, "dbo.WN_Bookings", "WHTRate");
            await using var cmd = new SqlCommand($"SELECT ISNULL(SendWhtInvoice, 0) AS SendWhtInvoice, {col} AS WhTaxId FROM dbo.WN_Bookings WITH (NOLOCK) WHERE Id = @Id", c);
            cmd.Parameters.AddWithValue("@Id", bookingId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return (false, null);
            bool send = Convert.ToBoolean(r["SendWhtInvoice"]);
            decimal? id = r["WhTaxId"] is DBNull ? null : Convert.ToDecimal(r["WhTaxId"]);
            return send && id is > 0 ? (true, id) : (false, null);
        }

        public async Task SetBookingWhtAsync(int bookingId, decimal? whTaxId)
        {
            await using var c = await Open();
            var col = await WhtIdColumnAsync(c, "dbo.WN_Bookings", "WHTRate");
            // Standard: flag off and no Id (the column may be NOT NULL on older schemas: then it is left as is).
            var sql = whTaxId is > 0
                ? $"UPDATE dbo.WN_Bookings SET SendWhtInvoice = 1, {col} = @W WHERE Id = @Id"
                : $"UPDATE dbo.WN_Bookings SET SendWhtInvoice = 0, {col} = CASE WHEN COLUMNPROPERTY(OBJECT_ID(N'dbo.WN_Bookings'), '{col}', 'AllowsNull') = 1 THEN NULL ELSE {col} END WHERE Id = @Id";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Id", bookingId);
            if (whTaxId is > 0) cmd.Parameters.AddWithValue("@W", whTaxId.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        // ---- Extra locations per user (WN_UserLocations). SQL error 208 = table not created yet. ----

        /// <summary>Locations assigned in WN_UserLocations (empty if none or the table doesn't exist yet).</summary>
        public async Task<List<int>> GetUserLocationIdsAsync(int userId)
        {
            var ids = new List<int>();
            try
            {
                await using var c = await Open();
                await using var cmd = new SqlCommand("SELECT LocationId FROM dbo.WN_UserLocations WITH (NOLOCK) WHERE UserId = @UserId", c);
                cmd.Parameters.AddWithValue("@UserId", userId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
            }
            catch (SqlException ex) when (ex.Number == 208) { }
            return ids;
        }

        /// <summary>Assigned locations (id, name) for several users at once, for the Users list.</summary>
        public async Task<Dictionary<int, List<(int Id, string Name)>>> GetUserLocationsMapAsync(IEnumerable<int> userIds)
        {
            var map = new Dictionary<int, List<(int Id, string Name)>>();
            var idList = userIds.Distinct().ToList();
            if (idList.Count == 0) return map;
            try
            {
                await using var c = await Open();
                var names = string.Join(",", idList.Select((_, i) => "@U" + i));
                await using var cmd = new SqlCommand($@"
                    SELECT ul.UserId, ul.LocationId, ISNULL(l.Name, '') AS Name
                    FROM dbo.WN_UserLocations ul WITH (NOLOCK)
                    LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = ul.LocationId
                    WHERE ul.UserId IN ({names})", c);
                for (int i = 0; i < idList.Count; i++) cmd.Parameters.AddWithValue("@U" + i, idList[i]);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var uid = r.GetInt32(0);
                    if (!map.TryGetValue(uid, out var list)) map[uid] = list = new List<(int, string)>();
                    list.Add((r.GetInt32(1), r.GetString(2)));
                }
            }
            catch (SqlException ex) when (ex.Number == 208) { }
            return map;
        }

        /// <summary>
        /// Replaces the user's assigned locations. Returns false if WN_UserLocations doesn't exist yet
        /// (only WN_Users.LocationId is then saved).
        /// </summary>
        public async Task<bool> SetUserLocationsAsync(int userId, IReadOnlyCollection<int> locationIds, int? actorId)
        {
            try
            {
                await using var c = await Open();
                await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
                await using (var del = new SqlCommand("DELETE FROM dbo.WN_UserLocations WHERE UserId = @UserId", c, tx))
                {
                    del.Parameters.AddWithValue("@UserId", userId);
                    await del.ExecuteNonQueryAsync();
                }
                foreach (var locId in locationIds.Distinct())
                {
                    await using var ins = new SqlCommand(
                        "INSERT INTO dbo.WN_UserLocations (UserId, LocationId, CreatedById) VALUES (@UserId, @LocationId, @CreatedById)", c, tx);
                    ins.Parameters.AddWithValue("@UserId", userId);
                    ins.Parameters.AddWithValue("@LocationId", locId);
                    ins.Parameters.AddWithValue("@CreatedById", (object?)actorId ?? DBNull.Value);
                    await ins.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                return true;
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                return false;
            }
        }

        public async Task<int?> GetSpaceLocationIdAsync(int spaceId)
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand("SELECT LocationId FROM dbo.WN_Spaces WITH (NOLOCK) WHERE Id = @Id", c);
            cmd.Parameters.AddWithValue("@Id", spaceId);
            var v = await cmd.ExecuteScalarAsync();
            return v is null || v is DBNull ? null : Convert.ToInt32(v);
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
                SET AssignedTo = DATEADD(day, -1, CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE)) 
                WHERE BookingDetailId = @BookingDetailId AND PersonId = @PersonId AND (AssignedTo IS NULL OR AssignedTo >= CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE));

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
                    (SELECT COUNT(1) FROM dbo.WN_BookingAttendants ba WHERE ba.BookingDetailId = bd.Id AND (ba.AssignedTo IS NULL OR ba.AssignedTo >= CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE))) AS ActiveAttendantsCount
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
                    (SELECT COUNT(1) FROM dbo.WN_BookingAttendants ba WHERE ba.BookingDetailId = bd.Id AND (ba.AssignedTo IS NULL OR ba.AssignedTo >= CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE))) AS CurrentActiveAttendants
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
                    ISNULL(i.GrandTotal - ISNULL(i.WHTAmount, 0) - i.PaidTotal, 0) AS BalanceDue,
                    i.CurrencyCode,
                    i.StatusId,
                    i.InvoiceTypeId,
                    i.Notes,
                    i.CreatedOn,
                    COALESCE(
                        -- legacy values on older rows
                        CASE i.StatusId WHEN 1 THEN 'Unpaid' WHEN 2 THEN 'Paid' WHEN 3 THEN 'Partial' WHEN 4 THEN 'Overdue' WHEN 5 THEN 'Cancelled' END,
                        -- OrderStatus IDs (looked up, not hard-coded)
                        (SELECT CASE LTRIM(RTRIM(os.Description)) WHEN 'Un Paid' THEN 'Unpaid' WHEN 'Challan Expire' THEN 'Overdue' ELSE LTRIM(RTRIM(os.Description)) END
                           FROM dbo.OrderStatus os WITH (NOLOCK) WHERE os.Id = i.StatusId),
                        'Unknown') AS StatusLabel
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
                LEFT JOIN dbo.WN_BookingAttendants ba WITH (NOLOCK) ON ba.PersonId = p.PersonId AND (ba.AssignedTo IS NULL OR ba.AssignedTo >= CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE))
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
                VendorPhone = r.IsDBNull(r.GetOrdinal("VendorPhone")) ? "+92 328 0256000 / +92 320 1809696" : r.GetString(r.GetOrdinal("VendorPhone")),
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
            string? firstName = fullName;
            string? lastName = null;
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                var parts = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                firstName = parts.Length > 0 ? parts[0] : fullName;
                lastName = parts.Length > 1 ? parts[1] : null;
            }
            cmd.Parameters.AddWithValue("@CustomerId", customerId);
            cmd.Parameters.AddWithValue("@FirstName", (object?)firstName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LastName", (object?)lastName ?? DBNull.Value);
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

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAgreementsAwaitingSignatureDbAsync(int? agreementId = null)
        {
            await using var c = await Open();
            // Reminders are logged per quotation; only those after this agreement's SentDate count, so a
            // re-sent agreement for the same quotation starts again. Only the newest agreement of a quotation is returned.
            const string sql = @"
                SELECT a.Id AS AgreementId, a.QuotationId, q.QuotationNumber, q.Version, a.Status, a.SentDate,
                       COALESCE(NULLIF(LTRIM(RTRIM(a.CustomerName)), ''), NULLIF(LTRIM(RTRIM(a.CompanyName)), ''),
                                NULLIF(LTRIM(RTRIM(CONCAT(cu.FirstName, ' ', ISNULL(cu.LastName, '')))), '')) AS CustomerName,
                       cu.Email AS CustomerEmail,
                       COALESCE(NULLIF(s.Name, ''), s.Code) AS SpaceName,
                       ISNULL(rem.ReminderCount, 0) AS ReminderCount, rem.LastReminderAt
                  FROM dbo.WN_Agreements a WITH (NOLOCK)
                  JOIN dbo.WN_Quotations q WITH (NOLOCK) ON q.Id = a.QuotationId
                  LEFT JOIN dbo.WN_Customers cu WITH (NOLOCK) ON cu.Id = q.CustomerId
                  LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = q.SpaceId
                 OUTER APPLY (SELECT COUNT(1) AS ReminderCount, MAX(qa.CreatedDate) AS LastReminderAt
                                FROM dbo.WN_QuotationActivities qa WITH (NOLOCK)
                               WHERE qa.QuotationId = a.QuotationId
                                 AND qa.ActivityType = 'AgreementReminder'
                                 AND qa.CreatedDate >= a.SentDate) rem
                 WHERE a.Status IN ('AgreementSent', 'EmailFailed')
                   AND a.BookingId IS NULL
                   AND (a.SignedPdfPath IS NULL OR a.SignedPdfPath = '')
                   AND a.SentDate IS NOT NULL
                   AND ISNULL(q.Status, '') NOT IN ('Declined', 'Rejected', 'Cancelled', 'Expired', 'Signed', 'Converted')
                   AND NOT EXISTS (SELECT 1 FROM dbo.WN_Agreements a2 WITH (NOLOCK)
                                    WHERE a2.QuotationId = a.QuotationId AND a2.Id > a.Id)
                   AND (@AgreementId IS NULL OR a.Id = @AgreementId)
                 ORDER BY a.SentDate, a.Id;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@AgreementId", SqlDbType.Int).Value = (object?)agreementId ?? DBNull.Value;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IDictionary<int, (int Count, DateTime? LastReminderAt)>> GetAgreementReminderStatsDbAsync(IEnumerable<int> agreementIds)
        {
            var result = new Dictionary<int, (int Count, DateTime? LastReminderAt)>();
            // Ids are ints from our own list query, never user input.
            var ids = agreementIds.Where(i => i > 0).Distinct().ToList();
            if (ids.Count == 0) return result;

            await using var c = await Open();
            string sql = $@"
                SELECT a.Id, COUNT(qa.Id) AS ReminderCount, MAX(qa.CreatedDate) AS LastReminderAt
                  FROM dbo.WN_Agreements a WITH (NOLOCK)
                  JOIN dbo.WN_QuotationActivities qa WITH (NOLOCK)
                    ON qa.QuotationId = a.QuotationId
                   AND qa.ActivityType = 'AgreementReminder'
                   AND qa.CreatedDate >= a.SentDate
                 WHERE a.Id IN ({string.Join(",", ids)})
                 GROUP BY a.Id;";
            await using var cmd = new SqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                result[r.GetInt32(0)] = (r.GetInt32(1), r.IsDBNull(2) ? null : r.GetDateTime(2));
            return result;
        }

        public async Task<IAsyncDisposable?> TryAcquireAppLockDbAsync(string resource)
        {
            var c = await Open();
            try
            {
                await using var cmd = new SqlCommand("sp_getapplock", c) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.Add("@Resource", SqlDbType.NVarChar, 255).Value = resource;
                cmd.Parameters.Add("@LockMode", SqlDbType.VarChar, 32).Value = "Exclusive";
                cmd.Parameters.Add("@LockOwner", SqlDbType.VarChar, 32).Value = "Session";
                cmd.Parameters.Add("@LockTimeout", SqlDbType.Int).Value = 0;
                var ret = cmd.Parameters.Add("@Result", SqlDbType.Int);
                ret.Direction = ParameterDirection.ReturnValue;
                await cmd.ExecuteNonQueryAsync();
                if (ret.Value is int code && code >= 0)
                    return new SqlAppLock(c, resource);
            }
            catch
            {
                await c.DisposeAsync();
                throw;
            }
            await c.DisposeAsync();
            return null;
        }

        /// <summary>Holds the connection that owns a session app lock; releases the lock and closes it on dispose.</summary>
        private sealed class SqlAppLock : IAsyncDisposable
        {
            private readonly SqlConnection _c;
            private readonly string _resource;
            public SqlAppLock(SqlConnection c, string resource) { _c = c; _resource = resource; }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await using var cmd = new SqlCommand("sp_releaseapplock", _c) { CommandType = CommandType.StoredProcedure };
                    cmd.Parameters.Add("@Resource", SqlDbType.NVarChar, 255).Value = _resource;
                    cmd.Parameters.Add("@LockOwner", SqlDbType.VarChar, 32).Value = "Session";
                    await cmd.ExecuteNonQueryAsync();
                }
                catch { /* closing the session releases the lock anyway */ }
                await _c.DisposeAsync();
            }
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
                            d.Code AS code, 
                            d.Location AS location, 
                            d.Online AS online, 
                            d.Last_seen AS last_seen
                        FROM dbo.WN_HIK_Devices d WITH (NOLOCK)
                        LEFT JOIN dbo.WN_HIK_Groups g WITH (NOLOCK) ON g.Id = d.Group_id";
            var shouldFilterLocation = !string.IsNullOrWhiteSpace(location)
                && !int.TryParse(location, out _)
                && !string.Equals(location, "all", StringComparison.OrdinalIgnoreCase);

            if (shouldFilterLocation)
            {
                sql += " WHERE d.Location = @Location";
            }
            sql += " ORDER BY d.Device_Name";

            await using var cmd = new SqlCommand(sql, conn);
            if (shouldFilterLocation)
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
                    Code = r.IsDBNull(r.GetOrdinal("code")) ? null : r.GetString(r.GetOrdinal("code")),
                    Location = r.IsDBNull(r.GetOrdinal("location")) ? null : r.GetString(r.GetOrdinal("location")),
                    Online = !r.IsDBNull(r.GetOrdinal("online")) && r.GetBoolean(r.GetOrdinal("online")) ? 1 : 0,
                    LastSeen = r.IsDBNull(r.GetOrdinal("last_seen")) ? null : r.GetDateTime(r.GetOrdinal("last_seen")).ToString("yyyy-MM-ddTHH:mm:ss")
                });
            }
            return devices;
        }

        public async Task<IEnumerable<(int DeviceId, string? RosterJson)>> GetHikDeviceSnapshotsAsync(string? location = null)
        {
            var shouldFilterLocation = !string.IsNullOrWhiteSpace(location)
                && !int.TryParse(location, out _)
                && !string.Equals(location, "all", StringComparison.OrdinalIgnoreCase);

            // Prefer the machines' real user lists, cached by the HIK sync engine (UserInfo incl. numOfFP/numOfFace).
            await using (var cacheConn = await Open())
            {
                var cacheSql = @"SELECT d.Id, dc.Users_snapshot
                                 FROM dbo.WN_HIK_Devices d WITH (NOLOCK)
                                 LEFT JOIN dbo.WN_HIK_DevCache dc WITH (NOLOCK) ON dc.Device_id = d.Id"
                               + (shouldFilterLocation ? " WHERE d.Location = @Location" : "")
                               + " ORDER BY d.Id";
                await using var cacheCmd = new SqlCommand(cacheSql, cacheConn);
                if (shouldFilterLocation) cacheCmd.Parameters.AddWithValue("@Location", location);
                var cached = new List<(int, string?)>();
                await using (var cr = await cacheCmd.ExecuteReaderAsync())
                {
                    while (await cr.ReadAsync()) cached.Add((cr.GetInt32(0), cr.IsDBNull(1) ? null : cr.GetString(1)));
                }
                if (cached.Any(x => !string.IsNullOrWhiteSpace(x.Item2))) return cached;
            }

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
            if (shouldFilterLocation)
            {
                sql += " WHERE d.Location = @Location";
            }
            sql += " ORDER BY d.Id, e.employee_no";

            await using var cmd = new SqlCommand(sql, conn);
            if (shouldFilterLocation)
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
                @"SELECT employee_no, name, cnic
                  FROM dbo.WN_HIK_Users WITH (NOLOCK)
                  WHERE employee_no IS NOT NULL AND cnic IS NOT NULL AND cnic <> ''", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var empNo = r.GetString(0);
                var name = r.IsDBNull(1) ? "" : r.GetString(1).Trim().ToLowerInvariant();
                cnics[$"{empNo}||{name}"] = r.GetString(2);
            }
            return cnics;
        }

        /// <summary>
        /// Active booked rooms per machine employee # â€” attendants enrolled from Attendants &amp; Access
        /// (WN_HIK_PersonMap â†’ WN_BookingAttendants â†’ WN_BookingDetails â†’ WN_Customers).
        /// </summary>
        public async Task<IEnumerable<IDictionary<string, object?>>> GetHikBookedRoomsDbAsync()
        {
            await using var c = await Open();
            const string sql = @"
                SELECT DISTINCT
                    hpm.MachineID AS employee_no,
                    ISNULL(bd.SpaceName, N'') AS space,
                    bd.SpaceCode AS space_code,
                    bd.EndDateTime AS booking_end,
                    COALESCE(NULLIF(cu.Company, ''), NULLIF(LTRIM(RTRIM(CONCAT(cu.FirstName, ' ', cu.LastName))), ''), cu.Email, bd.CustomerEmail) AS customer
                FROM dbo.WN_HIK_PersonMap hpm WITH (NOLOCK)
                JOIN dbo.WN_BookingAttendants ba WITH (NOLOCK) ON ba.PersonId = hpm.PersonId
                JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.Id = ba.BookingDetailId AND bd.IsDeleted = 0
                OUTER APPLY (SELECT TOP 1 Company, FirstName, LastName, Email FROM dbo.WN_Customers WITH (NOLOCK)
                             WHERE Code = bd.CustomerCode OR Email = bd.CustomerEmail) cu
                WHERE ba.AssignedTo IS NULL OR ba.AssignedTo >= CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE);";
            await using var cmd = new SqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<Dictionary<string, string>> GetHikStaffTagMapDbAsync()
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand(
                @"SELECT u.employee_no, t.Name
                  FROM dbo.WN_HIK_Users u WITH (NOLOCK)
                  JOIN dbo.WN_HIK_Tags t WITH (NOLOCK) ON t.Id = u.tag_id", c);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) map[r.GetString(0)] = r.GetString(1);
            return map;
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

        // --- Hikvision Attendant Enrollment ---

        public async Task<HikAttendantContext?> GetHikAttendantContextDbAsync(int bookingDetailId, int personId)
        {
            return (await GetHikAttendantContextsDbAsync(bookingDetailId, personId)).FirstOrDefault();
        }

        /// <summary>
        /// Active attendants of a booking (or one person) with the booking window, room, current access flag
        /// (WN_AccessStatus) and machine employee # (WN_HIK_PersonMap, null if never enrolled).
        /// </summary>
        public async Task<IEnumerable<HikAttendantContext>> GetHikAttendantContextsDbAsync(int? bookingDetailId, int? personId)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT
                    p.PersonId,
                    ba.BookingDetailId,
                    p.Name,
                    p.IdType,
                    p.IdNumber,
                    bd.StartDateTime,
                    bd.EndDateTime,
                    bd.SpaceCode,
                    bd.SpaceName,
                    ISNULL(acc.IsEnabled, 1) AS IsEnabled,
                    hpm.MachineID AS EmployeeNo,
                    CASE WHEN EXISTS (
                        SELECT 1 FROM dbo.WN_HIK_BookingAccessSuspensions sus WITH (NOLOCK)
                        JOIN dbo.WN_Bookings bk WITH (NOLOCK) ON bk.Id = sus.BookingId
                        WHERE bk.IdGUID = bd.BookingGuid AND sus.ResolvedAt IS NULL
                          AND (sus.OverrideUntil IS NULL OR sus.OverrideUntil < CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE))
                    ) THEN 1 ELSE 0 END AS IsSuspended
                FROM dbo.WN_BookingAttendants ba WITH (NOLOCK)
                JOIN dbo.WN_Persons p WITH (NOLOCK) ON p.PersonId = ba.PersonId
                JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.Id = ba.BookingDetailId
                LEFT JOIN dbo.WN_AccessStatus acc WITH (NOLOCK) ON acc.BookingDetailId = ba.BookingDetailId AND acc.PersonId = ba.PersonId
                LEFT JOIN dbo.WN_HIK_PersonMap hpm WITH (NOLOCK) ON hpm.PersonId = ba.PersonId
                WHERE (@BookingDetailId IS NULL OR ba.BookingDetailId = @BookingDetailId)
                  AND (@PersonId IS NULL OR ba.PersonId = @PersonId)
                  AND (ba.AssignedTo IS NULL OR ba.AssignedTo >= CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE));";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@BookingDetailId", SqlDbType.Int).Value = (object?)bookingDetailId ?? DBNull.Value;
            cmd.Parameters.Add("@PersonId", SqlDbType.Int).Value = (object?)personId ?? DBNull.Value;
            await using var r = await cmd.ExecuteReaderAsync();

            var list = new List<HikAttendantContext>();
            foreach (var row in await ReadAll(r))
            {
                list.Add(new HikAttendantContext
                {
                    PersonId = Convert.ToInt32(row["PersonId"]),
                    BookingDetailId = Convert.ToInt32(row["BookingDetailId"]),
                    Name = Convert.ToString(row["Name"]) ?? "",
                    IdType = Convert.ToString(row["IdType"]) ?? "",
                    IdNumber = Convert.ToString(row["IdNumber"]) ?? "",
                    StartDateTime = row["StartDateTime"] is DateTime s ? s : null,
                    EndDateTime = row["EndDateTime"] is DateTime e ? e : null,
                    SpaceCode = Convert.ToString(row["SpaceCode"]),
                    SpaceName = Convert.ToString(row["SpaceName"]),
                    IsEnabled = row["IsEnabled"] == null || Convert.ToBoolean(row["IsEnabled"]),
                    EmployeeNo = Convert.ToString(row["EmployeeNo"]),
                    IsSuspended = row["IsSuspended"] != null && Convert.ToInt32(row["IsSuspended"]) == 1
                });
            }
            return list;
        }

        /// <summary>
        /// Returns the terminal employee # for a WorkNest person, allocating the next free member number
        /// (below 8500, same range as the HIK dashboard) on first use. Serialised with an app lock.
        /// </summary>
        public async Task<string> GetOrCreateHikEmployeeNoDbAsync(int personId, int floor = 0)
        {
            await using var c = await Open();
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRAN;
                EXEC sp_getapplock @Resource = 'WN_HIK_PersonMap_Allocate', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;

                DECLARE @emp NVARCHAR(32) = (SELECT MachineID FROM dbo.WN_HIK_PersonMap WHERE PersonId = @PersonId);
                IF @emp IS NULL
                BEGIN
                    DECLARE @max INT = CASE WHEN @Floor > 999 AND @Floor < 8500 THEN @Floor ELSE 999 END;
                    SELECT @max = CASE WHEN MAX(n) > @max THEN MAX(n) ELSE @max END
                    FROM (SELECT TRY_CAST(MachineID AS INT) AS n FROM dbo.WN_HIK_PersonMap) t WHERE n < 8500;

                    IF OBJECT_ID('dbo.WN_HIK_Employees') IS NOT NULL
                        EXEC sp_executesql N'SELECT @m = CASE WHEN MAX(n) > @m THEN MAX(n) ELSE @m END FROM (SELECT TRY_CAST(employee_no AS INT) AS n FROM dbo.WN_HIK_Employees) t WHERE n < 8500',
                            N'@m INT OUTPUT', @m = @max OUTPUT;
                    IF OBJECT_ID('dbo.WN_HIK_Users') IS NOT NULL
                        EXEC sp_executesql N'SELECT @m = CASE WHEN MAX(n) > @m THEN MAX(n) ELSE @m END FROM (SELECT TRY_CAST(employee_no AS INT) AS n FROM dbo.WN_HIK_Users) t WHERE n < 8500',
                            N'@m INT OUTPUT', @m = @max OUTPUT;
                    IF OBJECT_ID('dbo.WN_HIK_Settings') IS NOT NULL
                        EXEC sp_executesql N'SELECT @m = CASE WHEN MAX(n) > @m THEN MAX(n) ELSE @m END FROM (SELECT TRY_CAST([value] AS INT) AS n FROM dbo.WN_HIK_Settings WHERE [key] = ''max_member_no'') t WHERE n < 8500',
                            N'@m INT OUTPUT', @m = @max OUTPUT;

                    IF @max + 1 >= 8500 THROW 50001, 'Machine ID range (1000-8499) is full.', 1;
                    SET @emp = CAST(@max + 1 AS NVARCHAR(32));
                    INSERT INTO dbo.WN_HIK_PersonMap (PersonId, MachineID) VALUES (@PersonId, @emp);
                END

                COMMIT;
                SELECT @emp;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@PersonId", personId);
            cmd.Parameters.AddWithValue("@Floor", floor);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToString(result) ?? throw new InvalidOperationException("Could not allocate a Hikvision employee number.");
        }

        /// <summary>
        /// Gives a person a new Machine ID above @Floor (only WN_HIK_PersonMap, WorkNest's own table, is written).
        /// Used when a freshly allocated number turns out to be taken on a machine.
        /// </summary>
        public async Task<string> ReallocateHikEmployeeNoDbAsync(int personId, int floor)
        {
            await using (var c = await Open())
            await using (var del = new SqlCommand("DELETE FROM dbo.WN_HIK_PersonMap WHERE PersonId = @PersonId;", c))
            {
                del.Parameters.AddWithValue("@PersonId", personId);
                await del.ExecuteNonQueryAsync();
            }
            return await GetOrCreateHikEmployeeNoDbAsync(personId, floor);
        }

        /// <summary>Devices that still have queued ops (any kind) for this machine user â€” read only.</summary>
        public async Task<HashSet<int>> GetHikPendingOpDeviceIdsForEmployeeDbAsync(string employeeNo)
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand("SELECT DISTINCT device_id FROM dbo.WN_HIK_PendingOps WITH (NOLOCK) WHERE employee_no = @Emp;", c);
            cmd.Parameters.AddWithValue("@Emp", employeeNo);
            var ids = new HashSet<int>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
            return ids;
        }

        public async Task<IEnumerable<HikDeviceConnection>> GetHikDeviceConnectionsDbAsync(IEnumerable<int> deviceIds)
        {
            var ids = deviceIds.Distinct().ToList();
            if (ids.Count == 0) return Enumerable.Empty<HikDeviceConnection>();

            await using var c = await Open();
            var names = ids.Select((_, i) => "@Id" + i).ToList();
            var sql = $@"
                SELECT Id, Device_Name, Host, Host2, Port, Use_https, Username, Password, Online, Code
                FROM dbo.WN_HIK_Devices WITH (NOLOCK)
                WHERE Id IN ({string.Join(",", names)});";
            await using var cmd = new SqlCommand(sql, c);
            for (var i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue(names[i], ids[i]);

            var list = new List<HikDeviceConnection>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var row = ToDict(r);
                list.Add(new HikDeviceConnection
                {
                    Id = Convert.ToInt32(row["Id"]),
                    Name = Convert.ToString(row["Device_Name"]) ?? "",
                    Host = Convert.ToString(row["Host"]) ?? "",
                    Host2 = Convert.ToString(row["Host2"]),
                    Port = row["Port"] != null ? Convert.ToInt32(row["Port"]) : 80,
                    UseHttps = row["Use_https"] != null && Convert.ToBoolean(row["Use_https"]),
                    Username = Convert.ToString(row["Username"]) ?? "",
                    Password = Convert.ToString(row["Password"]) ?? "",
                    Online = row["Online"] != null && Convert.ToBoolean(row["Online"]),
                    Code = Convert.ToString(row["Code"])
                });
            }
            return list;
        }

        public async Task<string?> GetHikPendingOpPayloadDbAsync(int deviceId, string op, string employeeNo)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT TOP 1 payload FROM dbo.WN_HIK_PendingOps WITH (NOLOCK)
                WHERE device_id = @DeviceId AND op = @Op AND ISNULL(employee_no, '') = @Emp
                ORDER BY id DESC;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@DeviceId", deviceId);
            cmd.Parameters.AddWithValue("@Op", op);
            cmd.Parameters.AddWithValue("@Emp", employeeNo);
            var result = await cmd.ExecuteScalarAsync();
            return result is string json ? json : null;
        }

        /// <summary>
        /// Queue an op for an offline machine. Same semantics as HIK queueOp(): one row per device + op + employee,
        /// replaced on re-queue, and a 'queued:op' line in WN_HIK_SyncLog.
        /// </summary>
        public async Task QueueHikPendingOpDbAsync(int deviceId, string op, string employeeNo, string payloadJson)
        {
            await using var c = await Open();
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRAN;
                DELETE FROM dbo.WN_HIK_PendingOps WHERE device_id = @DeviceId AND op = @Op AND ISNULL(employee_no, '') = @Emp;
                INSERT INTO dbo.WN_HIK_PendingOps (device_id, op, employee_no, payload) VALUES (@DeviceId, @Op, @Emp, @Payload);
                IF OBJECT_ID('dbo.WN_HIK_SyncLog', 'U') IS NOT NULL
                    INSERT INTO dbo.WN_HIK_SyncLog (Employee_id, Device_id, Action, Ok, Detail)
                    VALUES (NULL, @DeviceId, CONCAT('queued:', @Op), 1, CONCAT('{""employee_no"":""', @Emp, '"",""source"":""worknest""}'));
                COMMIT;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@DeviceId", deviceId);
            cmd.Parameters.AddWithValue("@Op", op);
            cmd.Parameters.AddWithValue("@Emp", employeeNo);
            cmd.Parameters.AddWithValue("@Payload", payloadJson);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- dbo.OrderStatus (status lookup by description) ---

        public async Task<IReadOnlyDictionary<int, string>> GetOrderStatusesDbAsync()
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand("SELECT Id, Description FROM dbo.OrderStatus WITH (NOLOCK) WHERE ISNULL(Status, 1) = 1;", c);
            var map = new Dictionary<int, string>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) map[Convert.ToInt32(r.GetValue(0))] = r.IsDBNull(1) ? "" : Convert.ToString(r.GetValue(1)) ?? "";
            return map;
        }

        // --- Challan Validity Extension (existing WN_Challan_* procedures) ---

        public async Task<IDictionary<string, object?>?> SearchChallanDbAsync(string query)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Challan_Search", c);
            cmd.Parameters.Add("@Query", SqlDbType.NVarChar, 100).Value = query;
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        /// <summary>Returns the procedure's validation message (RAISERROR) as an error instead of throwing.</summary>
        public async Task<(bool Ok, string? Error)> ExtendChallanValidityDbAsync(int bookingId, DateTime newExpiryDate, string updatedBy, string? remarks)
        {
            try
            {
                await using var c = await Open();
                await using var cmd = SP("dbo.WN_Challan_ExtendValidity", c);
                cmd.Parameters.AddWithValue("@BookingId", bookingId);
                cmd.Parameters.Add("@NewExpiryDate", SqlDbType.Date).Value = newExpiryDate.Date;
                cmd.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 200).Value = updatedBy;
                cmd.Parameters.Add("@Remarks", SqlDbType.NVarChar, 500).Value = (object?)remarks ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync();
                return (true, null);
            }
            catch (SqlException ex) when (ex.Class == 16)
            {
                return (false, ex.Message); // e.g. "New expiry date must be after the current expiry date."
            }
        }

        // --- Hikvision challan-based access suspension (WN_HIK_AccessSuspension_* SPs) ---

        public async Task<IEnumerable<HikAccessSuspensionChange>> RunHikAccessSuspensionDbAsync(DateTime? today = null)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_HIK_AccessSuspension_Run", c);
            cmd.Parameters.Add("@Today", SqlDbType.Date).Value = (today ?? _clock.Today).Date; // business (Pakistan) date
            var list = new List<HikAccessSuspensionChange>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var row = ToDict(r);
                list.Add(new HikAccessSuspensionChange
                {
                    SuspensionId = Convert.ToInt32(row["SuspensionId"]),
                    BookingId = Convert.ToInt32(row["BookingId"]),
                    ShouldBlock = row["ShouldBlock"] != null && Convert.ToBoolean(row["ShouldBlock"])
                });
            }
            return list;
        }

        public async Task SetHikAccessSuspensionAppliedDbAsync(int suspensionId, bool machinesBlocked)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_HIK_AccessSuspension_SetApplied", c);
            cmd.Parameters.AddWithValue("@SuspensionId", suspensionId);
            cmd.Parameters.AddWithValue("@MachinesBlocked", machinesBlocked);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IDictionary<string, object?>?> GetHikAccessSuspensionByBookingDetailDbAsync(int bookingDetailId)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_HIK_AccessSuspension_GetByBookingDetail", c);
            cmd.Parameters.AddWithValue("@BookingDetailId", bookingDetailId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : null;
        }

        /// <summary>
        /// Dashboard overview (read-only): [0] machine / queued-operation counts,
        /// [1] open suspensions of bookings that are still running.
        /// </summary>
        public async Task<List<List<IDictionary<string, object?>>>> GetHikAccessOverviewDbAsync()
        {
            await using var c = await Open();
            const string sql = @"
                DECLARE @Today DATE = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE);
                SELECT
                    (SELECT COUNT(*) FROM dbo.WN_HIK_Devices WITH (NOLOCK)) AS Devices,
                    (SELECT COUNT(*) FROM dbo.WN_HIK_Devices WITH (NOLOCK) WHERE Online = 1) AS DevicesOnline,
                    (SELECT COUNT(*) FROM dbo.WN_HIK_PendingOps WITH (NOLOCK)) AS PendingOps,
                    (SELECT COUNT(DISTINCT device_id) FROM dbo.WN_HIK_PendingOps WITH (NOLOCK)) AS PendingOpsDevices;

                SELECT
                    s.Id AS SuspensionId, s.BookingId, s.Reason, s.SuspendedAt, s.OverrideUntil,
                    bd.BookingDetailId, bd.SpaceName, bd.SpaceCode, bd.SpaceCount, bd.BookingEnd,
                    COALESCE(NULLIF(cu.Company, ''), NULLIF(LTRIM(RTRIM(CONCAT(cu.FirstName, ' ', cu.LastName))), ''), cu.Email, bd.CustomerEmail) AS Customer,
                    ppl.EnrolledPeople,
                    ovr.CreatedByEmail AS OverrideByEmail, ovr.Reason AS OverrideReason
                FROM dbo.WN_HIK_BookingAccessSuspensions s WITH (NOLOCK)
                JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = s.BookingId
                CROSS APPLY (SELECT MIN(d.Id) AS BookingDetailId, MIN(d.SpaceName) AS SpaceName, MIN(d.SpaceCode) AS SpaceCode,
                                    COUNT(*) AS SpaceCount, MAX(d.EndDateTime) AS BookingEnd,
                                    MIN(d.CustomerCode) AS CustomerCode, MIN(d.CustomerEmail) AS CustomerEmail
                               FROM dbo.WN_BookingDetails d WITH (NOLOCK)
                              WHERE d.BookingGuid = b.IdGUID AND d.IsDeleted = 0) bd
                OUTER APPLY (SELECT TOP 1 Company, FirstName, LastName, Email FROM dbo.WN_Customers WITH (NOLOCK)
                             WHERE Code = bd.CustomerCode OR Email = bd.CustomerEmail) cu
                OUTER APPLY (SELECT COUNT(DISTINCT ba.PersonId) AS EnrolledPeople
                               FROM dbo.WN_BookingAttendants ba WITH (NOLOCK)
                               JOIN dbo.WN_BookingDetails d2 WITH (NOLOCK) ON d2.Id = ba.BookingDetailId AND d2.IsDeleted = 0
                               JOIN dbo.WN_HIK_PersonMap hpm WITH (NOLOCK) ON hpm.PersonId = ba.PersonId
                              WHERE d2.BookingGuid = b.IdGUID
                                AND (ba.AssignedTo IS NULL OR ba.AssignedTo >= @Today)) ppl
                OUTER APPLY (SELECT TOP 1 o.CreatedByEmail, o.Reason FROM dbo.WN_HIK_BookingAccessOverrides o WITH (NOLOCK)
                              WHERE o.SuspensionId = s.Id ORDER BY o.Id DESC) ovr
                WHERE s.ResolvedAt IS NULL
                  AND ISNULL(b.IsDeleted, 0) = 0
                  AND b.BookingStatusId NOT IN (3, 4, 6, 86) -- rejected / old cancelled / no show / cancelled
                  AND bd.BookingDetailId IS NOT NULL
                  AND bd.BookingEnd >= @Today   -- booking still running
                ORDER BY CASE WHEN s.OverrideUntil >= @Today THEN 1 ELSE 0 END, ppl.EnrolledPeople DESC, s.SuspendedAt DESC;";
            await using var cmd = new SqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadResultSets(r);
        }

        public async Task<(int? SuspensionId, int? BookingId)> ExtendHikAccessSuspensionDbAsync(int bookingDetailId, DateTime overrideUntil, string reason, int? createdById, string? createdByEmail)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_HIK_AccessSuspension_Extend", c);
            cmd.Parameters.AddWithValue("@BookingDetailId", bookingDetailId);
            cmd.Parameters.Add("@OverrideUntil", SqlDbType.Date).Value = overrideUntil.Date;
            cmd.Parameters.AddWithValue("@Reason", reason);
            cmd.Parameters.Add("@CreatedById", SqlDbType.Int).Value = (object?)createdById ?? DBNull.Value;
            cmd.Parameters.Add("@CreatedByEmail", SqlDbType.NVarChar, 200).Value = (object?)createdByEmail ?? DBNull.Value;
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return (null, null);
            var row = ToDict(r);
            return (row["SuspensionId"] != null ? Convert.ToInt32(row["SuspensionId"]) : null,
                    row["BookingId"] != null ? Convert.ToInt32(row["BookingId"]) : null);
        }

        /// <summary>
        /// Challans of the booking behind a booked space â€” read only.
        /// Result sets: [0] booking (Id, ChallanNumber, ValidityDate), [1] invoices (void = 5 excluded),
        /// [2] booking challans (WN_Challans + amounts from WN_vw_BookingSummary + voucher status from WN_Payments).
        /// </summary>
        public async Task<List<List<IDictionary<string, object?>>>> GetBookingChallansByBookingDetailDbAsync(int bookingDetailId)
        {
            await using var c = await Open();
            const string sql = @"
                DECLARE @BookingId INT = (
                    SELECT TOP 1 b.Id
                    FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
                    JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.IdGUID = bd.BookingGuid
                    WHERE bd.Id = @BookingDetailId);

                SELECT b.Id AS BookingId, b.ChallanNumber, b.ValidityDate
                FROM dbo.WN_Bookings b WITH (NOLOCK)
                WHERE b.Id = @BookingId;

                SELECT i.Id, i.InvoiceNumber, i.InvoiceTypeId, i.IssuedOn, i.DueOn,
                       i.BillingPeriodStart, i.BillingPeriodEnd,
                       ISNULL(i.GrandTotal, 0) AS GrandTotal, ISNULL(i.PaidTotal, 0) AS PaidTotal, i.StatusId
                FROM dbo.WN_Invoices i WITH (NOLOCK)
                WHERE i.BookingId = @BookingId AND ISNULL(i.StatusId, 0) <> 5
                  AND ISNULL(i.StatusId, 0) NOT IN (SELECT Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) = 'Cancelled')
                ORDER BY i.IssuedOn DESC, i.Id DESC;

                SELECT c.Id, c.ChallanNumber, c.IssuedOn, c.ValidUntil, c.StatusId AS ChallanStatusId,
                       (SELECT bk.ValidityDate FROM dbo.WN_Bookings bk WITH (NOLOCK) WHERE bk.Id = c.BookingId) AS BookingValidityDate,
                       v.CurrentCycleAmount, v.TotalContractAmount, v.TotalPaidAmount, v.BalanceLeft,
                       p.StatusId AS PaymentStatusId, p.Amount AS VoucherAmount
                FROM dbo.WN_Challans c WITH (NOLOCK)
                LEFT JOIN dbo.WN_vw_BookingSummary v WITH (NOLOCK) ON v.BookingId = c.BookingId
                OUTER APPLY (SELECT TOP 1 pay.StatusId, pay.Amount
                             FROM dbo.WN_Payments pay WITH (NOLOCK)
                             WHERE pay.TransactionRef = c.ChallanNumber -- the challan's own voucher only
                             ORDER BY pay.Id DESC) p
                WHERE c.BookingId = @BookingId
                ORDER BY c.CreatedOn DESC, c.Id DESC;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@BookingDetailId", bookingDetailId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadResultSets(r);
        }

        /// <summary>Booked spaces (WN_BookingDetails) of a booking â€” read only.</summary>
        public async Task<IEnumerable<int>> GetBookingDetailIdsForBookingDbAsync(int bookingId)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT bd.Id
                FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
                JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.IdGUID = bd.BookingGuid
                WHERE b.Id = @BookingId;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            var ids = new List<int>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
            return ids;
        }

        // --- Hikvision Staff (janitors, office boys, â€¦ â€” tagged machine users without a booking) ---

        public async Task<IEnumerable<IDictionary<string, object?>>> GetHikStaffDbAsync(string? employeeNo = null)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT u.employee_no, u.name, u.cnic, u.tag_id, t.Name AS tag,
                       (SELECT COUNT(1) FROM dbo.WN_HIK_PendingOps po WITH (NOLOCK) WHERE po.employee_no = u.employee_no) AS pending_ops
                FROM dbo.WN_HIK_Users u WITH (NOLOCK)
                LEFT JOIN dbo.WN_HIK_Tags t WITH (NOLOCK) ON t.Id = u.tag_id
                WHERE u.tag_id IS NOT NULL AND (@Emp IS NULL OR u.employee_no = @Emp)
                ORDER BY u.name;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@Emp", SqlDbType.NVarChar, 32).Value = string.IsNullOrWhiteSpace(employeeNo) ? DBNull.Value : employeeNo.Trim();
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetHikTagsDbAsync()
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand("SELECT Id, Name FROM dbo.WN_HIK_Tags WITH (NOLOCK) WHERE Status = 1 ORDER BY Name;", c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        /// <summary>Adds a job tag (or re-activates an inactive one with the same name); returns its Id.</summary>
        public async Task<int> AddHikTagDbAsync(string name)
        {
            await using var c = await Open();
            const string sql = @"
                DECLARE @Id INT = (SELECT Id FROM dbo.WN_HIK_Tags WHERE Name = @Name);
                IF @Id IS NULL
                BEGIN
                    INSERT INTO dbo.WN_HIK_Tags (Name, Status) VALUES (@Name, 1);
                    SET @Id = SCOPE_IDENTITY();
                END
                ELSE
                    UPDATE dbo.WN_HIK_Tags SET Status = 1 WHERE Id = @Id;
                SELECT @Id;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Name", name.Trim());
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        /// <summary>
        /// Allocates the next free member employee # (same range and lock as attendants) and records the
        /// staff member in WN_HIK_Users (CNIC + tag) â€” the HIK sync keeps that row across its rebuilds.
        /// </summary>
        public async Task<string> CreateHikStaffDbAsync(string name, string cnic, int? tagId, int floor = 0)
        {
            await using var c = await Open();
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRAN;
                EXEC sp_getapplock @Resource = 'WN_HIK_PersonMap_Allocate', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;

                DECLARE @max INT = CASE WHEN @Floor > 999 AND @Floor < 8500 THEN @Floor ELSE 999 END;
                SELECT @max = CASE WHEN MAX(n) > @max THEN MAX(n) ELSE @max END
                FROM (SELECT TRY_CAST(MachineID AS INT) AS n FROM dbo.WN_HIK_PersonMap) t WHERE n < 8500;
                SELECT @max = CASE WHEN MAX(n) > @max THEN MAX(n) ELSE @max END
                FROM (SELECT TRY_CAST(employee_no AS INT) AS n FROM dbo.WN_HIK_Users) t WHERE n < 8500;
                IF OBJECT_ID('dbo.WN_HIK_Employees') IS NOT NULL
                    EXEC sp_executesql N'SELECT @m = CASE WHEN MAX(n) > @m THEN MAX(n) ELSE @m END FROM (SELECT TRY_CAST(employee_no AS INT) AS n FROM dbo.WN_HIK_Employees) t WHERE n < 8500',
                        N'@m INT OUTPUT', @m = @max OUTPUT;
                IF OBJECT_ID('dbo.WN_HIK_Settings') IS NOT NULL
                    EXEC sp_executesql N'SELECT @m = CASE WHEN MAX(n) > @m THEN MAX(n) ELSE @m END FROM (SELECT TRY_CAST([value] AS INT) AS n FROM dbo.WN_HIK_Settings WHERE [key] = ''max_member_no'') t WHERE n < 8500',
                        N'@m INT OUTPUT', @m = @max OUTPUT;

                IF @max + 1 >= 8500 THROW 50001, 'Machine ID range (1000-8499) is full.', 1;
                DECLARE @emp NVARCHAR(32) = CAST(@max + 1 AS NVARCHAR(32));
                INSERT INTO dbo.WN_HIK_Users (employee_no, name, cnic, tag_id, machines, machine_count)
                VALUES (@emp, @Name, @Cnic, @TagId, '[]', 0);

                COMMIT;
                SELECT @emp;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Name", name.Trim());
            cmd.Parameters.AddWithValue("@Cnic", cnic);
            cmd.Parameters.Add("@TagId", SqlDbType.Int).Value = (object?)tagId ?? DBNull.Value;
            cmd.Parameters.AddWithValue("@Floor", floor);
            return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? throw new InvalidOperationException("Could not allocate an employee number.");
        }

        public async Task DeleteHikStaffDbAsync(string employeeNo)
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand("DELETE FROM dbo.WN_HIK_Users WHERE employee_no = @Emp AND tag_id IS NOT NULL;", c);
            cmd.Parameters.AddWithValue("@Emp", employeeNo);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Per-device user rosters as cached by the HIK sync engine (WN_HIK_DevCache.Users_snapshot).</summary>
        public async Task<IEnumerable<(int DeviceId, string? UsersJson)>> GetHikDevCacheSnapshotsDbAsync()
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand("SELECT Device_id, Users_snapshot FROM dbo.WN_HIK_DevCache WITH (NOLOCK) WHERE Users_snapshot IS NOT NULL;", c);
            var list = new List<(int, string?)>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add((r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1)));
            return list;
        }

        public async Task<IEnumerable<int>> GetHikPendingOpDeviceIdsDbAsync(string employeeNo)
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand("SELECT DISTINCT device_id FROM dbo.WN_HIK_PendingOps WITH (NOLOCK) WHERE employee_no = @Emp AND op = 'grant';", c);
            cmd.Parameters.AddWithValue("@Emp", employeeNo);
            var ids = new List<int>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
            return ids;
        }

        /// <summary>
        /// All machines in an Entrance group (group name starting with "Entrance", as the HIK dashboard treats them).
        /// </summary>
        public async Task<IEnumerable<int>> GetHikEntranceDeviceIdsDbAsync()
        {
            await using var c = await Open();
            const string sql = @"
                SELECT d.Id
                FROM dbo.WN_HIK_Devices d WITH (NOLOCK)
                JOIN dbo.WN_HIK_Groups g WITH (NOLOCK) ON g.Id = d.Group_id
                WHERE g.Name LIKE N'Entrance%';";
            await using var cmd = new SqlCommand(sql, c);
            var ids = new List<int>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
            return ids;
        }

        // --- Hikvision Access Dashboard / Activity Log / Analytics (read-only) ---

        // Denied codes used when WN_HIK_EventCategories has no row for an event code.
        private const string HikDeniedFallbackSql = "CASE WHEN e.access_event IN (9, 23, 39, 76, 112) THEN 1 ELSE 0 END";

        private static async Task<List<List<IDictionary<string, object?>>>> ReadResultSets(SqlDataReader r)
        {
            var sets = new List<List<IDictionary<string, object?>>>();
            do { sets.Add(await ReadAll(r)); } while (await r.NextResultAsync());
            return sets;
        }

        public async Task<IDictionary<string, object?>> GetHikAccessStatsDbAsync()
        {
            await using var c = await Open();
            var sql = $@"
                DECLARE @Today DATETIME2(0) = CAST(CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE) AS DATETIME2(0));
                DECLARE @Tomorrow DATETIME2(0) = DATEADD(day, 1, @Today);
                DECLARE @Yesterday DATETIME2(0) = DATEADD(day, -1, @Today);
                SELECT
                    (SELECT COUNT(*) FROM dbo.WN_HIK_Devices WITH (NOLOCK)) AS devices,
                    (SELECT COUNT(*) FROM dbo.WN_HIK_Devices WITH (NOLOCK) WHERE Online = 1) AS devicesOnline,
                    (SELECT COUNT(*) FROM dbo.WN_HIK_Employees WITH (NOLOCK) WHERE status = 'active') AS activeMembers,
                    (SELECT COUNT(*) FROM dbo.WN_HIK_Employees WITH (NOLOCK) WHERE status = 'expired') AS expiredMembers,
                    (SELECT COUNT(*) FROM dbo.WN_HIK_Employees WITH (NOLOCK) WHERE kind = 'card') AS cards,
                    (SELECT COUNT(*) FROM dbo.WN_HIK_AccessGrants WITH (NOLOCK) WHERE sync_state IN ('pending', 'error', 'removing')) AS pendingSync,
                    (SELECT COUNT(*) FROM dbo.WN_HIK_Events WITH (NOLOCK) WHERE event_time >= @Today AND event_time < @Tomorrow) AS todayScans,
                    (SELECT COUNT(*) FROM dbo.WN_HIK_Events WITH (NOLOCK) WHERE event_time >= @Yesterday AND event_time < @Today) AS yesterdayScans,
                    (SELECT COUNT(DISTINCT employee_no) FROM dbo.WN_HIK_Events WITH (NOLOCK)
                        WHERE event_time >= @Today AND event_time < @Tomorrow AND employee_no IS NOT NULL AND employee_no <> '') AS uniqueToday,
                    (SELECT COUNT(*) FROM dbo.WN_HIK_Events e WITH (NOLOCK)
                        LEFT JOIN dbo.WN_HIK_EventCategories ec WITH (NOLOCK) ON ec.Code = e.access_event
                        WHERE e.event_time >= @Today AND e.event_time < @Tomorrow
                          AND COALESCE(CAST(ec.Is_denied AS INT), {HikDeniedFallbackSql}) = 1) AS deniedToday;";
            await using var cmd = new SqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ToDict(r) : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetHikAccessEventsDbAsync(DateTime? from, DateTime? to, int? deviceId, string? employeeNo, string? name, int limit)
        {
            await using var c = await Open();
            var sql = $@"
                SELECT TOP (@Limit)
                    e.id,
                    e.device_id,
                    e.device_name,
                    e.employee_no,
                    e.name,
                    e.card_no,
                    e.access_event,
                    e.event_time,
                    COALESCE(ec.Label, e.access_event_details) AS label,
                    COALESCE(CAST(ec.Is_denied AS INT), {HikDeniedFallbackSql}) AS is_denied
                FROM dbo.WN_HIK_Events e WITH (NOLOCK)
                LEFT JOIN dbo.WN_HIK_EventCategories ec WITH (NOLOCK) ON ec.Code = e.access_event
                WHERE (@From IS NULL OR e.event_time >= @From)
                  AND (@To IS NULL OR e.event_time < @To)
                  AND (@DeviceId IS NULL OR e.device_id = @DeviceId)
                  AND ((@Emp IS NULL AND @Name IS NULL)
                       OR (@Emp IS NOT NULL AND e.employee_no = @Emp)
                       OR (@Emp IS NULL AND e.name = @Name))
                ORDER BY e.event_time DESC, e.id DESC;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = (object?)from ?? DBNull.Value;
            cmd.Parameters.Add("@To", SqlDbType.DateTime2).Value = (object?)to ?? DBNull.Value;
            cmd.Parameters.Add("@DeviceId", SqlDbType.Int).Value = (object?)deviceId ?? DBNull.Value;
            cmd.Parameters.Add("@Emp", SqlDbType.NVarChar, 32).Value = string.IsNullOrWhiteSpace(employeeNo) ? DBNull.Value : employeeNo.Trim();
            cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 128).Value = string.IsNullOrWhiteSpace(name) ? DBNull.Value : name.Trim();
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetHikSyncActivityDbAsync(int limit)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_HIK_Activity_Recent", c);
            cmd.Parameters.AddWithValue("@limit", limit);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetHikExpiringDbAsync(int days)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT e.employee_no, e.name, e.valid_end, d.Device_Name AS device
                FROM dbo.WN_HIK_Employees e WITH (NOLOCK)
                JOIN dbo.WN_HIK_AccessGrants g WITH (NOLOCK) ON g.employee_id = e.id
                JOIN dbo.WN_HIK_Devices d WITH (NOLOCK) ON d.Id = g.device_id
                WHERE e.valid_end IS NOT NULL AND e.valid_end <= DATEADD(day, @Days, CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATETIME2(0)))
                ORDER BY e.valid_end ASC;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Days", days);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
        }

        /// <summary>
        /// Result sets: [0] hourly, [1] doors, [2] top users, [3] totals + method split, [4] daily.
        /// Range is [from, to) on the terminal's local event_time.
        /// </summary>
        public async Task<List<List<IDictionary<string, object?>>>> GetHikAccessAnalyticsDbAsync(DateTime from, DateTime to)
        {
            await using var c = await Open();
            var sql = $@"
                SELECT DATEPART(hour, event_time) AS hr, COUNT(*) AS cnt
                FROM dbo.WN_HIK_Events WITH (NOLOCK)
                WHERE event_time >= @From AND event_time < @To
                GROUP BY DATEPART(hour, event_time);

                SELECT device_name AS name, COUNT(*) AS cnt
                FROM dbo.WN_HIK_Events WITH (NOLOCK)
                WHERE event_time >= @From AND event_time < @To AND device_name IS NOT NULL
                GROUP BY device_name
                ORDER BY cnt DESC;

                SELECT TOP 10 employee_no, name, COUNT(*) AS cnt
                FROM dbo.WN_HIK_Events WITH (NOLOCK)
                WHERE event_time >= @From AND event_time < @To
                  AND ((employee_no IS NOT NULL AND employee_no <> '') OR (name IS NOT NULL AND name <> ''))
                GROUP BY employee_no, name
                ORDER BY cnt DESC;

                SELECT
                    COUNT(*) AS total,
                    COUNT(DISTINCT NULLIF(e.employee_no, '')) AS uniquePeople,
                    SUM(CASE WHEN COALESCE(CAST(ec.Is_denied AS INT), {HikDeniedFallbackSql}) = 1 THEN 1 ELSE 0 END) AS denied,
                    SUM(CASE WHEN e.access_event IN (38, 39) THEN 1 ELSE 0 END) AS fingerprint,
                    SUM(CASE WHEN e.access_event IN (75, 76, 104) THEN 1 ELSE 0 END) AS face,
                    SUM(CASE WHEN e.access_event NOT IN (38, 39, 75, 76, 104) AND NULLIF(e.card_no, '') IS NOT NULL THEN 1 ELSE 0 END) AS card,
                    SUM(CASE WHEN NULLIF(e.card_no, '') IS NULL AND (e.access_event BETWEEN 21 AND 26 OR e.access_event = 31) THEN 1 ELSE 0 END) AS door
                FROM dbo.WN_HIK_Events e WITH (NOLOCK)
                LEFT JOIN dbo.WN_HIK_EventCategories ec WITH (NOLOCK) ON ec.Code = e.access_event
                WHERE e.event_time >= @From AND e.event_time < @To;

                SELECT CAST(event_time AS DATE) AS d, COUNT(*) AS cnt
                FROM dbo.WN_HIK_Events WITH (NOLOCK)
                WHERE event_time >= @From AND event_time < @To
                GROUP BY CAST(event_time AS DATE)
                ORDER BY d;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = from;
            cmd.Parameters.Add("@To", SqlDbType.DateTime2).Value = to;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadResultSets(r);
        }

        /// <summary>
        /// Result sets: [0] profile (WN_HIK_Employees + WN_HIK_Users), [1] doors, [2] hourly, [3] range totals, [4] all-time total.
        /// The person is matched by employee # when given, otherwise by name.
        /// </summary>
        public async Task<List<List<IDictionary<string, object?>>>> GetHikUserAnalyticsDbAsync(string? employeeNo, string? name, DateTime from, DateTime to)
        {
            await using var c = await Open();
            const string who = "((@Emp IS NOT NULL AND employee_no = @Emp) OR (@Emp IS NULL AND name = @Name))";
            var sql = $@"
                SELECT
                    COALESCE(u.employee_no, emp.employee_no) AS employee_no,
                    COALESCE(u.name, emp.name) AS name,
                    emp.card_no, emp.status, emp.valid_end, u.room
                FROM (SELECT 1 AS x) one
                OUTER APPLY (SELECT TOP 1 employee_no, name, room FROM dbo.WN_HIK_Users WITH (NOLOCK) WHERE {who}) u
                OUTER APPLY (SELECT TOP 1 employee_no, name, card_no, status, valid_end
                             FROM dbo.WN_HIK_Employees WITH (NOLOCK) WHERE {who}
                             ORDER BY valid_end DESC) emp;

                SELECT device_name AS name, COUNT(*) AS cnt
                FROM dbo.WN_HIK_Events WITH (NOLOCK)
                WHERE {who} AND event_time >= @From AND event_time < @To AND device_name IS NOT NULL
                GROUP BY device_name
                ORDER BY cnt DESC;

                SELECT DATEPART(hour, event_time) AS hr, COUNT(*) AS cnt
                FROM dbo.WN_HIK_Events WITH (NOLOCK)
                WHERE {who} AND event_time >= @From AND event_time < @To
                GROUP BY DATEPART(hour, event_time);

                SELECT COUNT(*) AS total, MIN(event_time) AS firstScan, MAX(event_time) AS lastScan,
                       MAX(NULLIF(card_no, '')) AS cardNo
                FROM dbo.WN_HIK_Events WITH (NOLOCK)
                WHERE {who} AND event_time >= @From AND event_time < @To;

                SELECT COUNT(*) AS total
                FROM dbo.WN_HIK_Events WITH (NOLOCK)
                WHERE {who};";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@Emp", SqlDbType.NVarChar, 32).Value = string.IsNullOrWhiteSpace(employeeNo) ? DBNull.Value : employeeNo.Trim();
            cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 128).Value = string.IsNullOrWhiteSpace(name) ? DBNull.Value : name.Trim();
            cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = from;
            cmd.Parameters.Add("@To", SqlDbType.DateTime2).Value = to;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadResultSets(r);
        }

        /// <summary>
        /// Stores the CNIC against the member in WN_HIK_Users (keyed by employee # + name, as the HIK dashboard does).
        /// </summary>
        public async Task SaveHikUserCnicDbAsync(string employeeNo, string name, string? cnic)
        {
            await using var c = await Open();
            const string sql = @"
                IF OBJECT_ID('dbo.WN_HIK_Users') IS NOT NULL
                    EXEC sp_executesql N'
                        MERGE dbo.WN_HIK_Users WITH (HOLDLOCK) AS t
                        USING (SELECT @Emp AS emp, @Name AS nm) s ON t.employee_no = s.emp AND t.name = s.nm
                        WHEN MATCHED THEN UPDATE SET cnic = COALESCE(@Cnic, cnic)
                        WHEN NOT MATCHED THEN INSERT (employee_no, name, cnic) VALUES (s.emp, s.nm, @Cnic);',
                        N'@Emp NVARCHAR(32), @Name NVARCHAR(128), @Cnic NVARCHAR(20)',
                        @Emp = @Emp, @Name = @Name, @Cnic = @Cnic;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Emp", employeeNo);
            cmd.Parameters.AddWithValue("@Name", name.Trim());
            cmd.Parameters.AddWithValue("@Cnic", (object?)cnic ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Vaults a captured fingerprint template in WN_HIK_FpVault so the HIK sync engine can replicate it later.
        /// </summary>
        public async Task SaveHikFingerprintTemplateDbAsync(string employeeNo, string name, int fingerNo, string template)
        {
            await using var c = await Open();
            const string sql = @"
                IF OBJECT_ID('dbo.WN_HIK_FpVault') IS NOT NULL
                    EXEC sp_executesql N'
                        MERGE dbo.WN_HIK_FpVault WITH (HOLDLOCK) AS t
                        USING (SELECT @Emp AS emp, @Name AS nm, @FingerNo AS fno) s
                            ON t.employee_no = s.emp AND t.name = s.nm AND t.finger_no = s.fno
                        WHEN MATCHED THEN UPDATE SET template = @Template, updated_at = SYSDATETIME()
                        WHEN NOT MATCHED THEN INSERT (employee_no, name, finger_no, template) VALUES (s.emp, s.nm, s.fno, @Template);',
                        N'@Emp NVARCHAR(32), @Name NVARCHAR(128), @FingerNo INT, @Template NVARCHAR(MAX)',
                        @Emp = @Emp, @Name = @Name, @FingerNo = @FingerNo, @Template = @Template;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Emp", employeeNo);
            cmd.Parameters.AddWithValue("@Name", name.Trim());
            cmd.Parameters.AddWithValue("@FingerNo", fingerNo);
            cmd.Parameters.AddWithValue("@Template", template);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- UniFi dashboard: client-count history + client aliases (all times UTC) ---

        public async Task<IEnumerable<(DateTime SampledAt, int Wifi, int Wired, int Guest, int Online, int Offline, string? SitesJson)>> GetUnifiHistoryDbAsync(int days)
        {
            await using var c = await Open();
            const string sql = @"
                SELECT SampledAt, Wifi, Wired, Guest, Online, Offline, SitesJson
                FROM dbo.WN_UNIFI_History WITH (NOLOCK)
                WHERE SampledAt >= DATEADD(DAY, -@Days, SYSUTCDATETIME())
                ORDER BY SampledAt;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Days", days);
            var list = new List<(DateTime, int, int, int, int, int, string?)>();
            await using var r = await cmd.ExecuteReaderAsync();
            static int I(SqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
            while (await r.ReadAsync())
            {
                list.Add((DateTime.SpecifyKind(r.GetDateTime(0), DateTimeKind.Utc), I(r, 1), I(r, 2), I(r, 3), I(r, 4), I(r, 5),
                          r.IsDBNull(6) ? null : r.GetString(6)));
            }
            return list;
        }

        /// <summary>
        /// Inserts a history sample unless one already exists within minGapSeconds of it, so several API
        /// instances polling the same network don't write duplicate samples. Returns true when inserted.
        /// </summary>
        public async Task<bool> InsertUnifiHistoryDbAsync(DateTime sampledAt, int wifi, int wired, int guest, int online, int offline, string? sitesJson, int minGapSeconds)
        {
            await using var c = await Open();
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRAN;
                IF NOT EXISTS (SELECT 1 FROM dbo.WN_UNIFI_History WITH (UPDLOCK, HOLDLOCK)
                               WHERE SampledAt > DATEADD(SECOND, -@MinGap, @SampledAt))
                BEGIN
                    INSERT INTO dbo.WN_UNIFI_History (SampledAt, Wifi, Wired, Guest, Online, Offline, SitesJson)
                    VALUES (@SampledAt, @Wifi, @Wired, @Guest, @Online, @Offline, @SitesJson);
                    SELECT 1;
                END
                ELSE
                    SELECT 0;
                COMMIT;";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@SampledAt", SqlDbType.DateTime2).Value = sampledAt;
            cmd.Parameters.AddWithValue("@MinGap", Math.Max(0, minGapSeconds));
            cmd.Parameters.AddWithValue("@Wifi", wifi);
            cmd.Parameters.AddWithValue("@Wired", wired);
            cmd.Parameters.AddWithValue("@Guest", guest);
            cmd.Parameters.AddWithValue("@Online", online);
            cmd.Parameters.AddWithValue("@Offline", offline);
            cmd.Parameters.Add("@SitesJson", SqlDbType.NVarChar, -1).Value = (object?)sitesJson ?? DBNull.Value;
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result ?? 0) == 1;
        }

        public async Task<int> DeleteUnifiHistoryOlderThanDbAsync(int days)
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand("DELETE FROM dbo.WN_UNIFI_History WHERE SampledAt < DATEADD(DAY, -@Days, SYSUTCDATETIME());", c);
            cmd.Parameters.AddWithValue("@Days", days);
            return await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Client aliases keyed by MAC (lower-case aa:bb:cc:dd:ee:ff).</summary>
        public async Task<Dictionary<string, string>> GetUnifiClientAliasesDbAsync()
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand("SELECT Mac, Alias FROM dbo.WN_UNIFI_ClientAliases WITH (NOLOCK);", c);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                if (!r.IsDBNull(0) && !r.IsDBNull(1)) map[r.GetString(0)] = r.GetString(1);
            }
            return map;
        }

        public async Task UpsertUnifiClientAliasDbAsync(string mac, string alias, string? updatedByEmail)
        {
            await using var c = await Open();
            const string sql = @"
                MERGE dbo.WN_UNIFI_ClientAliases WITH (HOLDLOCK) AS t
                USING (SELECT @Mac AS Mac) s ON t.Mac = s.Mac
                WHEN MATCHED THEN UPDATE SET Alias = @Alias, UpdatedAt = SYSUTCDATETIME(), UpdatedByEmail = @Email
                WHEN NOT MATCHED THEN INSERT (Mac, Alias, UpdatedAt, UpdatedByEmail) VALUES (s.Mac, @Alias, SYSUTCDATETIME(), @Email);";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.AddWithValue("@Mac", mac);
            cmd.Parameters.AddWithValue("@Alias", alias);
            cmd.Parameters.AddWithValue("@Email", (object?)updatedByEmail ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteUnifiClientAliasDbAsync(string mac)
        {
            await using var c = await Open();
            await using var cmd = new SqlCommand("DELETE FROM dbo.WN_UNIFI_ClientAliases WHERE Mac = @Mac;", c);
            cmd.Parameters.AddWithValue("@Mac", mac);
            await cmd.ExecuteNonQueryAsync();
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

                DateTime pStart = request.StartOn ?? _clock.Today;
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




