using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Reception;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Front desk (api/reception/*): plain reads of dbo.WN_Contacts / WN_Bookings / WN_Spaces / WN_Invoices, and the writes of the
    /// columns script ROLE_01_receptionist adds. Column checks (COL_LENGTH) are cached for a minute, so the API notices the
    /// script soon after it is run, no restart needed.
    /// </summary>
    public class ReceptionRepository : IReceptionRepository
    {
        private readonly string _connectionString;
        private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);
        private static (bool Value, DateTime At) _checkIn, _tour;

        public ReceptionRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private async Task<SqlConnection> OpenAsync()
        {
            var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            return c;
        }

        private async Task<bool> ColumnsExistAsync(params (string Table, string Column)[] columns)
        {
            await using var c = await OpenAsync();
            var sql = string.Join(" AND ", columns.Select((_, i) => $"COL_LENGTH(@t{i}, @c{i}) IS NOT NULL"));
            await using var cmd = new SqlCommand($"SELECT CASE WHEN {sql} THEN 1 ELSE 0 END", c);
            for (var i = 0; i < columns.Length; i++)
            {
                cmd.Parameters.AddWithValue($"@t{i}", columns[i].Table);
                cmd.Parameters.AddWithValue($"@c{i}", columns[i].Column);
            }
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        public async Task<bool> HasCheckInColumnsAsync()
        {
            if (DateTime.UtcNow - _checkIn.At < CacheFor) return _checkIn.Value;
            var v = await ColumnsExistAsync(("dbo.WN_Bookings", "MeetingCheckedInAt"), ("dbo.WN_Bookings", "MeetingCheckedOutAt"),
                ("dbo.WN_Bookings", "MeetingCheckedInById"), ("dbo.WN_Bookings", "MeetingCheckedOutById"));
            _checkIn = (v, DateTime.UtcNow);
            return v;
        }

        public async Task<bool> HasTourColumnsAsync()
        {
            if (DateTime.UtcNow - _tour.At < CacheFor) return _tour.Value;
            var v = await ColumnsExistAsync(("dbo.WN_Contacts", "PreferredDate"), ("dbo.WN_Contacts", "PreferredTime"),
                ("dbo.WN_Contacts", "Company"), ("dbo.WN_Contacts", "SpaceType"));
            _tour = (v, DateTime.UtcNow);
            return v;
        }

        /// <summary>"1,2" for a locations filter; null = no filter.</summary>
        private static string? Csv(IReadOnlyList<int>? locations) =>
            locations == null ? null : (locations.Count == 0 ? "-1" : string.Join(",", locations));

        private const string InLocations = "(@Locs IS NULL OR {0} IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(@Locs, ',')))";

        private static void AddLocs(SqlCommand cmd, IReadOnlyList<int>? locations) =>
            cmd.Parameters.Add("@Locs", SqlDbType.NVarChar, -1).Value = (object?)Csv(locations) ?? DBNull.Value;

        private static string? Str(SqlDataReader r, string col) => r[col] is DBNull ? null : Convert.ToString(r[col]);
        private static int? Int(SqlDataReader r, string col) => r[col] is DBNull ? null : Convert.ToInt32(r[col]);
        private static DateTime? Date(SqlDataReader r, string col) => r[col] is DBNull ? null : Convert.ToDateTime(r[col]);

        public async Task<List<ReceptionTourRow>> GetToursForDayAsync(IReadOnlyList<int>? locations, DateTime day)
        {
            var withColumns = await HasTourColumnsAsync();
            await using var c = await OpenAsync();
            var dayFilter = withColumns
                ? "c.PreferredDate = @Day"
                : "c.CreatedOn >= @Day AND c.CreatedOn < DATEADD(DAY, 1, @Day)";
            // Own branches plus the inquiries without a branch, as the Tour Inquiries list.
            var sql = $@"SELECT c.Id, c.PublicId, c.Name, c.PhoneNumber, c.Email, c.StatusId, l.Name AS LocationName,
                       {(withColumns ? "c.PreferredDate, c.PreferredTime" : "CAST(NULL AS DATE) AS PreferredDate, CAST(NULL AS NVARCHAR(20)) AS PreferredTime")}
                  FROM dbo.WN_Contacts c WITH (NOLOCK)
                  LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = c.LocationId
                 WHERE c.ContactType = N'book_tour'
                   AND {dayFilter}
                   AND (@Locs IS NULL OR c.LocationId IS NULL OR c.LocationId IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(@Locs, ',')))
                 ORDER BY c.CreatedOn DESC";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@Day", SqlDbType.DateTime2).Value = day.Date;
            AddLocs(cmd, locations);
            var list = new List<ReceptionTourRow>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new ReceptionTourRow(Convert.ToInt32(r["Id"]), r["PublicId"] is Guid g ? g : null, Str(r, "Name"), Str(r, "PhoneNumber"),
                    Str(r, "Email"), Date(r, "PreferredDate"), Str(r, "PreferredTime"), Convert.ToInt32(r["StatusId"]), Str(r, "LocationName")));
            return list;
        }

        public async Task<List<ReceptionRoomRow>> GetMeetingRoomsAsync(IReadOnlyList<int>? locations)
        {
            await using var c = await OpenAsync();
            var sql = $@"SELECT s.Id, s.Name, s.Capacity, s.LocationId, l.Name AS LocationName
                  FROM dbo.WN_Spaces s WITH (NOLOCK)
                  JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
                  LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = s.LocationId
                 WHERE s.IsActive = 1 AND st.CategoryId = 2 AND {string.Format(InLocations, "s.LocationId")}
                 ORDER BY l.Name, s.Name, s.Id";
            await using var cmd = new SqlCommand(sql, c);
            AddLocs(cmd, locations);
            var list = new List<ReceptionRoomRow>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new ReceptionRoomRow(Convert.ToInt32(r["Id"]), Str(r, "Name"), Int(r, "LocationId"), Str(r, "LocationName"), Int(r, "Capacity")));
            return list;
        }

        private async Task<string> BookingSelectAsync() => $@"SELECT b.Id, b.SpaceId, s.Name AS SpaceName, s.LocationId, b.UserId, u.Name AS CustomerName,
                       b.StartOn, b.EndOn, b.BookingStatusId, b.IsFree, cb.Name AS BookedBy,
                       {(await HasCheckInColumnsAsync() ? "b.MeetingCheckedInAt AS CheckedInAt, b.MeetingCheckedOutAt AS CheckedOutAt" : "CAST(NULL AS DATETIME2) AS CheckedInAt, CAST(NULL AS DATETIME2) AS CheckedOutAt")}
                  FROM dbo.WN_Bookings b WITH (NOLOCK)
                  JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                  JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
                  LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = b.UserId
                  LEFT JOIN dbo.WN_Users cb WITH (NOLOCK) ON cb.Id = b.CreatedById";

        private static ReceptionBookingRow ReadBooking(SqlDataReader r) =>
            new(Convert.ToInt32(r["Id"]), Convert.ToInt32(r["SpaceId"]), Str(r, "SpaceName"), Int(r, "LocationId"),
                r["UserId"] is DBNull ? 0 : Convert.ToInt32(r["UserId"]), Str(r, "CustomerName"),
                Convert.ToDateTime(r["StartOn"]), Convert.ToDateTime(r["EndOn"]), Convert.ToInt32(r["BookingStatusId"]),
                r["IsFree"] is not DBNull && Convert.ToBoolean(r["IsFree"]), Date(r, "CheckedInAt"), Date(r, "CheckedOutAt"), Str(r, "BookedBy"));

        public async Task<List<ReceptionBookingRow>> GetMeetingBookingsAsync(IReadOnlyList<int>? locations, DateTime from, DateTime to)
        {
            var select = await BookingSelectAsync();
            await using var c = await OpenAsync();
            var sql = $@"{select}
                 WHERE b.IsDeleted = 0 AND st.CategoryId = 2 AND b.BookingStatusId IN (1, 2, 5, 6, 33)
                   AND b.StartOn < @To AND b.EndOn > @From AND {string.Format(InLocations, "s.LocationId")}
                 ORDER BY b.StartOn, b.Id";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = from;
            cmd.Parameters.Add("@To", SqlDbType.DateTime2).Value = to;
            AddLocs(cmd, locations);
            var list = new List<ReceptionBookingRow>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(ReadBooking(r));
            return list;
        }

        public async Task<ReceptionBookingRow?> GetMeetingBookingAsync(int bookingId)
        {
            var select = await BookingSelectAsync();
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand($"{select} WHERE b.Id = @Id AND b.IsDeleted = 0 AND st.CategoryId = 2", c);
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = bookingId;
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? ReadBooking(r) : null;
        }

        public async Task<List<ReceptionInvoiceRow>> GetInvoicesDueAsync(IReadOnlyList<int>? locations, DateTime day)
        {
            await using var c = await OpenAsync();
            // The invoice's own location, else the location of its booked space.
            var sql = $@"SELECT i.Id, i.InvoiceNumber, u.Name AS CustomerName, i.DueOn, i.BalanceDue
                  FROM dbo.WN_Invoices i WITH (NOLOCK)
                  LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
                  LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
                  LEFT JOIN dbo.WN_Spaces sp WITH (NOLOCK) ON sp.Id = b.SpaceId
                 WHERE i.DueOn = @Day AND i.BalanceDue > 0 AND i.StatusId NOT IN (40, 62)
                   AND ISNULL(i.ApprovalStatus, N'') <> N'PendingApproval'
                   AND {string.Format(InLocations, "COALESCE(i.LocationId, sp.LocationId)")}
                 ORDER BY i.Id";
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@Day", SqlDbType.Date).Value = day.Date;
            AddLocs(cmd, locations);
            var list = new List<ReceptionInvoiceRow>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new ReceptionInvoiceRow(Convert.ToInt32(r["Id"]), Str(r, "InvoiceNumber"), Str(r, "CustomerName"), Date(r, "DueOn"),
                    r["BalanceDue"] is DBNull ? 0 : Convert.ToDecimal(r["BalanceDue"])));
            return list;
        }

        public async Task<bool> SetCheckInAsync(int bookingId, DateTime at, int? byUserId)
        {
            if (!await HasCheckInColumnsAsync()) throw new ReceptionNotSetUpException();
            await using var c = await OpenAsync();
            // Only a confirmed booking that is not checked in yet.
            await using var cmd = new SqlCommand(@"UPDATE dbo.WN_Bookings SET MeetingCheckedInAt = @At, MeetingCheckedInById = @By
                 WHERE Id = @Id AND IsDeleted = 0 AND BookingStatusId = 33 AND MeetingCheckedInAt IS NULL", c);
            cmd.Parameters.Add("@At", SqlDbType.DateTime2).Value = at;
            cmd.Parameters.Add("@By", SqlDbType.Int).Value = (object?)byUserId ?? DBNull.Value;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = bookingId;
            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        public async Task<bool> SetCheckOutAsync(int bookingId, DateTime at, int? byUserId)
        {
            if (!await HasCheckInColumnsAsync()) throw new ReceptionNotSetUpException();
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand(@"UPDATE dbo.WN_Bookings SET MeetingCheckedOutAt = @At, MeetingCheckedOutById = @By
                 WHERE Id = @Id AND IsDeleted = 0 AND MeetingCheckedInAt IS NOT NULL AND MeetingCheckedOutAt IS NULL", c);
            cmd.Parameters.Add("@At", SqlDbType.DateTime2).Value = at;
            cmd.Parameters.Add("@By", SqlDbType.Int).Value = (object?)byUserId ?? DBNull.Value;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = bookingId;
            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        public async Task SetBookingCreatorAsync(int bookingId, int staffUserId)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("UPDATE dbo.WN_Bookings SET CreatedById = @By WHERE Id = @Id AND IsFree = 1", c);
            cmd.Parameters.Add("@By", SqlDbType.Int).Value = staffUserId;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = bookingId;
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> SaveTourExtrasAsync(int contactId, DateTime? preferredDate, string? preferredTime, string? company, string? spaceType)
        {
            if (!await HasTourColumnsAsync()) return false;
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand(@"UPDATE dbo.WN_Contacts SET PreferredDate = @D, PreferredTime = @T, Company = @C, SpaceType = @S WHERE Id = @Id", c);
            cmd.Parameters.Add("@D", SqlDbType.Date).Value = (object?)preferredDate?.Date ?? DBNull.Value;
            cmd.Parameters.Add("@T", SqlDbType.NVarChar, 20).Value = (object?)preferredTime ?? DBNull.Value;
            cmd.Parameters.Add("@C", SqlDbType.NVarChar, 200).Value = (object?)company ?? DBNull.Value;
            cmd.Parameters.Add("@S", SqlDbType.NVarChar, 100).Value = (object?)spaceType ?? DBNull.Value;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = contactId;
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
    }
}
