using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.DayPass;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>Day Pass data (Database/Booking/DP_01_day_pass.txt): WN_Bookings.BookingKind = 'DayPass', dbo.WN_DayPasses, dbo.WN_DayPassHistory.</summary>
    public class DayPassRepository : IDayPassRepository
    {
        private readonly string _cs;

        public DayPassRepository(IConfiguration configuration)
        {
            _cs = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection string is missing.");
        }

        private async Task<SqlConnection> OpenAsync()
        {
            var c = new SqlConnection(_cs);
            await c.OpenAsync();
            return c;
        }

        // ---- readiness (cached, like TransferRepository.IsRedesignReadyAsync) --------------------------

        private static volatile bool _ready;
        private static DateTime _checkedAt = DateTime.MinValue;

        public async Task<bool> IsInstalledAsync()
        {
            if (_ready) return true;
            if ((DateTime.UtcNow - _checkedAt).TotalSeconds < 60) return false;
            _checkedAt = DateTime.UtcNow;
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand(@"SELECT CASE WHEN COL_LENGTH(N'dbo.WN_Bookings', N'BookingKind') IS NOT NULL
                AND OBJECT_ID(N'dbo.WN_DayPasses', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.WN_DayPassHistory', N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.WN_DayPass_GetSpaceUsage', N'P') IS NOT NULL THEN 1 ELSE 0 END", c);
            _ready = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
            return _ready;
        }

        // ---- helpers -----------------------------------------------------------------------------------

        private static string? Str(SqlDataReader r, string n) { var i = r.GetOrdinal(n); return r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture); }
        private static int Int(SqlDataReader r, string n) { var i = r.GetOrdinal(n); return r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i)); }
        private static int? IntN(SqlDataReader r, string n) { var i = r.GetOrdinal(n); return r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i)); }
        private static decimal Dec(SqlDataReader r, string n) { var i = r.GetOrdinal(n); return r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i)); }
        private static DateTime Dt(SqlDataReader r, string n) => r.GetDateTime(r.GetOrdinal(n));
        private static DateTime? DtN(SqlDataReader r, string n) { var i = r.GetOrdinal(n); return r.IsDBNull(i) ? null : r.GetDateTime(i); }
        private static object V(object? v) => v ?? DBNull.Value;

        private static string Csv(IEnumerable<int> ids) => string.Join(",", ids.Distinct().Select(i => i.ToString(CultureInfo.InvariantCulture)));

        // ---- spaces ---------------------------------------------------------------------------------------

        public async Task<DayPassSpaceInfo?> GetSpaceInfoAsync(int spaceId, DateTime today)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand(@"
                SELECT s.Id, s.Name, ISNULL(NULLIF(s.Capacity, 0), 1) AS Capacity, s.IsActive, s.LocationId, l.Name AS LocationName, sc.Code AS Category,
                       ISNULL(tax.TaxPercentage, 0) AS TaxPercent
                FROM dbo.WN_Spaces s WITH (NOLOCK)
                JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
                JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
                JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = s.LocationId
                OUTER APPLY (SELECT TOP 1 t.TaxPercentage FROM dbo.WN_Tax t WITH (NOLOCK)
                              WHERE t.ProvinceId = l.ProvinceId AND t.StartDate <= @today AND (t.EndDate IS NULL OR t.EndDate > @today)
                              ORDER BY t.StartDate DESC) tax
                WHERE s.Id = @id", c);
            cmd.Parameters.AddWithValue("@id", spaceId);
            cmd.Parameters.Add("@today", SqlDbType.Date).Value = today.Date;
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            return new DayPassSpaceInfo
            {
                SpaceId = Int(r, "Id"), Name = Str(r, "Name") ?? "", Capacity = Int(r, "Capacity"), IsActive = Convert.ToBoolean(r["IsActive"]),
                LocationId = Int(r, "LocationId"), LocationName = Str(r, "LocationName") ?? "", Category = Str(r, "Category") ?? "", TaxPercent = Dec(r, "TaxPercent")
            };
        }

        public async Task<List<DayPassSpaceUsage>> GetSpaceUsageAsync(DateTime date, string window, IReadOnlyCollection<int>? locationIds, int? spaceId)
        {
            await using var c = await OpenAsync();
            return await ReadUsageAsync(c, null, date, window, locationIds, spaceId);
        }

        private static async Task<List<DayPassSpaceUsage>> ReadUsageAsync(SqlConnection c, SqlTransaction? tx, DateTime date, string window, IReadOnlyCollection<int>? locationIds, int? spaceId)
        {
            await using var cmd = new SqlCommand("dbo.WN_DayPass_GetSpaceUsage", c, tx) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@Date", SqlDbType.Date).Value = date.Date;
            cmd.Parameters.Add("@Window", SqlDbType.VarChar, 10).Value = window;
            cmd.Parameters.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = locationIds == null ? DBNull.Value : Csv(locationIds);
            cmd.Parameters.Add("@SpaceId", SqlDbType.Int).Value = V(spaceId);
            var list = new List<DayPassSpaceUsage>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var pi = r.GetOrdinal("ListPrice");
                list.Add(new DayPassSpaceUsage
                {
                    SpaceId = Int(r, "SpaceId"), SpaceName = Str(r, "SpaceName") ?? "", CategoryCode = Str(r, "CategoryCode") ?? "",
                    LocationId = Int(r, "LocationId"), LocationName = Str(r, "LocationName") ?? "", Capacity = Int(r, "Capacity"),
                    SeatsInUse = Int(r, "SeatsInUse"), BookingCount = Int(r, "BookingCount"),
                    ListPrice = r.IsDBNull(pi) ? null : Convert.ToDecimal(r.GetValue(pi))
                });
            }
            return list;
        }

        // ---- customers / people ------------------------------------------------------------------------------

        public async Task<List<DayPassCustomerRow>> SearchCustomersAsync(string? search, IReadOnlyCollection<int>? allowedLocationIds)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_Customers_SearchScoped", c) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@Query", search ?? "");
            cmd.Parameters.AddWithValue("@LocationIds", allowedLocationIds == null ? DBNull.Value : Csv(allowedLocationIds));
            var list = new List<DayPassCustomerRow>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new DayPassCustomerRow
                {
                    Code = Str(r, "Code") ?? "", Name = Str(r, "FullName") ?? "", Company = Str(r, "Company"), Email = Str(r, "Email"),
                    Phone = Str(r, "PhoneNumber"), Cnic = Str(r, "CnicOrPassport")
                });
            return list;
        }

        public async Task<DayPassCustomerRow?> GetCustomerAsync(string code)
        {
            await using var c = await OpenAsync();
            return await ReadCustomerAsync(c, null, code);
        }

        private static async Task<DayPassCustomerRow?> ReadCustomerAsync(SqlConnection c, SqlTransaction? tx, string code)
        {
            await using var cmd = new SqlCommand(@"SELECT TOP 1 c.Id, c.Code, LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))) AS FullName, c.Company, c.Email, c.PhoneNumber, c.CnicOrPassport
                FROM dbo.WN_Customers c WITH (NOLOCK) WHERE c.Code = @c AND c.IsActive = 1", c, tx);
            cmd.Parameters.AddWithValue("@c", code);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            return new DayPassCustomerRow { Code = Str(r, "Code") ?? "", Name = Str(r, "FullName") ?? "", Company = Str(r, "Company"), Email = Str(r, "Email"), Phone = Str(r, "PhoneNumber"), Cnic = Str(r, "CnicOrPassport") };
        }

        public async Task<string?> FindWalkInConflictAsync(string email, string cnicDigits)
        {
            await using var c = await OpenAsync();
            return await FindConflictAsync(c, null, email, cnicDigits);
        }

        private static async Task<string?> FindConflictAsync(SqlConnection c, SqlTransaction? tx, string email, string cnicDigits)
        {
            await using var cmd = new SqlCommand(@"
                SELECT TOP 1 c.Code, CASE WHEN LOWER(LTRIM(RTRIM(c.Email))) = LOWER(@e) THEN 1 ELSE 2 END AS Why
                FROM dbo.WN_Customers c WITH (NOLOCK)
                WHERE LOWER(LTRIM(RTRIM(c.Email))) = LOWER(@e)
                   OR (@n <> '' AND REPLACE(REPLACE(ISNULL(c.CnicOrPassport, ''), '-', ''), ' ', '') = @n)
                ORDER BY 2", c, tx);
            cmd.Parameters.AddWithValue("@e", email.Trim());
            cmd.Parameters.AddWithValue("@n", cnicDigits ?? "");
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            return (Int(r, "Why") == 1 ? "A customer with this email already exists" : "A customer with this CNIC already exists") + $" ({Str(r, "Code")}): choose them from the customer list instead of a walk-in.";
        }

        public async Task<bool> IsBankAccountForLocationAsync(int accountId, int locationId)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand(@"
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.AccountsCOA a WITH (NOLOCK)
                    JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = @loc
                    WHERE a.Id = @a
                      AND (a.CompanyId = l.CompanyId OR a.IsForAllCompanies = 1 OR a.CompanyId IS NULL)
                      AND ISNULL(a.ActiveStatus, 1) = 1 AND a.Description NOT LIKE '%DONATION%'
                      AND (a.AccountNature IN ('Bank Account', 'Bank Credit Card Account') OR a.Description LIKE '%Bank%' OR a.AccountsCategoryId = 21)
                      AND ISNULL(a.AccountNature, '') NOT IN ('Cash in Hand', 'Petty Cash')
                      AND a.Description NOT IN ('CASH IN HAND', 'CASH')
                ) THEN 1 ELSE 0 END", c);
            cmd.Parameters.AddWithValue("@a", accountId);
            cmd.Parameters.AddWithValue("@loc", locationId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        public async Task<(int Id, string Name)?> ResolveActorAsync(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("SELECT TOP 1 Id, ISNULL(NULLIF(Name, ''), Email) AS Name FROM dbo.WN_Users WITH (NOLOCK) WHERE LOWER(LTRIM(RTRIM(Email))) = LOWER(@e) ORDER BY IsActive DESC, Id", c);
            cmd.Parameters.AddWithValue("@e", email.Trim());
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? (Int(r, "Id"), Str(r, "Name") ?? email) : null;
        }

        // ---- create ------------------------------------------------------------------------------------------------

        private static SqlCommand Cmd(string sql, SqlConnection c, SqlTransaction tx) => new(sql, c, tx);

        private static SqlCommand Proc(string name, SqlConnection c, SqlTransaction tx) => new(name, c, tx) { CommandType = CommandType.StoredProcedure };

        public async Task<int> CreateAsync(DayPassCreateData d)
        {
            var r = d.Request;
            await using var c = await OpenAsync();
            await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
            try
            {
                await using (var set = Cmd("SET XACT_ABORT ON; EXEC sys.sp_set_session_context N'AppUserId', @u; EXEC sys.sp_set_session_context N'AppUserEmail', @e; EXEC sys.sp_set_session_context N'SourceApp', N'DayPass';", c, tx))
                {
                    set.Parameters.AddWithValue("@u", d.ActorId);
                    set.Parameters.AddWithValue("@e", V(d.ActorEmail));
                    await set.ExecuteNonQueryAsync();
                }

                // 1. No race between two staff: one at a time per space, then the same range lock the booking procedures take.
                await using (var lk = Cmd(@"DECLARE @rc INT; EXEC @rc = sys.sp_getapplock @Resource = @res, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 15000; SELECT @rc;", c, tx))
                {
                    lk.Parameters.AddWithValue("@res", "WN_DayPass_Space_" + r.SpaceId.ToString(CultureInfo.InvariantCulture));
                    if (Convert.ToInt32(await lk.ExecuteScalarAsync()) < 0)
                        throw new DayPassException(409, "This space is being booked by someone else right now. Try again in a moment.");
                }
                await using (var rl = Cmd(@"SELECT COUNT(*) FROM dbo.WN_Bookings WITH (UPDLOCK, HOLDLOCK) WHERE SpaceId = @s AND IsDeleted = 0 AND BookingStatusId IN (1, 2, 5, 33) AND @bs < EndOn AND @be > StartOn", c, tx))
                {
                    rl.Parameters.AddWithValue("@s", r.SpaceId);
                    rl.Parameters.Add("@bs", SqlDbType.DateTime2).Value = d.BlockStart;
                    rl.Parameters.Add("@be", SqlDbType.DateTime2).Value = d.BlockEnd;
                    await rl.ExecuteScalarAsync();
                }
                var usage = (await ReadUsageAsync(c, tx, d.Date, d.Window, null, r.SpaceId)).FirstOrDefault()
                            ?? throw new DayPassException(404, "Space not found.");
                var type = DayPassRules.SpaceTypeOf(usage.CategoryCode);
                var cap = Math.Max(usage.Capacity, 1);
                int inUse = DayPassRules.IsShared(type) ? Math.Min(usage.SeatsInUse, cap) : (usage.BookingCount > 0 ? cap : 0);
                var (free, whole) = DayPassRules.Availability(type, cap, inUse);
                var (_, _, seatError) = DayPassRules.ResolveSeats(type, cap, free, whole, d.WholeSpace, d.Seats);
                if (seatError != null) throw new DayPassException(409, seatError);

                // 2. Customer: existing, or a new walk-in (with its app login, so the pass shows in My Bookings).
                string customerCode; string customerName; string customerEmail; string? customerPhone; int customerId;
                if (!string.IsNullOrWhiteSpace(r.CustomerCode))
                {
                    await using var q = Cmd(@"SELECT TOP 1 c.Id, c.Code, LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))) AS FullName, c.Email, c.PhoneNumber, c.Company
                        FROM dbo.WN_Customers c WHERE c.Code = @c AND c.IsActive = 1", c, tx);
                    q.Parameters.AddWithValue("@c", r.CustomerCode!.Trim());
                    await using var rd = await q.ExecuteReaderAsync();
                    if (!await rd.ReadAsync()) throw new DayPassException(404, "Customer not found.");
                    customerId = Int(rd, "Id"); customerCode = Str(rd, "Code")!;
                    customerName = string.IsNullOrWhiteSpace(Str(rd, "FullName")) ? (Str(rd, "Company") ?? customerCode) : Str(rd, "FullName")!;
                    customerEmail = Str(rd, "Email") ?? ""; customerPhone = Str(rd, "PhoneNumber");
                }
                else
                {
                    var w = r.WalkIn!;
                    var conflict = await FindConflictAsync(c, tx, w.Email!.Trim(), DayPassRules.CnicDigits(w.Cnic));
                    if (conflict != null) throw new DayPassException(400, conflict);
                    var name = System.Text.RegularExpressions.Regex.Replace(w.Name!.Trim(), @"\s+", " ");
                    var sp = name.IndexOf(' ');
                    string first = sp < 0 ? name : name[..sp], last = sp < 0 ? "" : name[(sp + 1)..];
                    await using (var ins = Proc("dbo.WN_Customers_Insert", c, tx))
                    {
                        ins.Parameters.AddWithValue("@FirstName", first);
                        ins.Parameters.AddWithValue("@LastName", last == "" ? DBNull.Value : last);
                        ins.Parameters.AddWithValue("@Email", w.Email!.Trim());
                        ins.Parameters.AddWithValue("@PhoneNumber", w.Phone!.Trim());
                        ins.Parameters.AddWithValue("@CnicOrPassport", DayPassRules.CnicDigits(w.Cnic));
                        ins.Parameters.AddWithValue("@Notes", "Walk-in day pass customer");
                        ins.Parameters.AddWithValue("@CreatedBy", V(d.ActorEmail));
                        ins.Parameters.AddWithValue("@Company", string.IsNullOrWhiteSpace(w.Company) ? DBNull.Value : w.Company!.Trim());
                        customerId = Convert.ToInt32(await ins.ExecuteScalarAsync());
                    }
                    await using (var q = Cmd("SELECT Code FROM dbo.WN_Customers WHERE Id = @i", c, tx))
                    {
                        q.Parameters.AddWithValue("@i", customerId);
                        customerCode = Convert.ToString(await q.ExecuteScalarAsync()) ?? "";
                    }
                    customerName = name; customerEmail = w.Email!.Trim(); customerPhone = w.Phone!.Trim();
                }
                int userId;
                await using (var eu = Proc("dbo.WN_Customers_EnsureLoginUser", c, tx))
                {
                    eu.Parameters.AddWithValue("@CustomerId", customerId);
                    await using var rd = await eu.ExecuteReaderAsync();
                    if (!await rd.ReadAsync() || rd.IsDBNull(rd.GetOrdinal("UserId")))
                        throw new DayPassException(400, "This customer has no email on file: add it to the customer first (the receipt and the app login need it).");
                    userId = Convert.ToInt32(rd["UserId"]);
                }

                // 3. The booking row (flagged), its detail (the door machines read the window from it) and the pass.
                int bookingId; Guid bookingGuid;
                await using (var bk = Cmd(@"
                    INSERT INTO dbo.WN_Bookings (BookingDate, CustomerCode, UserId, SpaceId, StartOn, EndOn, BookingStatusId, IsDeleted, CreatedById, CreatedOn,
                        SubtotalAmount, TotalAmount, BillingPeriodMonths, ShiftType, Capacity, BookingKind, Notes,
                        AccountReceivableId, RentAccountId, ServicesIncomeId, SalesTaxId, SecurityReceivedId, SecurityRecivedId)
                    OUTPUT inserted.Id, inserted.IdGUID
                    SELECT SYSUTCDATETIME(), @cc, @u, s.Id, @bs, @be, 33, 0, @by, SYSDATETIME(),
                        @sub, @tot, 1, @shift, @seats, N'DayPass', @note,
                        COALESCE(s.AccountReceivableId, st.AccountReceivableId), COALESCE(s.RentAccountId, st.RentAccountId), COALESCE(s.ServicesIncomeId, st.ServicesIncomeId),
                        COALESCE(s.SalesTaxId, st.SalesTaxId), COALESCE(s.SecurityReceivedId, st.SecurityReceivedId, s.SecurityRecivedId), COALESCE(s.SecurityReceivedId, st.SecurityReceivedId, s.SecurityRecivedId)
                    FROM dbo.WN_Spaces s JOIN dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeId WHERE s.Id = @s", c, tx))
                {
                    bk.Parameters.AddWithValue("@cc", customerCode);
                    bk.Parameters.AddWithValue("@u", userId);
                    bk.Parameters.Add("@bs", SqlDbType.DateTime2).Value = d.BlockStart;
                    bk.Parameters.Add("@be", SqlDbType.DateTime2).Value = d.BlockEnd;
                    bk.Parameters.AddWithValue("@by", d.ActorId);
                    bk.Parameters.Add("@sub", SqlDbType.Decimal).Value = d.SubTotal;
                    bk.Parameters.Add("@tot", SqlDbType.Decimal).Value = d.Total;
                    bk.Parameters.AddWithValue("@shift", d.Shift);
                    bk.Parameters.AddWithValue("@seats", d.Seats);
                    bk.Parameters.AddWithValue("@note", V(string.IsNullOrWhiteSpace(r.Note) ? null : r.Note!.Trim()));
                    bk.Parameters.AddWithValue("@s", r.SpaceId);
                    await using var rd = await bk.ExecuteReaderAsync();
                    await rd.ReadAsync();
                    bookingId = Convert.ToInt32(rd[0]); bookingGuid = (Guid)rd[1];
                }
                string spaceName, spaceCode, categoryLabel;
                await using (var q = Cmd("SELECT s.Name, s.Code, sc.Label FROM dbo.WN_Spaces s JOIN dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeId JOIN dbo.WN_SpaceCategories sc ON sc.Id = st.CategoryId WHERE s.Id = @s", c, tx))
                {
                    q.Parameters.AddWithValue("@s", r.SpaceId);
                    await using var rd = await q.ExecuteReaderAsync();
                    await rd.ReadAsync();
                    spaceName = Str(rd, "Name") ?? ""; spaceCode = Str(rd, "Code") ?? ""; categoryLabel = Str(rd, "Label") ?? "";
                }
                // The window the machines enforce: a full day ends 23:59:59 of its day.
                var detailEnd = d.Window == DayPassRules.Full ? d.EndsAt.AddSeconds(-1) : d.EndsAt;
                await using (var bd = Proc("dbo.WN_BookingDetails_Insert", c, tx))
                {
                    bd.Parameters.AddWithValue("@BookingGuid", bookingGuid);
                    bd.Parameters.AddWithValue("@CustomerCode", customerCode);
                    bd.Parameters.AddWithValue("@CustomerName", customerName);
                    bd.Parameters.AddWithValue("@CustomerEmail", customerEmail);
                    bd.Parameters.AddWithValue("@SpaceName", spaceName);
                    bd.Parameters.AddWithValue("@SpaceCode", spaceCode);
                    bd.Parameters.AddWithValue("@SpaceCategory", categoryLabel);
                    bd.Parameters.Add("@StartDateTime", SqlDbType.DateTime).Value = d.StartsAt;
                    bd.Parameters.Add("@EndDateTime", SqlDbType.DateTime).Value = detailEnd;
                    bd.Parameters.Add("@RentAmount", SqlDbType.Decimal).Value = d.SubTotal;
                    bd.Parameters.Add("@SecurityDeposit", SqlDbType.Decimal).Value = 0m;
                    bd.Parameters.AddWithValue("@PaymentMethod", r.PaymentMethod!);
                    bd.Parameters.AddWithValue("@Notes", "Day pass");
                    bd.Parameters.Add("@AppliedTaxPercentage", SqlDbType.Decimal).Value = d.TaxPercent;
                    bd.Parameters.Add("@SupportChargeAmount", SqlDbType.Decimal).Value = 0m;
                    bd.Parameters.Add("@TaxAmount", SqlDbType.Decimal).Value = d.Tax;
                    await bd.ExecuteNonQueryAsync();
                }

                int passId;
                await using (var ins = Cmd(@"
                    INSERT INTO dbo.WN_DayPasses (BookingId, PassDate, Window, StartsAt, EndsAt, LocationId, SpaceId, CustomerCode, UserId, WholeSpace, Seats, PriceMode, UnitPrice,
                        SubTotal, ApplyTax, TaxPercent, Tax, Total, AccessMode, PaymentMethod, PaymentAccountId, PaymentReference, Note, Status, CreatedById, CreatedAt)
                    VALUES (@b, @date, @w, @sa, @ea, @loc, @s, @cc, @u, @whole, @seats, @pm, @unit, @sub, @at, @tp, @tax, @tot, @am, @pay, @acc, @ref, @note, 'Active', @by,
                        CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATETIME2(0)));
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", c, tx))
                {
                    ins.Parameters.AddWithValue("@b", bookingId);
                    ins.Parameters.Add("@date", SqlDbType.Date).Value = d.Date;
                    ins.Parameters.AddWithValue("@w", d.Window);
                    ins.Parameters.Add("@sa", SqlDbType.DateTime2).Value = d.StartsAt;
                    ins.Parameters.Add("@ea", SqlDbType.DateTime2).Value = d.EndsAt;
                    ins.Parameters.AddWithValue("@loc", r.LocationId);
                    ins.Parameters.AddWithValue("@s", r.SpaceId);
                    ins.Parameters.AddWithValue("@cc", customerCode);
                    ins.Parameters.AddWithValue("@u", userId);
                    ins.Parameters.AddWithValue("@whole", d.WholeSpace);
                    ins.Parameters.AddWithValue("@seats", d.Seats);
                    ins.Parameters.AddWithValue("@pm", d.PriceMode);
                    ins.Parameters.Add("@unit", SqlDbType.Decimal).Value = d.UnitPrice;
                    ins.Parameters.Add("@sub", SqlDbType.Decimal).Value = d.SubTotal;
                    ins.Parameters.AddWithValue("@at", r.ApplyTax);
                    ins.Parameters.Add("@tp", SqlDbType.Decimal).Value = d.TaxPercent;
                    ins.Parameters.Add("@tax", SqlDbType.Decimal).Value = d.Tax;
                    ins.Parameters.Add("@tot", SqlDbType.Decimal).Value = d.Total;
                    ins.Parameters.AddWithValue("@am", r.AccessMode!.Trim().ToLowerInvariant());
                    ins.Parameters.AddWithValue("@pay", r.PaymentMethod!);
                    ins.Parameters.AddWithValue("@acc", V(r.PaymentMethod == DayPassRules.PayBank ? r.BankAccountId : null));
                    ins.Parameters.AddWithValue("@ref", V(r.PaymentMethod == DayPassRules.PayBank ? r.PaymentReference!.Trim() : null));
                    ins.Parameters.AddWithValue("@note", V(string.IsNullOrWhiteSpace(r.Note) ? null : r.Note!.Trim()));
                    ins.Parameters.AddWithValue("@by", d.ActorId);
                    passId = Convert.ToInt32(await ins.ExecuteScalarAsync());
                }
                var passNumber = DayPassRules.PassNumber(passId);
                await using (var up = Cmd("UPDATE dbo.WN_DayPasses SET PassNumber = @n WHERE Id = @i", c, tx))
                {
                    up.Parameters.AddWithValue("@n", passNumber);
                    up.Parameters.AddWithValue("@i", passId);
                    await up.ExecuteNonQueryAsync();
                }

                // 4. The invoice: approved and paid at once. Revenue is Room Rent, tax is Sales Tax (no service charge, no deposit).
                int invoiceId; Guid invoiceGuid;
                var issued = d.Now;
                var invNumber = "INV-DP-" + passId.ToString("D5", CultureInfo.InvariantCulture);
                await using (var inv = Proc("dbo.WN_Invoices_Insert", c, tx))
                {
                    inv.Parameters.AddWithValue("@UserId", userId);
                    inv.Parameters.AddWithValue("@BookingId", bookingId);
                    inv.Parameters.AddWithValue("@InvoiceNumber", invNumber);
                    inv.Parameters.AddWithValue("@IssuedOn", issued);
                    inv.Parameters.AddWithValue("@DueOn", d.Today.Date);
                    inv.Parameters.Add("@SubTotal", SqlDbType.Decimal).Value = d.SubTotal;
                    inv.Parameters.Add("@DiscountTotal", SqlDbType.Decimal).Value = 0m;
                    inv.Parameters.Add("@TaxTotal", SqlDbType.Decimal).Value = d.Tax;
                    inv.Parameters.Add("@GrandTotal", SqlDbType.Decimal).Value = d.Total;
                    inv.Parameters.Add("@PaidTotal", SqlDbType.Decimal).Value = 0m;
                    inv.Parameters.AddWithValue("@CurrencyCode", "PKR");
                    inv.Parameters.AddWithValue("@Notes", $"Day pass {passNumber}: {spaceName}, {d.Date:yyyy-MM-dd}, {DayPassRules.WindowLabel(d.Window)}");
                    inv.Parameters.AddWithValue("@InvoiceTypeId", 1);
                    inv.Parameters.AddWithValue("@AdvanceRentMonths", 0);
                    inv.Parameters.AddWithValue("@SecurityDepositMonths", 0);
                    inv.Parameters.Add("@SecurityDepositAmount", SqlDbType.Decimal).Value = 0m;
                    inv.Parameters.AddWithValue("@BillingPeriodMonths", 0);
                    inv.Parameters.Add("@BillingPeriodStart", SqlDbType.DateTime).Value = d.Date;
                    inv.Parameters.Add("@BillingPeriodEnd", SqlDbType.DateTime).Value = d.Date;
                    inv.Parameters.AddWithValue("@CreatedById", d.ActorId);
                    inv.Parameters.AddWithValue("@SyncCustomerVendor", true);
                    inv.Parameters.Add("@RoomRentExclTax", SqlDbType.Decimal).Value = d.SubTotal;
                    inv.Parameters.Add("@ServiceCharges", SqlDbType.Decimal).Value = 0m;
                    inv.Parameters.Add("@TaxOnServiceCharges", SqlDbType.Decimal).Value = d.Tax;
                    inv.Parameters.AddWithValue("@LocationId", r.LocationId);
                    var pId = new SqlParameter("@InvoiceId", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    var pGuid = new SqlParameter("@GeneratedPublicId", SqlDbType.UniqueIdentifier) { Direction = ParameterDirection.Output };
                    inv.Parameters.Add(pId); inv.Parameters.Add(pGuid);
                    await inv.ExecuteNonQueryAsync();
                    if (pId.Value is DBNull or null || Convert.ToInt32(pId.Value) <= 0) throw new InvalidOperationException("The invoice of the day pass could not be created.");
                    invoiceId = Convert.ToInt32(pId.Value); invoiceGuid = (Guid)pGuid.Value;
                }
                await using (var ap = Cmd("UPDATE dbo.WN_Invoices SET ApprovalStatus = N'Approved', ApprovedAt = SYSUTCDATETIME(), ApprovedBy = @u, LocationId = @loc WHERE Id = @i", c, tx))
                {
                    ap.Parameters.AddWithValue("@u", d.ActorId); ap.Parameters.AddWithValue("@loc", r.LocationId); ap.Parameters.AddWithValue("@i", invoiceId);
                    await ap.ExecuteNonQueryAsync();
                }
                await using (var ln = Proc("dbo.WN_InsertInvoiceLine", c, tx))
                {
                    ln.Parameters.AddWithValue("@InvoiceId", invoiceId);
                    ln.Parameters.AddWithValue("@ChargeTypeId", (byte)1);
                    ln.Parameters.AddWithValue("@Description", $"Day pass {passNumber} - {spaceName} - {d.Date:yyyy-MM-dd} - {DayPassRules.WindowLabel(d.Window)}" + (d.WholeSpace ? "" : $" - {d.Seats} seat(s)"));
                    ln.Parameters.Add("@Quantity", SqlDbType.Decimal).Value = 1m;
                    ln.Parameters.Add("@UnitPrice", SqlDbType.Decimal).Value = d.SubTotal;
                    ln.Parameters.Add("@DiscountAmount", SqlDbType.Decimal).Value = 0m;
                    ln.Parameters.Add("@TaxRate", SqlDbType.Decimal).Value = 0m;
                    ln.Parameters.AddWithValue("@SortOrder", (short)1);
                    await ln.ExecuteNonQueryAsync();
                }

                // 5. The receipt (pays the whole balance; the procedure posts it and marks the invoice Paid).
                await using (var fr = Proc("dbo.WN_FeeReceipts_Insert", c, tx))
                {
                    fr.Parameters.AddWithValue("@InvoiceIdGUID", invoiceGuid);
                    fr.Parameters.AddWithValue("@CashInHandAccountId", V(r.PaymentMethod == DayPassRules.PayBank ? r.BankAccountId : null));
                    fr.Parameters.AddWithValue("@PaymentType", r.PaymentMethod == DayPassRules.PayBank ? "Bank" : "Cash");
                    fr.Parameters.AddWithValue("@Remarks", $"Day pass {passNumber}" + (r.PaymentMethod == DayPassRules.PayBank ? $" - bank ref {r.PaymentReference!.Trim()}" : ""));
                    fr.Parameters.AddWithValue("@CreatedById", d.ActorId);
                    fr.Parameters.AddWithValue("@DepositorName", customerName.Length > 150 ? customerName[..150] : customerName);
                    await fr.ExecuteNonQueryAsync();
                }
                int receiptId;
                await using (var q = Cmd("SELECT TOP 1 Id FROM dbo.WN_FeeReceipts WHERE InvoiceIdGUID = @g AND ISNULL(Status, 1) = 1 ORDER BY Id DESC", c, tx))
                {
                    q.Parameters.AddWithValue("@g", invoiceGuid);
                    var v = await q.ExecuteScalarAsync();
                    if (v == null || v is DBNull) throw new InvalidOperationException("The receipt of the day pass could not be created.");
                    receiptId = Convert.ToInt32(v);
                }
                await using (var up = Cmd("UPDATE dbo.WN_DayPasses SET InvoiceId = @i, ReceiptId = @r WHERE Id = @p", c, tx))
                {
                    up.Parameters.AddWithValue("@i", invoiceId); up.Parameters.AddWithValue("@r", receiptId); up.Parameters.AddWithValue("@p", passId);
                    await up.ExecuteNonQueryAsync();
                }
                await HistoryAsync(c, tx, passId, "Created", $"{spaceName}, {d.Date:yyyy-MM-dd}, {DayPassRules.WindowLabel(d.Window)}, {(d.WholeSpace ? "whole space" : d.Seats + " seat(s)")}, total PKR {d.Total:N2}", d.ActorId, d.ActorName);

                await tx.CommitAsync();
                return passId;
            }
            catch (SqlException ex) when (ex.Number is 50000 or 50001 || ex.Class is 16)
            {
                try { await tx.RollbackAsync(); } catch { }
                throw new DayPassException(400, ex.Message);
            }
            catch
            {
                try { await tx.RollbackAsync(); } catch { }
                throw;
            }
        }

        private static async Task HistoryAsync(SqlConnection c, SqlTransaction? tx, int passId, string action, string? note, int? byId, string? byName)
        {
            await using var cmd = new SqlCommand(@"INSERT INTO dbo.WN_DayPassHistory (DayPassId, At, ByUserId, ByName, Action, Note)
                VALUES (@p, CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATETIME2(0)), @u,
                COALESCE(@n, (SELECT TOP 1 ISNULL(NULLIF(Name, ''), Email) FROM dbo.WN_Users WHERE Id = @u)), @a, @note)", c, tx);
            cmd.Parameters.AddWithValue("@p", passId);
            cmd.Parameters.AddWithValue("@u", V(byId));
            cmd.Parameters.AddWithValue("@n", V(byName));
            cmd.Parameters.AddWithValue("@a", action);
            cmd.Parameters.AddWithValue("@note", V(string.IsNullOrWhiteSpace(note) ? null : (note.Length > 1000 ? note[..1000] : note)));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task AddHistoryAsync(int dayPassId, string action, string? note, int? byUserId, string? byName)
        {
            await using var c = await OpenAsync();
            await HistoryAsync(c, null, dayPassId, action, note, byUserId, byName ?? (byUserId == null ? "System" : null));
        }

        // ---- read ----------------------------------------------------------------------------------------------------

        private const string SelectPass = @"
            SELECT dp.Id, dp.BookingId, dp.PassNumber, dp.Status, dp.PassDate, dp.Window, dp.StartsAt, dp.EndsAt, dp.LocationId, l.Name AS LocationName,
                   dp.SpaceId, s.Name AS SpaceName, sc.Code AS CategoryCode, dp.WholeSpace, dp.Seats, dp.CustomerCode,
                   ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), c.Company) AS CustomerName, c.Email AS CustomerEmail, c.PhoneNumber AS CustomerPhone,
                   dp.PriceMode, dp.UnitPrice, dp.SubTotal, dp.TaxPercent, dp.Tax, dp.Total, dp.PaymentMethod, ac.Description AS BankAccountName, dp.PaymentReference,
                   dp.InvoiceId, i.InvoiceNumber, dp.ReceiptId, fr.Code AS ReceiptNumber, dp.AccessMode,
                   cu.Name AS CreatedByName, dp.CreatedAt, xu.Name AS CancelledByName, dp.CancelledAt, dp.CancelReason
            FROM dbo.WN_DayPasses dp WITH (NOLOCK)
            JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = dp.SpaceId
            JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
            JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
            JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = dp.LocationId
            OUTER APPLY (SELECT TOP 1 * FROM dbo.WN_Customers c0 WITH (NOLOCK) WHERE c0.Code = dp.CustomerCode ORDER BY c0.Id) c
            LEFT JOIN dbo.WN_Invoices i WITH (NOLOCK) ON i.Id = dp.InvoiceId
            LEFT JOIN dbo.WN_FeeReceipts fr WITH (NOLOCK) ON fr.Id = dp.ReceiptId
            LEFT JOIN dbo.AccountsCOA ac WITH (NOLOCK) ON ac.Id = dp.PaymentAccountId
            LEFT JOIN dbo.WN_Users cu WITH (NOLOCK) ON cu.Id = dp.CreatedById
            LEFT JOIN dbo.WN_Users xu WITH (NOLOCK) ON xu.Id = dp.CancelledById";

        private static DayPassDto Map(SqlDataReader r)
        {
            var window = Str(r, "Window") ?? "full";
            return new DayPassDto
            {
                Id = Int(r, "Id"), BookingId = Int(r, "BookingId"), PassNumber = Str(r, "PassNumber") ?? DayPassRules.PassNumber(Int(r, "Id")),
                Status = Str(r, "Status") ?? "Active", Date = Dt(r, "PassDate").ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Window = window,
                WindowLabel = DayPassRules.WindowLabel(window), StartsAt = Dt(r, "StartsAt"), EndsAt = Dt(r, "EndsAt"),
                LocationId = Int(r, "LocationId"), LocationName = Str(r, "LocationName") ?? "", SpaceId = Int(r, "SpaceId"), SpaceName = Str(r, "SpaceName") ?? "",
                SpaceType = DayPassRules.SpaceTypeOf(Str(r, "CategoryCode")), WholeSpace = Convert.ToBoolean(r["WholeSpace"]), Seats = Int(r, "Seats"),
                CustomerCode = Str(r, "CustomerCode") ?? "", CustomerName = Str(r, "CustomerName") ?? "", CustomerEmail = Str(r, "CustomerEmail"), CustomerPhone = Str(r, "CustomerPhone"),
                PriceMode = Str(r, "PriceMode") ?? "total", UnitPrice = Dec(r, "UnitPrice"), SubTotal = Dec(r, "SubTotal"), TaxPercent = Dec(r, "TaxPercent"), Tax = Dec(r, "Tax"), Total = Dec(r, "Total"),
                PaymentMethod = Str(r, "PaymentMethod") ?? "cash", BankAccountName = Str(r, "BankAccountName"), PaymentReference = Str(r, "PaymentReference"),
                InvoiceId = IntN(r, "InvoiceId"), InvoiceNumber = Str(r, "InvoiceNumber"), ReceiptId = IntN(r, "ReceiptId"), ReceiptNumber = Str(r, "ReceiptNumber"),
                AccessMode = Str(r, "AccessMode") ?? "reception", CreatedByName = Str(r, "CreatedByName") ?? "", CreatedAt = Dt(r, "CreatedAt"),
                CancelledByName = Str(r, "CancelledByName"), CancelledAt = DtN(r, "CancelledAt"), CancelReason = Str(r, "CancelReason")
            };
        }

        public async Task<DayPassDto?> GetAsync(int id, bool withHistory)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand(SelectPass + " WHERE dp.Id = @id", c);
            cmd.Parameters.AddWithValue("@id", id);
            DayPassDto? dto = null;
            await using (var r = await cmd.ExecuteReaderAsync())
                if (await r.ReadAsync()) dto = Map(r);
            if (dto != null && withHistory)
            {
                dto.History = new List<DayPassHistoryEntry>();
                await using var h = new SqlCommand("SELECT At, ISNULL(ByName, N'System') AS ByName, Action, Note FROM dbo.WN_DayPassHistory WITH (NOLOCK) WHERE DayPassId = @id ORDER BY At, Id", c);
                h.Parameters.AddWithValue("@id", id);
                await using var r = await h.ExecuteReaderAsync();
                while (await r.ReadAsync()) dto.History.Add(new DayPassHistoryEntry { At = Dt(r, "At"), ByName = Str(r, "ByName") ?? "", Action = Str(r, "Action") ?? "", Note = Str(r, "Note") });
            }
            return dto;
        }

        public async Task<DayPassPage> ListAsync(DayPassListFilter f, IReadOnlyCollection<int>? allowedLocationIds, DateTime now)
        {
            var where = new List<string> { "1 = 1" };
            var ps = new List<SqlParameter>();
            if (f.From.HasValue) { where.Add("dp.PassDate >= @from"); ps.Add(new SqlParameter("@from", SqlDbType.Date) { Value = f.From.Value.Date }); }
            if (f.To.HasValue) { where.Add("dp.PassDate <= @to"); ps.Add(new SqlParameter("@to", SqlDbType.Date) { Value = f.To.Value.Date }); }
            if (f.LocationId is > 0) { where.Add("dp.LocationId = @loc"); ps.Add(new SqlParameter("@loc", f.LocationId.Value)); }
            if (allowedLocationIds != null) where.Add("dp.LocationId IN (" + (allowedLocationIds.Count == 0 ? "-1" : Csv(allowedLocationIds)) + ")");
            switch (f.Status)
            {
                case "active": where.Add("dp.Status <> 'Cancelled' AND dp.EndsAt > @now"); break;
                case "used": where.Add("dp.Status <> 'Cancelled' AND dp.EndsAt <= @now"); break;
                case "cancelled": where.Add("dp.Status = 'Cancelled'"); break;
            }
            ps.Add(new SqlParameter("@now", SqlDbType.DateTime2) { Value = now });
            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                where.Add(@"(dp.PassNumber LIKE @q OR dp.CustomerCode LIKE @q OR s.Name LIKE @q OR c.FirstName LIKE @q OR c.LastName LIKE @q OR c.Company LIKE @q OR c.Email LIKE @q OR c.PhoneNumber LIKE @q OR i.InvoiceNumber LIKE @q OR fr.Code LIKE @q)");
                ps.Add(new SqlParameter("@q", "%" + f.Search!.Trim() + "%"));
            }
            var whereSql = " WHERE " + string.Join(" AND ", where);
            var page = new DayPassPage { Page = f.Page, PageSize = f.PageSize };
            await using var c = await OpenAsync();
            await using (var cmd = new SqlCommand(SelectPass + whereSql + " ORDER BY dp.PassDate DESC, dp.Id DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", c))
            {
                foreach (var p in ps) cmd.Parameters.Add(Clone(p));
                cmd.Parameters.AddWithValue("@skip", (f.Page - 1) * f.PageSize);
                cmd.Parameters.AddWithValue("@take", f.PageSize);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) page.Items.Add(Map(r));
            }
            // Totals: what the filter holds; cancelled passes are counted but their money is not (it was given back).
            await using (var cmd = new SqlCommand(@"SELECT COUNT(*) AS N, ISNULL(SUM(CASE WHEN dp.Status = 'Cancelled' THEN 0 ELSE dp.SubTotal END), 0) AS Sub,
                    ISNULL(SUM(CASE WHEN dp.Status = 'Cancelled' THEN 0 ELSE dp.Tax END), 0) AS Tax, ISNULL(SUM(CASE WHEN dp.Status = 'Cancelled' THEN 0 ELSE dp.Total END), 0) AS Tot
                FROM dbo.WN_DayPasses dp WITH (NOLOCK)
                JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = dp.SpaceId
                OUTER APPLY (SELECT TOP 1 * FROM dbo.WN_Customers c0 WITH (NOLOCK) WHERE c0.Code = dp.CustomerCode ORDER BY c0.Id) c
                LEFT JOIN dbo.WN_Invoices i WITH (NOLOCK) ON i.Id = dp.InvoiceId
                LEFT JOIN dbo.WN_FeeReceipts fr WITH (NOLOCK) ON fr.Id = dp.ReceiptId" + whereSql, c))
            {
                foreach (var p in ps) cmd.Parameters.Add(Clone(p));
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    page.Total = Int(r, "N");
                    page.Totals = new DayPassTotals { Count = page.Total, SubTotal = Dec(r, "Sub"), Tax = Dec(r, "Tax"), Total = Dec(r, "Tot") };
                }
            }
            return page;
        }

        private static SqlParameter Clone(SqlParameter p) => new(p.ParameterName, p.SqlDbType) { Value = p.Value };

        // ---- cancel ------------------------------------------------------------------------------------------------------

        public async Task CancelAsync(int id, string reason, int actorId, string actorName, DateTime today)
        {
            await using var c = await OpenAsync();
            await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
            try
            {
                await using (var set = Cmd("SET XACT_ABORT ON; EXEC sys.sp_set_session_context N'AppUserId', @u; EXEC sys.sp_set_session_context N'SourceApp', N'DayPass';", c, tx))
                { set.Parameters.AddWithValue("@u", actorId); await set.ExecuteNonQueryAsync(); }

                int bookingId; int? invoiceId, receiptId; string status; DateTime passDate;
                await using (var q = Cmd("SELECT BookingId, InvoiceId, ReceiptId, Status, PassDate FROM dbo.WN_DayPasses WITH (UPDLOCK, HOLDLOCK) WHERE Id = @i", c, tx))
                {
                    q.Parameters.AddWithValue("@i", id);
                    await using var r = await q.ExecuteReaderAsync();
                    if (!await r.ReadAsync()) throw new DayPassException(404, "Day pass not found.");
                    bookingId = Int(r, "BookingId"); invoiceId = IntN(r, "InvoiceId"); receiptId = IntN(r, "ReceiptId"); status = Str(r, "Status") ?? ""; passDate = Dt(r, "PassDate");
                }
                if (status == DayPassRules.Cancelled) throw new DayPassException(400, "This day pass is already cancelled.");
                if (!DayPassRules.CanCancel(false, passDate, today)) throw new DayPassException(400, "A day pass can be cancelled the same day or before, not after its day.");

                if (receiptId != null)
                {
                    await using var v = Proc("dbo.WN_FeeReceipts_Void", c, tx);
                    v.Parameters.AddWithValue("@ReceiptId", receiptId.Value);
                    v.Parameters.AddWithValue("@VoidReason", "Day pass cancelled: " + reason);
                    v.Parameters.AddWithValue("@UpdatedById", actorId);
                    await v.ExecuteNonQueryAsync();
                }
                if (invoiceId != null)
                {
                    await using var v = Proc("dbo.WN_Invoices_CancelById", c, tx);
                    v.Parameters.AddWithValue("@Id", invoiceId.Value);
                    v.Parameters.AddWithValue("@UpdatedBy", actorId);
                    await v.ExecuteNonQueryAsync();
                }
                await using (var up = Cmd(@"UPDATE dbo.WN_Bookings SET BookingStatusId = 86, CancelReason = @r, UpdatedOn = SYSDATETIME(), UpdatedById = @u WHERE Id = @b;
                    UPDATE dbo.WN_DayPasses SET Status = 'Cancelled', CancelledById = @u, CancelledAt = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATETIME2(0)), CancelReason = @r WHERE Id = @i;", c, tx))
                {
                    up.Parameters.AddWithValue("@r", reason); up.Parameters.AddWithValue("@u", actorId); up.Parameters.AddWithValue("@b", bookingId); up.Parameters.AddWithValue("@i", id);
                    await up.ExecuteNonQueryAsync();
                }
                await HistoryAsync(c, tx, id, "Cancelled", reason, actorId, actorName);
                await tx.CommitAsync();
            }
            catch (SqlException ex) when (ex.Class is 16)
            {
                try { await tx.RollbackAsync(); } catch { }
                throw new DayPassException(400, ex.Message);
            }
            catch
            {
                try { await tx.RollbackAsync(); } catch { }
                throw;
            }
        }

        // ---- booking / access ----------------------------------------------------------------------------------------------

        public async Task<DayPassBookingInfo?> GetByBookingAsync(int bookingId)
        {
            if (!await IsInstalledAsync()) return null;
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("SELECT Id, AccessMode, Seats, Status FROM dbo.WN_DayPasses WITH (NOLOCK) WHERE BookingId = @b", c);
            cmd.Parameters.AddWithValue("@b", bookingId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? new DayPassBookingInfo { DayPassId = Int(r, "Id"), AccessMode = Str(r, "AccessMode") ?? "reception", Seats = Int(r, "Seats"), Status = Str(r, "Status") ?? "Active" } : null;
        }

        public async Task<int?> GetBookingDetailIdAsync(int bookingId)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("SELECT TOP 1 d.Id FROM dbo.WN_BookingDetails d WITH (NOLOCK) JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.IdGUID = d.BookingGuid WHERE b.Id = @b ORDER BY d.Id", c);
            cmd.Parameters.AddWithValue("@b", bookingId);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v is DBNull ? null : Convert.ToInt32(v);
        }

        public async Task<List<DayPassAccessDue>> GetAccessDueAsync(DateTime now)
        {
            var list = new List<DayPassAccessDue>();
            if (!await IsInstalledAsync()) return list;
            await using var c = await OpenAsync();
            // Ended or cancelled, access not yet removed; passes older than 3 days are left alone (the machines' own valid-until has passed long ago).
            await using var cmd = new SqlCommand(@"
                SELECT TOP 100 dp.Id, dp.BookingId, dp.PassNumber, CASE WHEN dp.Status = 'Cancelled' THEN 1 ELSE 0 END AS Cancelled,
                       (SELECT TOP 1 d.Id FROM dbo.WN_BookingDetails d WITH (NOLOCK) JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.IdGUID = d.BookingGuid WHERE b.Id = dp.BookingId ORDER BY d.Id) AS DetailId
                FROM dbo.WN_DayPasses dp WITH (NOLOCK)
                WHERE dp.AccessRemovedAt IS NULL AND (dp.Status = 'Cancelled' OR dp.EndsAt <= @now)
                  AND dp.EndsAt > DATEADD(DAY, -3, @now)
                ORDER BY dp.Id", c);
            cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new DayPassAccessDue { DayPassId = Int(r, "Id"), BookingId = Int(r, "BookingId"), PassNumber = Str(r, "PassNumber") ?? "", Cancelled = Int(r, "Cancelled") == 1, BookingDetailId = IntN(r, "DetailId") });
            return list;
        }

        public async Task<List<int>> GetAttendantPersonIdsAsync(int bookingDetailId)
        {
            var ids = new List<int>();
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("SELECT DISTINCT PersonId FROM dbo.WN_BookingAttendants WITH (NOLOCK) WHERE BookingDetailId = @d", c);
            cmd.Parameters.AddWithValue("@d", bookingDetailId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) ids.Add(Convert.ToInt32(r[0]));
            return ids;
        }

        public async Task<string?> GetPersonNameAsync(int personId)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("SELECT TOP 1 Name FROM dbo.WN_Persons WITH (NOLOCK) WHERE PersonId = @p", c);
            cmd.Parameters.AddWithValue("@p", personId);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v is DBNull ? null : Convert.ToString(v);
        }

        public async Task MarkAccessRemovedAsync(int dayPassId)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("UPDATE dbo.WN_DayPasses SET AccessRemovedAt = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATETIME2(0)) WHERE Id = @i AND AccessRemovedAt IS NULL", c);
            cmd.Parameters.AddWithValue("@i", dayPassId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
