using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.CustomInvoice;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Custom invoices and per-user permissions: dbo.WN_CustomInvoice_* / dbo.WN_UserPermissions_* (script
    /// CI_01_custom_invoice). Own connection. Before the insert and the permission change it sets the audit
    /// SESSION_CONTEXT (AppUserId, AppUserEmail, SourceApp = 'WebAPI') from the JWT, as DbRepository.Open() does, so the
    /// audit triggers record who did it. SQL errors 2812 / 2715 / 208 (procedure / table type / table missing) mean CI_01
    /// has not run: <see cref="CustomInvoiceDatabaseMissingException"/>; THROW 51000 is a rule: <see cref="CustomInvoiceRuleException"/>.
    /// </summary>
    public class CustomInvoiceRepository : ICustomInvoiceRepository
    {
        public const string LinesTypeName = "dbo.TT_WN_CustomInvoiceLedgerLine";

        private readonly string _connectionString;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public CustomInvoiceRepository(IConfiguration configuration, IHttpContextAccessor? httpContextAccessor = null)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
            _httpContextAccessor = httpContextAccessor;
        }

        // ---- permissions -----------------------------------------------------------------------------------------

        public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(int userId)
        {
            var rows = await QueryAsync("dbo.WN_UserPermissions_GetByUser", false, cmd =>
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId);
            return rows.Select(CustomInvoiceRows.Permission).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).ToList();
        }

        public async Task<IReadOnlyList<CustomInvoiceAccessItem>> ListAdminsAsync(string permission)
        {
            var rows = await QueryAsync("dbo.WN_UserPermissions_ListAdmins", false, cmd =>
                cmd.Parameters.Add("@Permission", SqlDbType.NVarChar, 100).Value = permission);
            return rows.Select(CustomInvoiceRows.AccessItem).ToList();
        }

        public async Task<CustomInvoiceAccessItem?> SetPermissionAsync(int userId, string permission, bool grant, int grantedById)
        {
            var rows = await QueryAsync("dbo.WN_UserPermissions_Set", true, cmd =>
            {
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                cmd.Parameters.Add("@Permission", SqlDbType.NVarChar, 100).Value = permission;
                cmd.Parameters.Add("@Grant", SqlDbType.Bit).Value = grant;
                cmd.Parameters.Add("@GrantedById", SqlDbType.Int).Value = grantedById;
            });
            return rows.Count > 0 ? CustomInvoiceRows.AccessItem(rows[0]) : null;
        }

        // ---- lookups ---------------------------------------------------------------------------------------------

        public async Task<IReadOnlyList<CustomInvoiceCustomer>> SearchCustomersAsync(string? search, int top)
        {
            var rows = await QueryAsync("dbo.WN_CustomInvoice_SearchCustomers", false, cmd =>
            {
                cmd.Parameters.Add("@Search", SqlDbType.NVarChar, 200).Value = (object?)search ?? DBNull.Value;
                cmd.Parameters.Add("@Top", SqlDbType.Int).Value = top;
            });
            return rows.Select(CustomInvoiceRows.Customer).ToList();
        }

        public async Task<IReadOnlyList<CustomInvoiceBooking>> GetBookingsAsync(int userId)
        {
            var rows = await QueryAsync("dbo.WN_CustomInvoice_GetBookings", false, cmd =>
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId);
            return rows.Select(CustomInvoiceRows.Booking).ToList();
        }

        public async Task<IReadOnlyList<CustomInvoiceReplaceable>> GetReplaceableAsync(int bookingId)
        {
            var rows = await QueryAsync("dbo.WN_CustomInvoice_GetReplaceable", false, cmd =>
                cmd.Parameters.Add("@BookingId", SqlDbType.Int).Value = bookingId);
            return rows.Select(CustomInvoiceRows.Replaceable).ToList();
        }

        public async Task<CustomInvoiceLedgerDefaults> GetLedgerDefaultsAsync(int locationId, DateTime date)
        {
            var rows = await QueryAsync("dbo.WN_CustomInvoice_GetLedgerDefaults", false, cmd =>
            {
                cmd.Parameters.Add("@LocationId", SqlDbType.Int).Value = locationId;
                cmd.Parameters.Add("@Date", SqlDbType.Date).Value = date.Date;
            });
            return CustomInvoiceRows.LedgerDefaults(rows.Count > 0 ? rows[0] : null);
        }

        public async Task<IReadOnlyList<CustomInvoiceAccount>> SearchAccountsAsync(string? search, int top)
        {
            var rows = await QueryAsync("dbo.WN_CustomInvoice_SearchAccounts", false, cmd =>
            {
                cmd.Parameters.Add("@Search", SqlDbType.NVarChar, 200).Value = (object?)search ?? DBNull.Value;
                cmd.Parameters.Add("@Top", SqlDbType.Int).Value = top;
            });
            return rows.Select(CustomInvoiceRows.Account).ToList();
        }

        public async Task<CustomInvoiceListPage> GetListAsync(string? search, int page, int pageSize)
        {
            var rows = await QueryAsync("dbo.WN_CustomInvoice_GetList", false, cmd =>
            {
                cmd.Parameters.Add("@Search", SqlDbType.NVarChar, 200).Value = (object?)search ?? DBNull.Value;
                cmd.Parameters.Add("@Page", SqlDbType.Int).Value = page;
                cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;
            });
            var total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) && t is not null ? Convert.ToInt32(t) : 0;
            return new CustomInvoiceListPage { Items = rows.Select(CustomInvoiceRows.ListItem).ToList(), Total = total };
        }

        // ---- insert ----------------------------------------------------------------------------------------------

        /// <summary>
        /// dbo.WN_CustomInvoice_Insert. Its answer is the result set with an InvoiceId column: when it creates the
        /// customer's account, dbo.WN_CustomersVendors_SyncOnInvoice first returns a one-column set (SELECT @ShortGUID).
        /// </summary>
        public async Task<CustomInvoiceCreated?> InsertAsync(CustomInvoiceInsert x)
        {
            var rows = await QueryAsync("dbo.WN_CustomInvoice_Insert", true, resultWithColumn: "InvoiceId", parameters: cmd =>
            {
                var p = cmd.Parameters;
                p.Add("@UserId", SqlDbType.Int).Value = x.UserId;
                p.Add("@BookingId", SqlDbType.Int).Value = (object?)x.BookingId ?? DBNull.Value;
                p.Add("@LocationId", SqlDbType.Int).Value = (object?)x.LocationId ?? DBNull.Value;
                p.Add("@IssuedOn", SqlDbType.Date).Value = x.IssuedOn.Date;
                p.Add("@DueOn", SqlDbType.Date).Value = x.DueOn.Date;
                p.Add("@PeriodFrom", SqlDbType.Date).Value = (object?)x.PeriodFrom?.Date ?? DBNull.Value;
                p.Add("@PeriodTo", SqlDbType.Date).Value = (object?)x.PeriodTo?.Date ?? DBNull.Value;
                AddDecimal(cmd, "@RoomRentExclTax", x.RoomRentExclTax);
                AddDecimal(cmd, "@ServiceCharges", x.ServiceCharges);
                AddDecimal(cmd, "@TaxPercent", x.TaxPercent, 9, 4);  // NULL when none was sent
                AddDecimal(cmd, "@TaxOnServiceCharges", x.TaxOnServiceCharges);
                AddDecimal(cmd, "@SecurityDepositAmount", x.SecurityDepositAmount);
                AddDecimal(cmd, "@GrandTotal", x.GrandTotal);
                p.Add("@CustomReason", SqlDbType.NVarChar, 500).Value = x.CustomReason;
                p.Add("@CreatedById", SqlDbType.Int).Value = x.CreatedById;
                p.Add("@VoucherDate", SqlDbType.Date).Value = x.VoucherDate.Date;
                p.Add("@VoucherDescription", SqlDbType.NVarChar, 500).Value = (object?)x.VoucherDescription ?? DBNull.Value;
                AddDecimal(cmd, "@CustomerLedgerDebit", x.CustomerLedgerDebit);
                p.Add("@CustomerLedgerNarration", SqlDbType.NVarChar, 500).Value = (object?)x.CustomerLedgerNarration ?? DBNull.Value;
                p.Add("@ReplaceInvoiceId", SqlDbType.Int).Value = (object?)x.ReplaceInvoiceId ?? DBNull.Value;
                var lines = p.Add("@Lines", SqlDbType.Structured);
                lines.TypeName = LinesTypeName;
                lines.Value = LinesTable(x.Lines);
            });
            return rows.Count > 0 ? CustomInvoiceRows.Created(rows[0]) : null;
        }

        public async Task<Dictionary<int, int>> GetInvoiceLocationsAsync(IReadOnlyCollection<int> invoiceIds)
        {
            var result = new Dictionary<int, int>();
            foreach (var chunk in invoiceIds.Where(id => id > 0).Distinct().Chunk(500))
            {
                // The ids are ints read from dbo.WN_CustomInvoice_GetList, never user text. The chosen location
                // (WN_CustomInvoices, CI_01) or else the booking's.
                var rows = await QueryTextAsync($@"
                    SELECT i.Id AS InvoiceId, COALESCE(ci.LocationId, s.LocationId) AS LocationId
                      FROM dbo.WN_Invoices i WITH (NOLOCK)
                      LEFT JOIN dbo.WN_CustomInvoices ci WITH (NOLOCK) ON ci.InvoiceId = i.Id
                      LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
                      LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                     WHERE i.Id IN ({string.Join(",", chunk)});");
                foreach (var r in rows)
                    if (r.TryGetValue("InvoiceId", out var id) && id is not null && r.TryGetValue("LocationId", out var loc) && loc is not null)
                        result[Convert.ToInt32(id)] = Convert.ToInt32(loc);
            }
            return result;
        }

        public async Task<Dictionary<int, HashSet<int>>> GetCustomerLocationsAsync(IReadOnlyCollection<(int UserId, int? CustomerId)> customers)
        {
            var result = new Dictionary<int, HashSet<int>>();
            var pairs = customers.Where(c => c.UserId > 0).Distinct().ToList();
            if (pairs.Count == 0) return result;
            // Ints read from dbo.WN_CustomInvoice_SearchCustomers (at most 50), never user text.
            var values = string.Join(", ", pairs.Select(p => $"({p.UserId}, {(p.CustomerId is > 0 ? p.CustomerId.Value.ToString() : "NULL")})"));
            var rows = await QueryTextAsync($@"
                DECLARE @P TABLE (UserId INT NOT NULL, CustomerId INT NULL);
                INSERT INTO @P (UserId, CustomerId) VALUES {values};
                SELECT p.UserId, u.LocationId
                  FROM @P p
                  JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = p.UserId
                 WHERE u.LocationId IS NOT NULL
                UNION
                SELECT p.UserId, s.LocationId
                  FROM @P p
                  LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.Id = p.CustomerId
                  JOIN dbo.WN_Bookings b WITH (NOLOCK)
                    ON b.UserId = p.UserId OR (c.Code IS NOT NULL AND c.Code <> N'' AND b.CustomerCode = c.Code)
                  JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                 WHERE s.LocationId IS NOT NULL;");
            foreach (var r in rows)
            {
                if (!r.TryGetValue("UserId", out var u) || u is null || !r.TryGetValue("LocationId", out var l) || l is null) continue;
                var userId = Convert.ToInt32(u);
                if (!result.TryGetValue(userId, out var set)) result[userId] = set = new HashSet<int>();
                set.Add(Convert.ToInt32(l));
            }
            return result;
        }

        /// <summary>The voucher lines as dbo.TT_WN_CustomInvoiceLedgerLine ([LineNo], AccountId, Debit, Credit, Narration).</summary>
        public static DataTable LinesTable(IEnumerable<CustomInvoiceInsertLine> lines)
        {
            var t = new DataTable();
            t.Columns.Add("LineNo", typeof(int));
            t.Columns.Add("AccountId", typeof(int));
            t.Columns.Add("Debit", typeof(decimal));
            t.Columns.Add("Credit", typeof(decimal));
            t.Columns.Add("Narration", typeof(string));
            foreach (var l in lines)
                t.Rows.Add(l.LineNo, l.AccountId, l.Debit, l.Credit, (object?)l.Narration ?? DBNull.Value);
            return t;
        }

        // ---- errors ----------------------------------------------------------------------------------------------

        /// <summary>
        /// The exception to throw instead of <paramref name="ex"/>: CI_01 missing (2812 / 2715 / 208) or a procedure
        /// rule (51000, with that error's own message); null for any other error (left to ExceptionMiddleware).
        /// </summary>
        public static Exception? Translate(SqlException ex)
        {
            var errors = ex.Errors.Cast<SqlError>().ToList();
            var rule = errors.FirstOrDefault(e => e.Number == CustomInvoiceDbErrors.Rule);
            if (rule != null || ex.Number == CustomInvoiceDbErrors.Rule)
                return new CustomInvoiceRuleException(rule?.Message ?? ex.Message, ex);
            if (CustomInvoiceDbErrors.IsMissingObject(ex.Number) || errors.Any(e => CustomInvoiceDbErrors.IsMissingObject(e.Number)))
                return new CustomInvoiceDatabaseMissingException(ex);
            return null;
        }

        // ---- plumbing --------------------------------------------------------------------------------------------

        private async Task<List<IDictionary<string, object?>>> QueryAsync(string procedure, bool audit,
            Action<SqlCommand> parameters, string? resultWithColumn = null)
        {
            try
            {
                await using var c = await OpenAsync(audit);
                await using var cmd = new SqlCommand(procedure, c) { CommandType = CommandType.StoredProcedure };
                parameters(cmd);
                await using var r = await cmd.ExecuteReaderAsync();
                return resultWithColumn == null ? await ReadRowsAsync(r) : await ReadResultWithColumnAsync(r, resultWithColumn);
            }
            catch (SqlException ex) when (Translate(ex) is { } mapped)
            {
                throw mapped;
            }
        }

        private async Task<List<IDictionary<string, object?>>> QueryTextAsync(string sql)
        {
            try
            {
                await using var c = await OpenAsync(false);
                await using var cmd = new SqlCommand(sql, c);
                await using var r = await cmd.ExecuteReaderAsync();
                return await ReadRowsAsync(r);
            }
            catch (SqlException ex) when (Translate(ex) is { } mapped)
            {
                throw mapped;
            }
        }

        private async Task<SqlConnection> OpenAsync(bool audit)
        {
            var c = new SqlConnection(_connectionString);
            try
            {
                await c.OpenAsync();
                await using (var set = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", c))
                    await set.ExecuteNonQueryAsync();
                if (audit) await SetAuditContextAsync(c);
                return c;
            }
            catch
            {
                await c.DisposeAsync();
                throw;
            }
        }

        /// <summary>The rows of the current result set (column name -> value, DBNull = null, UTC audit columns marked UTC).</summary>
        public static async Task<List<IDictionary<string, object?>>> ReadRowsAsync(DbDataReader r)
        {
            var rows = new List<IDictionary<string, object?>>();
            while (await r.ReadAsync())
            {
                var d = new Dictionary<string, object?>(r.FieldCount, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < r.FieldCount; i++)
                {
                    var name = r.GetName(i);
                    var value = r.IsDBNull(i) ? null : r.GetValue(i);
                    d[name] = DbDateTimeKinds.Normalize(name, value); // GrantedAt (UTC) gets Kind = Utc
                }
                rows.Add(d);
            }
            return rows;
        }

        /// <summary>
        /// The rows of the first result set that has <paramref name="column"/>, skipping the ones before it (e.g. the
        /// SELECT @ShortGUID of a nested procedure); empty when no result set has it.
        /// </summary>
        public static async Task<List<IDictionary<string, object?>>> ReadResultWithColumnAsync(DbDataReader r, string column)
        {
            do
            {
                if (HasColumn(r, column)) return await ReadRowsAsync(r);
            } while (await r.NextResultAsync());
            return new List<IDictionary<string, object?>>();
        }

        private static bool HasColumn(DbDataReader r, string column)
        {
            for (int i = 0; i < r.FieldCount; i++)
                if (string.Equals(r.GetName(i), column, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// The audit identity, exactly as DbRepository.Open() sets it from the JWT: AppUserId (the numeric id claim, or
        /// the WN_Users row with that public id / e-mail), AppUserEmail and SourceApp = 'WebAPI'. A failure here never
        /// blocks the change.
        /// </summary>
        private async Task SetAuditContextAsync(SqlConnection c)
        {
            try
            {
                var user = _httpContextAccessor?.HttpContext?.User;
                if (user == null) return;

                var userIdOrGuid = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                ?? user.FindFirst("sub")?.Value
                                ?? user.FindFirst("UserId")?.Value
                                ?? user.FindFirst("id")?.Value;
                var userEmail = user.FindFirst(ClaimTypes.Email)?.Value
                             ?? user.FindFirst("email")?.Value
                             ?? user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userIdOrGuid) && string.IsNullOrWhiteSpace(userEmail)) return;

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
            catch
            {
                // Same as DbRepository.Open(): the audit context never blocks the change itself.
            }
        }

        private static void AddDecimal(SqlCommand cmd, string name, decimal? value, byte precision = 18, byte scale = 4)
        {
            var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
            p.Precision = precision;
            p.Scale = scale;
            p.Value = (object?)value ?? DBNull.Value;
        }
    }
}
