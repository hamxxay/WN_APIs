
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

        // ── User ──────────────────────────────────────────────────────────────

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
                var pub = row.TryGetValue("PublicId", out var eg) ? eg?.ToString() : null;
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
                        row.TryGetValue("PublicId", out var ng) ? ng?.ToString() : null);
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
            int? createdById = null)
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

            if (await r.ReadAsync())
                return ToDict(r);

            return new Dictionary<string, object?>();
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
            await using var cmd = SP("dbo.WN_Quotations_ConvertToBooking", c);

            cmd.Parameters.AddWithValue("@QuotationId", quotationId);
            cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();

            if (await r.ReadAsync())
                return ToDict(r);

            return new Dictionary<string, object?>();
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
                        row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
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
            await using var cmd = SP("dbo.WN_Users_GetHistory", c);
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
                        row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
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

        // ── Space ─────────────────────────────────────────────────────────────

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
            // No WN_Spaces_GetById SP — fetch from list and filter
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

        public async Task<(int? Id, string? PublicId)> InsertSpaceAsync(string name, int locationId, int spaceTypeId, string? code, string? description, int? floorId, string? imageUrl, int capacity, int? createdById)
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
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = ToDict(r);
                return (row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null,
                        row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
            }
            return (null, null);
        }

        public async Task UpdateSpaceAsync(int id, string? name, int? locationId, int? spaceTypeId, string? code, string? description, int? floorId, string? imageUrl, int? capacity, int? updatedById)
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
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteSpaceAsync(int id)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Spaces_Delete", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // ── Booking ───────────────────────────────────────────────────────────

        public async Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetBookingsAsync(int page, int limit, string? search)
        {
            await using var c = await Open();
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
            string? customerCnic = null, string? customerAddress = null, int? customerCityId = null, string? customerNotes = null, decimal discountPercentage = 0)
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
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) return ToDict(r);
            return new Dictionary<string, object?>();
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

        // ── Payment ───────────────────────────────────────────────────────────

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

        // ── Membership ────────────────────────────────────────────────────────

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

        // ── PricingPlan ───────────────────────────────────────────────────────

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

        // ── PlanFeature ───────────────────────────────────────────────────────

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

        // ── Location ──────────────────────────────────────────────────────────

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

        // ── Branch / Company / City ───────────────────────────────────────────

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

        // ── Floor ─────────────────────────────────────────────────────────────

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

        // ── SpaceType ─────────────────────────────────────────────────────────

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

        // ── SpaceConfig ───────────────────────────────────────────────────────

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
            cmd.Parameters.AddWithValue("@PricePerHour", req.PricePerHour);
            cmd.Parameters.AddWithValue("@PricePerDay", req.PricePerDay);
            cmd.Parameters.AddWithValue("@PricePerMonth", req.PricePerMonth);
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
            cmd.Parameters.AddWithValue("@PricePerHour", req.PricePerHour);
            cmd.Parameters.AddWithValue("@PricePerDay", req.PricePerDay);
            cmd.Parameters.AddWithValue("@PricePerMonth", req.PricePerMonth);
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

        public async Task UpdateSpaceConfigAsync(string category, string? updatedBy, int? totalSpaces, string? defaultCapacities, string? openingTime, string? closingTime, decimal? securityDeposit, decimal? pricePerHour, decimal? pricePerDay, decimal? pricePerMonth)
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
            cmd.Parameters.AddWithValue("@PricePerHour", (object?)pricePerHour ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PricePerDay", (object?)pricePerDay ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PricePerMonth", (object?)pricePerMonth ?? DBNull.Value);
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

            if (locationGuid.HasValue && spaceTypeGuid.HasValue && configTotalSpaces > 0)
            {
                await using var fix = new Microsoft.Data.SqlClient.SqlCommand(
                    "UPDATE s SET " +
                    "    LocationIdInt  = COALESCE(s.LocationIdInt, l.Id)," +
                    "    SpaceTypeIdInt = COALESCE(s.SpaceTypeIdInt, st.Id)," +
                    "    IsActive      = COALESCE(s.IsActive, 1)," +
                    "    Status        = COALESCE(s.Status, 1) " +
                    "FROM dbo.WN_Spaces s " +
                    "LEFT JOIN dbo.WN_Locations l ON l.IdGUID = s.LocationId " +
                    "LEFT JOIN dbo.WN_SpaceTypes st ON st.IdGUID = s.SpaceTypeId " +
                    "WHERE s.LocationId = @LocationGuid " +
                    "  AND s.SpaceTypeId = @SpaceTypeGuid " +
                    "  AND TRY_CAST(s.Code AS INT) BETWEEN @MinCode AND (@MinCode + @TotalSpaces - 1);", c);
                fix.Parameters.AddWithValue("@LocationGuid", locationGuid.Value);
                fix.Parameters.AddWithValue("@SpaceTypeGuid", spaceTypeGuid.Value);
                fix.Parameters.AddWithValue("@MinCode", configMinCode);
                fix.Parameters.AddWithValue("@TotalSpaces", configTotalSpaces);
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
                "SELECT s.Id, s.Code, CAST(s.IdGUID AS NVARCHAR(50)) AS IdGuidStr, CAST(s.PublicId AS NVARCHAR(50)) AS PublicIdStr " +
                "FROM dbo.WN_Spaces s " +
                "LEFT JOIN dbo.WN_Locations l ON l.Id = s.LocationIdInt OR l.IdGUID = s.LocationId " +
                "LEFT JOIN dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeIdInt OR st.IdGUID = s.SpaceTypeId " +
                "WHERE (s.LocationIdInt = @L OR l.Id = @L) AND (s.SpaceTypeIdInt = @ST OR st.Id = @ST) AND s.IsActive = 1", c))
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

        // ── Amenity ───────────────────────────────────────────────────────────

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

        // ── Gallery ───────────────────────────────────────────────────────────

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

        // ── Contact ───────────────────────────────────────────────────────────

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

        // ── Dashboard ─────────────────────────────────────────────────────────

        public async Task<IEnumerable<IEnumerable<IDictionary<string, object?>>>> GetDashboardSummaryAsync()
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_Dashboard_GetSummary", c);
            await using var r = await cmd.ExecuteReaderAsync();
            var results = new List<List<IDictionary<string, object?>>>();
            do { results.Add(await ReadAll(r)); } while (await r.NextResultAsync());
            return results;
        }

        // ── AccountCOA ────────────────────────────────────────────────────────

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

        // ── AmountFields ──────────────────────────────────────────────────────

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

        // ── Customer ──────────────────────────────────────────────────────────

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

        public async Task<IDictionary<string, object?>> CreateCustomerAsync(string firstName, string? lastName, string email, string? phone, string? cnic, string? address, int? cityId, string? notes, string? createdBy)
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
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) return ToDict(r);
            return new Dictionary<string, object?>();
        }

        public async Task UpdateCustomerAsync(string guid, string? firstName, string? lastName, string? email, string? phone, string? cnic, string? address, int? cityId, string? notes, bool? isActive)
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

        public async Task<IEnumerable<IDictionary<string, object?>>> GetBookingDetailsAsync(string bookingIdentifier, string? userEmail)
        {
            await using var c = await Open();
            await using var cmd = SP("dbo.WN_BookingDetails_GetByBooking", c);
            cmd.Parameters.AddWithValue("@BookingIdentifier", bookingIdentifier);
            cmd.Parameters.AddWithValue("@UserEmail", (object?)userEmail ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAll(r);
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
    }
}