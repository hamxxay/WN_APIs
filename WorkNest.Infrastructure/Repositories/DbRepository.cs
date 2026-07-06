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

        // ── User ──────────────────────────────────────────────────────────────

        public async Task<(int? NumericId, string? Guid)> SyncUserAsync(
            string email, string firstName, string lastName, string? phone)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Try get existing
            await using (var cmd = new SqlCommand("dbo.WN_Users_GetByEmail", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Email", email);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    var row = RowToDictionary(r);
                    var existId = row.TryGetValue("Id", out var eid) ? Convert.ToInt32(eid) : (int?)null;
                    var existGuid = row.TryGetValue("IdGUID", out var eg) ? eg?.ToString() : null;
                    await r.CloseAsync();

                    // Update
                    await using var upd = new SqlCommand("dbo.WN_Users_Update", conn);
                    upd.CommandType = CommandType.StoredProcedure;
                    upd.Parameters.AddWithValue("@IdGUID", existGuid ?? (object)DBNull.Value);
                    upd.Parameters.AddWithValue("@Name", $"{firstName} {lastName}");
                    upd.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
                    await upd.ExecuteNonQueryAsync();
                    return (existId, existGuid);
                }
            }

            // Insert
            await using var ins = new SqlCommand("dbo.WN_Users_Insert", conn);
            ins.CommandType = CommandType.StoredProcedure;
            ins.Parameters.AddWithValue("@Email", email);
            ins.Parameters.AddWithValue("@Name", $"{firstName} {lastName}");
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
            await using var cmd = new SqlCommand("dbo.WN_Users_GetByEmail", conn);
            cmd.CommandType = CommandType.StoredProcedure;
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
            await using var cmd = new SqlCommand("dbo.WN_Users_GetList", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IDictionary<string, object?>?> GetUserByIdAsync(string id)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = Guid.TryParse(id, out _)
                ? "SELECT * FROM dbo.WN_Users WHERE IdGUID = @Id"
                : int.TryParse(id, out _)
                    ? "SELECT * FROM dbo.WN_Users WHERE Id = @Id"
                    : "SELECT * FROM dbo.WN_Users WHERE Email = @Id";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? RowToDictionary(r) : null;
        }

        public async Task<IDictionary<string, object?>?> GetUserByEmailAsync(string email)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT * FROM dbo.WN_Users WHERE Email = @Email", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Email", email);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? RowToDictionary(r) : null;
        }

        public async Task UpdateUserAsync(string guid, string name, string? phone)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Users SET Name=@Name, PhoneNumber=@Phone WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Phone", (object?)phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteUserAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Users SET IsActive=0 WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetUserStatusAsync(string guid, int status)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Users SET Status=@Status WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetUserRoleAsync(string guid, int roleId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Users SET RoleId=@RoleId WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@RoleId", roleId);
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetBookingsByUserIdAsync(int numericUserId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_Bookings_GetListByUserId", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@UserId", numericUserId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetPaymentsByUserIdAsync(int numericUserId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                SELECT p.* FROM dbo.WN_Payments p
                JOIN dbo.WN_Bookings b ON p.BookingId = b.Id
                JOIN dbo.WN_Users u ON b.UserId = u.Id
                WHERE u.Id = @UserId";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@UserId", numericUserId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        // ── Space ─────────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllSpacesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_Spaces_GetList", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> InsertSpaceAsync(string name, int locationId, int spaceTypeId, string? code,
            string? description, int? floorId, double? pricePerDay, double? pricePerHour,
            string? imageUrl, string? amenities)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                INSERT INTO dbo.WN_Spaces
                    (Name, LocationId, SpaceTypeId, Code, Description, FloorId, PricePerDay, PricePerHour, ImageUrl, Amenities)
                OUTPUT INSERTED.Id
                VALUES (@Name,@LocationId,@SpaceTypeId,@Code,@Description,@FloorId,@PricePerDay,@PricePerHour,@ImageUrl,@Amenities)";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
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
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }

        public async Task UpdateSpaceAsync(int spaceId, string? name, int? locationId, int? spaceTypeId,
            string? code, string? description, int? floorId, double? pricePerDay,
            double? pricePerHour, string? imageUrl, string? amenities)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                UPDATE dbo.WN_Spaces SET
                    Name=COALESCE(@Name,Name),
                    LocationId=COALESCE(@LocationId,LocationId),
                    SpaceTypeId=COALESCE(@SpaceTypeId,SpaceTypeId),
                    Code=COALESCE(@Code,Code),
                    Description=COALESCE(@Description,Description),
                    FloorId=COALESCE(@FloorId,FloorId),
                    PricePerDay=COALESCE(@PricePerDay,PricePerDay),
                    PricePerHour=COALESCE(@PricePerHour,PricePerHour),
                    ImageUrl=COALESCE(@ImageUrl,ImageUrl),
                    Amenities=COALESCE(@Amenities,Amenities)
                WHERE Id=@SpaceId";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
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
            cmd.Parameters.AddWithValue("@SpaceId", spaceId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteSpaceAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Spaces SET Status=0 WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int?> GetSpaceNumericIdByGuidAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT Id FROM dbo.WN_Spaces WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Guid", guid);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }

        public async Task<IDictionary<string, object?>?> GetSpaceSummaryAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                SELECT s.*, l.Name AS LocationName, st.Name AS SpaceTypeName
                FROM dbo.WN_Spaces s
                JOIN dbo.WN_Locations l ON s.LocationId = l.Id
                JOIN dbo.WN_SpaceTypes st ON s.SpaceTypeId = st.Id
                WHERE s.IdGUID = @Guid";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Guid", guid);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? RowToDictionary(r) : null;
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetSpaceReservationsAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                SELECT b.*, u.Name AS UserName, u.Email
                FROM dbo.WN_Bookings b
                JOIN dbo.WN_Users u ON b.UserId = u.Id
                JOIN dbo.WN_Spaces s ON b.SpaceId = s.Id
                WHERE s.IdGUID = @Guid";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Guid", guid);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesAsync(
            string spaceType, string start, string end)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_GetAvailableSpaces", conn);
            cmd.CommandType = CommandType.StoredProcedure;
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
            await using var cmd = new SqlCommand("dbo.WN_GetAvailableSpacesByType", conn);
            cmd.CommandType = CommandType.StoredProcedure;
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
            await using var cmd = new SqlCommand("dbo.WN_GetAvailabilityCounts", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesV2Async(
            string spaceCategory, string start, string end, int? capacity)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_Booking_GetAvailableSpaces", conn);
            cmd.CommandType = CommandType.StoredProcedure;
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
            await using var cmd = new SqlCommand("dbo.WN_GetAvailableSpacesForReassignment", conn);
            cmd.CommandType = CommandType.StoredProcedure;
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
            const string sql = @"
                SELECT b.*, u.Name AS UserName, u.Email,
                       s.Name AS SpaceName, st.Name AS SpaceTypeName
                FROM dbo.WN_Bookings b
                JOIN dbo.WN_Users u ON b.UserId = u.Id
                JOIN dbo.WN_Spaces s ON b.SpaceId = s.Id
                JOIN dbo.WN_SpaceTypes st ON s.SpaceTypeId = st.Id";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetMyBookingsAsync(int userId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                SELECT b.*, u.Name AS UserName, u.Email,
                       s.Name AS SpaceName, st.Name AS SpaceTypeName
                FROM dbo.WN_Bookings b
                JOIN dbo.WN_Users u ON b.UserId = u.Id
                JOIN dbo.WN_Spaces s ON b.SpaceId = s.Id
                JOIN dbo.WN_SpaceTypes st ON s.SpaceTypeId = st.Id
                WHERE u.Id = @UserId";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@UserId", userId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IDictionary<string, object?>?> GetBookingByIdAsync(int userId, int bookingId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                SELECT b.*, s.Name AS SpaceName, st.Name AS SpaceTypeName
                FROM dbo.WN_Bookings b
                JOIN dbo.WN_Spaces s ON b.SpaceId = s.Id
                JOIN dbo.WN_SpaceTypes st ON s.SpaceTypeId = st.Id
                WHERE b.Id = @BookingId AND b.UserId = @UserId";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            cmd.Parameters.AddWithValue("@UserId", userId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? RowToDictionary(r) : null;
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
                await using var bCmd = new SqlCommand("dbo.WN_Bookings_Insert", conn, tx);
                bCmd.CommandType = CommandType.StoredProcedure;
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

                await using var pCmd = new SqlCommand("dbo.WN_Payments_Insert", conn, tx);
                pCmd.CommandType = CommandType.StoredProcedure;
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
            await using var cmd = new SqlCommand("dbo.WN_CreateBookingWithAutoAssignment", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@UserEmail", userEmail);
            cmd.Parameters.AddWithValue("@SpaceType", spaceType);
            cmd.Parameters.AddWithValue("@StartDateTime", start);
            cmd.Parameters.AddWithValue("@EndDateTime", end);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Amount", amount);
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
            await using var cmd = new SqlCommand("dbo.WN_Booking_Create", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@UserEmail", userEmail);
            cmd.Parameters.AddWithValue("@SpaceCategory", spaceCategory);
            cmd.Parameters.AddWithValue("@StartDateTime", start);
            cmd.Parameters.AddWithValue("@EndDateTime", end);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Amount", amount);
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
            await using var cmd = new SqlCommand("dbo.WN_Bookings_Cancel", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateBookingStatusAsync(string guid, int statusVal)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Bookings SET BookingStatus=@Status WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Status", statusVal);
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        
        public async Task UpdateBookingDatesAsync(string guid, string start, string end)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Bookings SET StartDateTime=@Start, EndDateTime=@End WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Start", start);
            cmd.Parameters.AddWithValue("@End", end);
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IDictionary<string, object?>> ReassignBookingAsync(int bookingId, int newSpaceId, string adminEmail)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_Bookings_Reassign", conn);
            cmd.CommandType = CommandType.StoredProcedure;
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
            await using var cmd = new SqlCommand("dbo.WN_Bookings_GetCalendar", conn);
            cmd.CommandType = CommandType.StoredProcedure;
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
            await using var cmd = new SqlCommand("SELECT Id FROM dbo.WN_Bookings WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Guid", guid);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }

        // ── Payment ───────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllPaymentsAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT * FROM dbo.WN_Payments", conn);
            cmd.CommandType = CommandType.Text;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetMyPaymentsAsync(int userId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT * FROM dbo.WN_Payments WHERE UserId=@UserId", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@UserId", userId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IDictionary<string, object?>> CreatePaymentAsync(int userId, int bookingId,
            double amount, string method, string transactionRef)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_Payments_Insert", conn);
            cmd.CommandType = CommandType.StoredProcedure;
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
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Payments SET Status=@Status WHERE TransactionRef=@Ref", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Ref", transactionRef);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdatePaymentStatusByGuidAsync(string guid, string status)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Payments SET Status=@Status WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeletePaymentAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Payments SET IsActive=0 WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetPaymentsByUserGuidAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                SELECT p.* FROM dbo.WN_Payments p
                JOIN dbo.WN_Users u ON p.UserId = u.Id
                WHERE u.IdGUID = @Guid";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Guid", guid);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        // ── Location ──────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllLocationsAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT * FROM dbo.WN_Locations", conn);
            cmd.CommandType = CommandType.Text;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreateLocationAsync(string name, string? address, int? cityId,
            string? openingTime, string? closingTime, bool isActive, int? branchId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                INSERT INTO dbo.WN_Locations (Name, Address, CityId, OpeningTime, ClosingTime, IsActive, BranchId)
                OUTPUT INSERTED.Id
                VALUES (@Name,@Address,@CityId,@OpeningTime,@ClosingTime,@IsActive,@BranchId)";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CityId", (object?)cityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OpeningTime", (object?)openingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ClosingTime", (object?)closingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", isActive);
            cmd.Parameters.AddWithValue("@BranchId", (object?)branchId ?? DBNull.Value);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }

        public async Task UpdateLocationAsync(string guid, string name, string? address, int? cityId,
            string? openingTime, string? closingTime, bool isActive, int? branchId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                UPDATE dbo.WN_Locations SET
                    Name=@Name, Address=@Address, CityId=@CityId,
                    OpeningTime=@OpeningTime, ClosingTime=@ClosingTime,
                    IsActive=@IsActive, BranchId=@BranchId
                WHERE IdGUID=@Guid";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CityId", (object?)cityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OpeningTime", (object?)openingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ClosingTime", (object?)closingTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", isActive);
            cmd.Parameters.AddWithValue("@BranchId", (object?)branchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteLocationAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Locations SET IsActive=0 WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        // ── SpaceType ─────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllSpaceTypesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT * FROM dbo.WN_SpaceTypes", conn);
            cmd.CommandType = CommandType.Text;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreateSpaceTypeAsync(string name, int capacity, bool hourlyAllowed)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                INSERT INTO dbo.WN_SpaceTypes (Name, Capacity, HourlyAllowed)
                OUTPUT INSERTED.Id VALUES (@Name,@Capacity,@HourlyAllowed)";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Capacity", capacity);
            cmd.Parameters.AddWithValue("@HourlyAllowed", hourlyAllowed);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }

        public async Task UpdateSpaceTypeAsync(string guid, string name, int capacity, bool hourlyAllowed)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_SpaceTypes SET Name=@Name, Capacity=@Capacity, HourlyAllowed=@HourlyAllowed WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Capacity", capacity);
            cmd.Parameters.AddWithValue("@HourlyAllowed", hourlyAllowed);
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteSpaceTypeAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_SpaceTypes SET IsActive=0 WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        // ── PricingPlan ───────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllPricingPlansAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT * FROM dbo.WN_PricingPlans", conn);
            cmd.CommandType = CommandType.Text;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreatePricingPlanAsync(string name, double price, string? billingCycle,
            int includesHours, bool isActive)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                INSERT INTO dbo.WN_PricingPlans (Name, Price, BillingCycle, IncludesHours, IsActive)
                OUTPUT INSERTED.Id VALUES (@Name,@Price,@BillingCycle,@IncludesHours,@IsActive)";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Price", price);
            cmd.Parameters.AddWithValue("@BillingCycle", (object?)billingCycle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IncludesHours", includesHours);
            cmd.Parameters.AddWithValue("@IsActive", isActive);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }

        public async Task UpdatePricingPlanAsync(int id, string name, double price, string? billingCycle,
            int includesHours, bool isActive)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                UPDATE dbo.WN_PricingPlans SET Name=@Name, Price=@Price, BillingCycle=@BillingCycle,
                    IncludesHours=@IncludesHours, IsActive=@IsActive WHERE Id=@Id", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Price", price);
            cmd.Parameters.AddWithValue("@BillingCycle", (object?)billingCycle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IncludesHours", includesHours);
            cmd.Parameters.AddWithValue("@IsActive", isActive);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeletePricingPlanAsync(int id)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_PricingPlans SET IsActive=0 WHERE Id=@Id", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetMembershipsByPlanIdAsync(int planId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT * FROM dbo.WN_Memberships WHERE PlanId=@PlanId", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@PlanId", planId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        // ── Membership ────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllMembershipsAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT * FROM dbo.WN_Memberships", conn);
            cmd.CommandType = CommandType.Text;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreateMembershipAsync(string? userId, int planId, string startDate)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                INSERT INTO dbo.WN_Memberships (UserId, PlanId, StartDate)
                OUTPUT INSERTED.Id VALUES (@UserId,@PlanId,@StartDate)";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PlanId", planId);
            cmd.Parameters.AddWithValue("@StartDate", startDate);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }

        public async Task UpdateMembershipStatusAsync(int id, string status)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Memberships SET Status=@Status WHERE Id=@Id", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteMembershipAsync(int id)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Memberships SET IsActive=0 WHERE Id=@Id", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetPaymentsByMembershipIdAsync(int membershipId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT * FROM dbo.WN_Payments WHERE MembershipId=@MembershipId", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@MembershipId", membershipId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        // ── Contact ───────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllContactsAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT * FROM dbo.WN_Contacts", conn);
            cmd.CommandType = CommandType.Text;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> BookTourAsync(string name, string email, string message, string phone, int? userId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                INSERT INTO dbo.WN_Contacts (Name, Email, Message, Phone, UserId)
                OUTPUT INSERTED.Id VALUES (@Name,@Email,@Message,@Phone,@UserId)";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@Message", message);
            cmd.Parameters.AddWithValue("@Phone", phone);
            cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }

        public async Task UpdateContactStatusAsync(string guid, string status)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Contacts SET Status=@Status WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteContactAsync(string guid)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Contacts SET IsActive=0 WHERE IdGUID=@Guid", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Guid", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        // ── Gallery ───────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllGalleryImagesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT * FROM dbo.WN_Gallery", conn);
            cmd.CommandType = CommandType.Text;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreateGalleryImageAsync(string? title, string imageUrl, int sortOrder, bool isActive)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                INSERT INTO dbo.WN_Gallery (Title, ImageUrl, SortOrder, IsActive)
                OUTPUT INSERTED.Id VALUES (@Title,@ImageUrl,@SortOrder,@IsActive)";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Title", (object?)title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", imageUrl);
            cmd.Parameters.AddWithValue("@SortOrder", sortOrder);
            cmd.Parameters.AddWithValue("@IsActive", isActive);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }

        public async Task UpdateGalleryImageAsync(string id, string? title, string imageUrl, int sortOrder, bool isActive)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                UPDATE dbo.WN_Gallery SET Title=@Title, ImageUrl=@ImageUrl,
                    SortOrder=@SortOrder, IsActive=@IsActive WHERE IdGUID=@Id", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Title", (object?)title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", imageUrl);
            cmd.Parameters.AddWithValue("@SortOrder", sortOrder);
            cmd.Parameters.AddWithValue("@IsActive", isActive);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeleteGalleryImageAsync(string id)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_Gallery SET IsActive=0 WHERE IdGUID=@Id", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // ── Floor ─────────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllFloorsAsync(int? locationId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            var sql = locationId.HasValue
                ? "SELECT * FROM dbo.WN_Floors WHERE LocationId=@LocationId"
                : "SELECT * FROM dbo.WN_Floors";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            if (locationId.HasValue) cmd.Parameters.AddWithValue("@LocationId", locationId.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreateFloorAsync(int locationId, string floorName)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                INSERT INTO dbo.WN_Floors (LocationId, FloorName)
                OUTPUT INSERTED.Id VALUES (@LocationId,@FloorName)";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@LocationId", locationId);
            cmd.Parameters.AddWithValue("@FloorName", floorName);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }

        // ── Amenity ───────────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllAmenitiesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT * FROM dbo.WN_Amenities", conn);
            cmd.CommandType = CommandType.Text;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreateAmenityAsync(string name)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "INSERT INTO dbo.WN_Amenities (Name) OUTPUT INSERTED.Id VALUES (@Name)", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Name", name);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }

        // ── SpaceConfig ───────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetSpaceConfigAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT * FROM dbo.WN_SpaceConfig", conn);
            cmd.CommandType = CommandType.Text;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<double> GetSecurityDepositAsync(string category)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT SecurityDeposit FROM dbo.WN_SpaceConfig WHERE Category=@Category", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Category", category);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? 0 : Convert.ToDouble(result);
        }

        public async Task UpdateSpaceConfigAsync(string category, int totalSpaces, string? defaultCapacities,
            string? openingTime, string? closingTime, string? adminEmail, double? securityDeposit)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
                UPDATE dbo.WN_SpaceConfig SET
                    TotalSpaces=@TotalSpaces, DefaultCapacities=@DefaultCapacities,
                    OpeningTime=@OpeningTime, ClosingTime=@ClosingTime,
                    AdminEmail=@AdminEmail, SecurityDeposit=@SecurityDeposit
                WHERE Category=@Category";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Category", category);
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
            await using var cmd = new SqlCommand("dbo.WN_GenerateSpaceInventory", conn);
            cmd.CommandType = CommandType.StoredProcedure;
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
            await using var cmd = new SqlCommand("SELECT * FROM dbo.WN_Branches", conn);
            cmd.CommandType = CommandType.Text;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllCompaniesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT * FROM dbo.WN_Companies", conn);
            cmd.CommandType = CommandType.Text;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetAllCitiesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT * FROM dbo.WN_Cities", conn);
            cmd.CommandType = CommandType.Text;
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        // ── PlanFeature ───────────────────────────────────────────────────────

        public async Task<IEnumerable<IDictionary<string, object?>>> GetPlanFeaturesByPlanIdAsync(int planId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT * FROM dbo.WN_PlanFeatures WHERE PlanId=@PlanId", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@PlanId", planId);
            await using var r = await cmd.ExecuteReaderAsync();
            return await ReadAllRowsAsync(r);
        }

        public async Task<int?> CreatePlanFeatureAsync(int planId, string featureName)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                INSERT INTO dbo.WN_PlanFeatures (PlanId, FeatureName)
                OUTPUT INSERTED.Id VALUES (@PlanId,@FeatureName)", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@PlanId", planId);
            cmd.Parameters.AddWithValue("@FeatureName", featureName);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }

        public async Task UpdatePlanFeatureAsync(int id, string featureName)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_PlanFeatures SET FeatureName=@FeatureName WHERE Id=@Id", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@FeatureName", featureName);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SoftDeletePlanFeatureAsync(int id)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.WN_PlanFeatures SET IsActive=0 WHERE Id=@Id", conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
