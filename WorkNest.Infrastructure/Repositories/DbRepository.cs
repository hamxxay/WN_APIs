using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Data;
using WorkNest.Application.Interfaces;
using WorkNest.Infrastructure.Database;

namespace WorkNest.Infrastructure.Repositories
{
    public class DbRepository : IDbRepository
    {
        private readonly string _connectionString;

        public DbRepository(IOptions<DatabaseSettings> settings)
        {
            _connectionString = settings.Value.ToConnectionString();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static object? Normalize(object? v) => v is DBNull ? null : v;

        private static IDictionary<string, object?> RowToDictionary(SqlDataReader r)
        {
            var d = new Dictionary<string, object?>(r.FieldCount);
            for (int i = 0; i < r.FieldCount; i++)
                d[r.GetName(i)] = Normalize(r.GetValue(i));
            return d;
        }

        private static async Task<List<IDictionary<string, object?>>> ReadAllRowsAsync(SqlDataReader r)
        {
            var list = new List<IDictionary<string, object?>>();
            while (await r.ReadAsync()) list.Add(RowToDictionary(r));
            return list;
        }

        private SqlCommand SP(string name, SqlConnection conn, SqlTransaction? tx = null)
        {
            var cmd = tx is null ? new SqlCommand(name, conn) : new SqlCommand(name, conn, tx);
            cmd.CommandType = CommandType.StoredProcedure;
            return cmd;
        }

        // ── User ──────────────────────────────────────────────────────────────

        public async Task<(int? NumericId, string? Guid)> SyncUserAsync(
            string email, string firstName, string lastName, string? phone)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await using (var cmd = SP("dbo.WN_Users_GetByEmail", conn))
            {
                cmd.Parameters.AddWithValue("@Email", email);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    var row = RowToDictionary(r);
                    var existId = row.TryGetValue("Id", out var eid) ? Convert.ToInt32(eid) : (int?)null;
                    var existGuid = row.TryGetValue("IdGUID", out var eg) ? eg?.ToString() : null;
                    await r.CloseAsync();

                    await using var upd = SP("dbo.WN_Users_Update", conn);
                    upd.Parameters.AddWithValue("@FirstName", firstName);
                    upd.Parameters.AddWithValue("@LastName", lastName);
                    upd.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@Id", existId);
                    await upd.ExecuteNonQueryAsync();
                    return (existId, existGuid);
                }
            }

            await using var ins = SP("dbo.WN_Users_Insert", conn);
            ins.Parameters.AddWithValue("@FirstName", firstName);
            ins.Parameters.AddWithValue("@LastName", lastName);
            ins.Parameters.AddWithValue("@Email", email);
            ins.Parameters.AddWithValue("@Username", email);
            ins.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
            await using var ir = await ins.ExecuteReaderAsync();
            if (await ir.ReadAsync())
            {
                var row = RowToDictionary(ir);
                return (
                    row.TryGetValue("Id", out var nid) ? Convert.ToInt32(nid) : (int?)null,
                    row.TryGetValue("IdGUID", out var ng) ? ng?.ToString() : null
                );
            }
            return (null, null);
        }

        public async Task<(int? NumericId, string? Guid)> GetUserIdByEmailAsync(string email)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Users_GetByEmail", conn);
            cmd.Parameters.AddWithValue("@Email", email);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                return (
                    row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null,
                    row.TryGetValue("IdGUID", out var g) ? g?.ToString() : null
                );
            }
            return (null, null);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllUsersAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Users_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IDictionary<string, object?>?> GetUserByIdAsync(string id)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Users_GetByEmail", conn);
            // SP accepts email; for GUID/numeric lookups use GetByEmail fallback via email resolution
            cmd.Parameters.AddWithValue("@Email", id);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? RowToDictionary(r) : null;
        }

        public async Task<IDictionary<string, object?>?> GetUserByEmailAsync(string email)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Users_GetByEmail", conn);
            cmd.Parameters.AddWithValue("@Email", email);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? RowToDictionary(r) : null;
        }

        public async Task UpdateUserAsync(string guid, string name, string? phone)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Users_Update", conn);
            cmd.Parameters.AddWithValue("@FirstName", name);
            cmd.Parameters.AddWithValue("@LastName", "");
            cmd.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteUserAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Users_Update", conn);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            cmd.Parameters.AddWithValue("@IsActive", 0);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetUserStatusAsync(string guid, int status)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Users_Update", conn);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            cmd.Parameters.AddWithValue("@Status", status);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetUserRoleAsync(string guid, int roleId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Users_Update", conn);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            cmd.Parameters.AddWithValue("@RoleId", roleId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetBookingsByUserIdAsync(int numericUserId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Bookings_GetListByUserId", conn);
            cmd.Parameters.AddWithValue("@UserId", numericUserId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetPaymentsByUserIdAsync(int numericUserId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Payments_GetMyList", conn);
            cmd.Parameters.AddWithValue("@UserId", numericUserId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        // ── Space ─────────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllSpacesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Spaces_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> InsertSpaceAsync(string name, int locationId, int spaceTypeId, string? code,
            string? description, int? floorId, double? pricePerDay, double? pricePerHour,
            string? imageUrl, string? amenities)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Spaces_Insert", conn);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@LocationId", locationId);
            cmd.Parameters.AddWithValue("@SpaceTypeId", spaceTypeId);
            cmd.Parameters.AddWithValue("@Code", (object?)code ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FloorId", (object?)floorId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PricePerDay", (object?)pricePerDay ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PricePerHour", (object?)pricePerHour ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)imageUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Amenities", (object?)amenities ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        public async Task UpdateSpaceAsync(int spaceId, string? name, int? locationId, int? spaceTypeId,
            string? code, string? description, int? floorId, double? pricePerDay,
            double? pricePerHour, string? imageUrl, string? amenities)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Spaces_Update", conn);
            cmd.Parameters.AddWithValue("@SpaceId", spaceId);
            cmd.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SpaceTypeId", (object?)spaceTypeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Code", (object?)code ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FloorId", (object?)floorId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PricePerDay", (object?)pricePerDay ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PricePerHour", (object?)pricePerHour ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)imageUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Amenities", (object?)amenities ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteSpaceAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Spaces_Delete", conn);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int?> GetSpaceNumericIdByGuidAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Spaces_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                if (row.TryGetValue("IdGUID", out var g) && g?.ToString() == guid)
                    return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        public async Task<IDictionary<string, object?>?> GetSpaceSummaryAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Spaces_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                if (row.TryGetValue("IdGUID", out var g) && g?.ToString() == guid)
                    return row;
            }
            return null;
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetSpaceReservationsAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Bookings_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            var all = await ReadAllRowsAsync(r);
            return all.Where(row => row.TryGetValue("SpaceGuid", out var sg) && sg?.ToString() == guid).ToList();
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesAsync(
            string spaceType, string start, string end)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_GetAvailableSpaces", conn);
            cmd.Parameters.AddWithValue("@SpaceType", spaceType);
            cmd.Parameters.AddWithValue("@Start", start);
            cmd.Parameters.AddWithValue("@End", end);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesByTypeAsync(
            string spaceType, string? start, string? end)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_GetAvailableSpacesByType", conn);
            cmd.Parameters.AddWithValue("@SpaceType", spaceType);
            cmd.Parameters.AddWithValue("@Start", (object?)start ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@End", (object?)end ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailabilityCountsAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_GetAvailabilityCounts", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesV2Async(
            string spaceCategory, string start, string end, int? capacity)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Booking_GetAvailableSpaces", conn);
            cmd.Parameters.AddWithValue("@SpaceCategory", spaceCategory);
            cmd.Parameters.AddWithValue("@Start", start);
            cmd.Parameters.AddWithValue("@End", end);
            cmd.Parameters.AddWithValue("@Capacity", (object?)capacity ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesForReassignmentAsync(
            string spaceType, string start, string end, int? excludeBookingId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_GetAvailableSpacesForReassignment", conn);
            cmd.Parameters.AddWithValue("@SpaceType", spaceType);
            cmd.Parameters.AddWithValue("@Start", start);
            cmd.Parameters.AddWithValue("@End", end);
            cmd.Parameters.AddWithValue("@ExcludeBookingId", (object?)excludeBookingId ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        // ── Booking ───────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllBookingsAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Bookings_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetMyBookingsAsync(int userId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Bookings_GetListByUserId", conn);
            cmd.Parameters.AddWithValue("@UserId", userId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IDictionary<string, object?>?> GetBookingByIdAsync(int userId, int bookingId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Bookings_GetListByUserId", conn);
            cmd.Parameters.AddWithValue("@UserId", userId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                if (row.TryGetValue("Id", out var id) && Convert.ToInt32(id) == bookingId)
                    return row;
            }
            return null;
        }

        public async Task<IDictionary<string, object?>> CreateBookingAsync(
            int userId, object spaceId, string start, string end, string notes,
            double amount, string? paymentMethod, string? paymentRef)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();
            try
            {
                await using var bCmd = SP("dbo.WN_Bookings_Insert", conn, tx);
                bCmd.Parameters.AddWithValue("@UserId", userId);
                bCmd.Parameters.AddWithValue("@SpaceId", spaceId);
                bCmd.Parameters.AddWithValue("@StartDateTime", start);
                bCmd.Parameters.AddWithValue("@EndDateTime", end);
                bCmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
                bCmd.Parameters.AddWithValue("@Amount", amount);

                IDictionary<string, object?> bookingRow;
                await using (var br = await bCmd.ExecuteReaderAsync())
                {
                    if (!await br.ReadAsync()) throw new InvalidOperationException("Booking insert returned no row.");
                    bookingRow = RowToDictionary(br);
                }

                var bookingId = Convert.ToInt32(bookingRow["Id"]);

                await using var pCmd = SP("dbo.WN_Payments_Insert", conn, tx);
                pCmd.Parameters.AddWithValue("@UserId", userId);
                pCmd.Parameters.AddWithValue("@BookingId", bookingId);
                pCmd.Parameters.AddWithValue("@Amount", amount);
                pCmd.Parameters.AddWithValue("@PaymentMethod", (object?)paymentMethod ?? DBNull.Value);
                pCmd.Parameters.AddWithValue("@TransactionRef", (object?)paymentRef ?? DBNull.Value);
                await pCmd.ExecuteNonQueryAsync();

                await tx.CommitAsync();
                return bookingRow;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<IDictionary<string, object?>> CreateBookingWithAutoAssignmentAsync(
            string userEmail, string spaceType, string start, string end, string notes,
            double amount, string? paymentMethod, string? paymentRef)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_CreateBookingWithAutoAssignment", conn);
            cmd.Parameters.AddWithValue("@Email", userEmail);
            cmd.Parameters.AddWithValue("@SpaceType", spaceType);
            cmd.Parameters.AddWithValue("@StartDateTime", start);
            cmd.Parameters.AddWithValue("@EndDateTime", end);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TotalAmount", amount);
            cmd.Parameters.AddWithValue("@PaymentMethod", (object?)paymentMethod ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PaymentRef", (object?)paymentRef ?? DBNull.Value);

            var bookingIdParam = new SqlParameter("@BookingId", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var spaceIdParam = new SqlParameter("@AssignedSpaceId", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(bookingIdParam);
            cmd.Parameters.Add(spaceIdParam);

            await cmd.ExecuteNonQueryAsync();

            return new Dictionary<string, object?>
            {
                ["BookingId"] = Normalize(bookingIdParam.Value),
                ["AssignedSpaceId"] = Normalize(spaceIdParam.Value)
            };
        }

        public async Task<IDictionary<string, object?>> CreateSmartBookingAsync(
            string userEmail, string spaceCategory, string start, string end, string notes,
            double amount, string? paymentMethod, string? paymentRef, int? capacity)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Booking_Create", conn);
            cmd.Parameters.AddWithValue("@Email", userEmail);
            cmd.Parameters.AddWithValue("@SpaceCategory", spaceCategory);
            cmd.Parameters.AddWithValue("@StartDT", start);
            cmd.Parameters.AddWithValue("@EndDT", end);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TotalAmount", amount);
            cmd.Parameters.AddWithValue("@PaymentMethod", (object?)paymentMethod ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PaymentRef", (object?)paymentRef ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Capacity", (object?)capacity ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) return RowToDictionary(r);
            return new Dictionary<string, object?>();
        }

        public async Task CancelBookingAsync(int userId, int bookingId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Bookings_Cancel", conn);
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            cmd.Parameters.AddWithValue("@UserId", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateBookingStatusAsync(string guid, int statusVal)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Bookings_UpdateStatus", conn);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            cmd.Parameters.AddWithValue("@Status", statusVal);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateBookingDatesAsync(string guid, string start, string end)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Bookings_UpdateDates", conn);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            cmd.Parameters.AddWithValue("@StartDateTime", start);
            cmd.Parameters.AddWithValue("@EndDateTime", end);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IDictionary<string, object?>> ReassignBookingAsync(int bookingId, int newSpaceId, string adminEmail)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_ReassignBooking", conn);
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            cmd.Parameters.AddWithValue("@NewSpaceId", newSpaceId);
            cmd.Parameters.AddWithValue("@AdminEmail", adminEmail);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) return RowToDictionary(r);
            return new Dictionary<string, object?>();
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetBookingCalendarAsync(int spaceId, int year, int month)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_GetBookingCalendar", conn);
            cmd.Parameters.AddWithValue("@SpaceId", spaceId);
            cmd.Parameters.AddWithValue("@Year", year);
            cmd.Parameters.AddWithValue("@Month", month);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> GetBookingNumericIdByGuidAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Bookings_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                if (row.TryGetValue("IdGUID", out var g) && g?.ToString() == guid)
                    return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        // ── Payment ───────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllPaymentsAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Payments_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetMyPaymentsAsync(int userId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Payments_GetMyList", conn);
            cmd.Parameters.AddWithValue("@UserId", userId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IDictionary<string, object?>> CreatePaymentAsync(int userId, int bookingId,
            double amount, string method, string transactionRef)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Payments_Insert", conn);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            cmd.Parameters.AddWithValue("@Amount", amount);
            cmd.Parameters.AddWithValue("@PaymentMethod", method);
            cmd.Parameters.AddWithValue("@TransactionRef", transactionRef);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) return RowToDictionary(r);
            return new Dictionary<string, object?>();
        }

        public async Task UpdatePaymentStatusByRefAsync(string transactionRef, string status)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Payments_UpdateStatusByRef", conn);
            cmd.Parameters.AddWithValue("@TransactionRef", transactionRef);
            cmd.Parameters.AddWithValue("@Status", status);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdatePaymentStatusByGuidAsync(string guid, string status)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Payments_UpdateStatusByRef", conn);
            cmd.Parameters.AddWithValue("@TransactionRef", guid);
            cmd.Parameters.AddWithValue("@Status", status);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeletePaymentAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Payments_UpdateStatusByRef", conn);
            cmd.Parameters.AddWithValue("@TransactionRef", guid);
            cmd.Parameters.AddWithValue("@Status", "Deleted");
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetPaymentsByUserGuidAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            // Resolve numeric userId from GUID then use WN_Payments_GetMyList
            var (userId, _) = await GetUserIdByEmailAsync(guid); // guid passed as fallback; caller should resolve
            await using var cmd = SP("dbo.WN_Payments_GetMyList", conn);
            cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        // ── Location ──────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllLocationsAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Locations_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreateLocationAsync(string name, string? address, int? cityId,
            string? openingTime, string? closingTime, bool isActive, int? branchId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Locations_Insert", conn);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CityId", (object?)cityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OpeningTime", (object?)openingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ClosingTime", (object?)closingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", isActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@BranchId", (object?)branchId ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        public async Task UpdateLocationAsync(string guid, string name, string? address, int? cityId,
            string? openingTime, string? closingTime, bool isActive, int? branchId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Locations_Update", conn);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CityId", (object?)cityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OpeningTime", (object?)openingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ClosingTime", (object?)closingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", isActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@BranchId", (object?)branchId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteLocationAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Locations_Delete", conn);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        // ── SpaceType ─────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllSpaceTypesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_SpaceTypes_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreateSpaceTypeAsync(string name, int capacity, bool hourlyAllowed)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_SpaceTypes_Insert", conn);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Capacity", capacity);
            cmd.Parameters.AddWithValue("@HourlyAllowed", hourlyAllowed ? 1 : 0);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        public async Task UpdateSpaceTypeAsync(string guid, string name, int capacity, bool hourlyAllowed)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_SpaceTypes_Update", conn);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Capacity", capacity);
            cmd.Parameters.AddWithValue("@HourlyAllowed", hourlyAllowed ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteSpaceTypeAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_SpaceTypes_Delete", conn);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        // ── PricingPlan ───────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllPricingPlansAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_PricingPlans_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreatePricingPlanAsync(string name, double price, string? billingCycle,
            int includesHours, bool isActive)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_PricingPlans_Insert", conn);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Price", price);
            cmd.Parameters.AddWithValue("@BillingCycle", (object?)billingCycle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IncludesHours", includesHours);
            cmd.Parameters.AddWithValue("@IsActive", isActive ? 1 : 0);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        public async Task UpdatePricingPlanAsync(int id, string name, double price, string? billingCycle,
            int includesHours, bool isActive)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_PricingPlans_Update", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Price", price);
            cmd.Parameters.AddWithValue("@BillingCycle", (object?)billingCycle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IncludesHours", includesHours);
            cmd.Parameters.AddWithValue("@IsActive", isActive ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeletePricingPlanAsync(int id)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_PricingPlans_Delete", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetMembershipsByPlanIdAsync(int planId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Memberships_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            var all = await ReadAllRowsAsync(r);
            return all.Where(row => row.TryGetValue("PlanId", out var pid) && Convert.ToInt32(pid) == planId).ToList();
        }

        // ── Membership ────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllMembershipsAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Memberships_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreateMembershipAsync(string? userId, int planId, string startDate)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Memberships_Insert", conn);
            cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PlanId", planId);
            cmd.Parameters.AddWithValue("@StartDate", startDate);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        public async Task UpdateMembershipStatusAsync(int id, string status)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Memberships_UpdateStatus", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Status", status);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteMembershipAsync(int id)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Memberships_Delete", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetPaymentsByMembershipIdAsync(int membershipId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Payments_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            var all = await ReadAllRowsAsync(r);
            return all.Where(row => row.TryGetValue("MembershipId", out var mid) && Convert.ToInt32(mid) == membershipId).ToList();
        }

        // ── Contact ───────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllContactsAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Contacts_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> BookTourAsync(string name, string email, string message, string phone, int? userId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_BookTour_Insert", conn);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@Message", message);
            cmd.Parameters.AddWithValue("@PhoneNumber", phone);
            cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        public async Task UpdateContactStatusAsync(string guid, string status)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Contacts_UpdateStatus", conn);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            cmd.Parameters.AddWithValue("@Status", status);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteContactAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Contacts_Delete", conn);
            cmd.Parameters.AddWithValue("@IdGUID", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        // ── Gallery ───────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllGalleryImagesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_GalleryImages_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreateGalleryImageAsync(string? title, string imageUrl, int sortOrder, bool isActive)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_GalleryImages_Insert", conn);
            cmd.Parameters.AddWithValue("@Title", (object?)title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", imageUrl);
            cmd.Parameters.AddWithValue("@SortOrder", sortOrder);
            cmd.Parameters.AddWithValue("@IsActive", isActive ? 1 : 0);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        public async Task UpdateGalleryImageAsync(string id, string? title, string imageUrl, int sortOrder, bool isActive)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_GalleryImages_Update", conn);
            cmd.Parameters.AddWithValue("@IdGUID", id);
            cmd.Parameters.AddWithValue("@Title", (object?)title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", imageUrl);
            cmd.Parameters.AddWithValue("@SortOrder", sortOrder);
            cmd.Parameters.AddWithValue("@IsActive", isActive ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteGalleryImageAsync(string id)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_GalleryImages_Delete", conn);
            cmd.Parameters.AddWithValue("@IdGUID", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // ── Floor ─────────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllFloorsAsync(int? locationId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Floors_GetList", conn);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreateFloorAsync(int locationId, string floorName)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Floors_Insert", conn);
            cmd.Parameters.AddWithValue("@LocationId", locationId);
            cmd.Parameters.AddWithValue("@FloorName", floorName);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        // ── Amenity ───────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllAmenitiesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Amenities_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreateAmenityAsync(string name)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Amenities_Insert", conn);
            cmd.Parameters.AddWithValue("@Name", name);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        // ── SpaceConfig ───────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetSpaceConfigAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_SpaceConfig_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<double> GetSecurityDepositAsync(string category)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_SpaceConfig_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                if (row.TryGetValue("SpaceCategory", out var cat) && cat?.ToString() == category)
                {
                    return row.TryGetValue("SecurityDeposit", out var dep) && dep is not null
                        ? Convert.ToDouble(dep) : 0;
                }
            }
            return 0;
        }

        public async Task UpdateSpaceConfigAsync(string category, int totalSpaces, string? defaultCapacities,
            string? openingTime, string? closingTime, string? adminEmail, double? securityDeposit)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Spaces_UpdateConfig", conn);
            cmd.Parameters.AddWithValue("@SpaceCategory", category);
            cmd.Parameters.AddWithValue("@TotalSpaces", totalSpaces);
            cmd.Parameters.AddWithValue("@DefaultCapacities", (object?)defaultCapacities ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OpeningTime", (object?)openingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ClosingTime", (object?)closingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AdminEmail", (object?)adminEmail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SecurityDeposit", (object?)securityDeposit ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IDictionary<string, object?>> GenerateSpaceInventoryAsync(string spaceCategory,
            int spaceTypeId, int locationId, double pricePerHour, double pricePerDay)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Spaces_GenerateInventory", conn);
            cmd.Parameters.AddWithValue("@SpaceCategory", spaceCategory);
            cmd.Parameters.AddWithValue("@SpaceTypeId", spaceTypeId);
            cmd.Parameters.AddWithValue("@LocationId", locationId);
            cmd.Parameters.AddWithValue("@PricePerHour", pricePerHour);
            cmd.Parameters.AddWithValue("@PricePerDay", pricePerDay);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) return RowToDictionary(r);
            return new Dictionary<string, object?>();
        }

        // ── Branch / Company / City ───────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllBranchesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Branches_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllCompaniesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Companies_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllCitiesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_Cities_GetList", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        // ── PlanFeature ───────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetPlanFeaturesByPlanIdAsync(int planId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_PlanFeatures_GetByPlanId", conn);
            cmd.Parameters.AddWithValue("@PlanId", planId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreatePlanFeatureAsync(int planId, string featureName)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_PlanFeatures_Insert", conn);
            cmd.Parameters.AddWithValue("@PlanId", planId);
            cmd.Parameters.AddWithValue("@FeatureName", featureName);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var row = RowToDictionary(r);
                return row.TryGetValue("Id", out var id) ? Convert.ToInt32(id) : (int?)null;
            }
            return null;
        }

        public async Task UpdatePlanFeatureAsync(int id, string featureName)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_PlanFeatures_Update", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@FeatureName", featureName);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeletePlanFeatureAsync(int id)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = SP("dbo.WN_PlanFeatures_Delete", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

