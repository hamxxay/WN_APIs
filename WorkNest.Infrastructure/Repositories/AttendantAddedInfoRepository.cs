using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Who added each person of a booked room: customer requests (dbo.WN_BookingEmployeeRequests) and, for staff, the
    /// insert audit of WN_BookingAttendants (dbo.WN_AuditLog). Read-only; works before EMP_01 (missing table = skipped).
    /// </summary>
    public class AttendantAddedInfoRepository : IAttendantAddedInfoRepository
    {
        private readonly string _connectionString;
        private readonly IBusinessClock _clock;

        public AttendantAddedInfoRepository(IConfiguration configuration, IBusinessClock clock)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
            _clock = clock;
        }

        public async Task<List<AttendantAddedInfo>> GetAsync(int bookingDetailId)
        {
            var result = new Dictionary<int, AttendantAddedInfo>();
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();

            async Task Read(string sql, Action<SqlDataReader> each)
            {
                try
                {
                    await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 30 };
                    cmd.Parameters.Add("@D", SqlDbType.Int).Value = bookingDetailId;
                    await using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync()) each(r);
                }
                catch (SqlException ex) when (ex.Number is 208 or 207 or 2812) { /* source not on this database */ }
            }

            string Day(object v) => _clock.FromUtc(DateTime.SpecifyKind(Convert.ToDateTime(v), DateTimeKind.Utc)).ToString("yyyy-MM-dd");

            // Staff: first insert of the assignment (oldest first, so the earliest wins).
            await Read(@"SELECT a.ChangedAt, a.NewValues, a.ChangedByUserEmail
                           FROM dbo.WN_AuditLog a WITH (NOLOCK)
                          WHERE a.EntityName = N'WN_BookingAttendants' AND a.Operation = 'I'
                            AND ISJSON(a.NewValues) = 1
                            AND TRY_CAST(JSON_VALUE(a.NewValues, '$.BookingDetailId') AS INT) = @D
                          ORDER BY a.ChangedAt DESC",
                r =>
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(Convert.ToString(r["NewValues"]) ?? "{}");
                        if (!doc.RootElement.TryGetProperty("PersonId", out var p) || !p.TryGetInt32(out var pid)) return;
                        result[pid] = new AttendantAddedInfo
                        {
                            PersonId = pid, ByCustomer = false,
                            ByEmail = r["ChangedByUserEmail"] is DBNull ? null : Convert.ToString(r["ChangedByUserEmail"]),
                            On = r["ChangedAt"] is DBNull ? null : Day(r["ChangedAt"])
                        };
                    }
                    catch (JsonException) { }
                });

            // Customer-added people override (the staff assignment behind them is just the CNIC verification).
            await Read(@"SELECT r.PersonId, r.AddedAt, u.Email AS AddedByEmail
                           FROM dbo.WN_BookingEmployeeRequests r WITH (NOLOCK)
                           LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = r.AddedByUserId
                          WHERE r.BookingDetailId = @D AND r.Status IN (N'Pending', N'Verified')
                          ORDER BY r.AddedAt DESC",
                r => result[Convert.ToInt32(r["PersonId"])] = new AttendantAddedInfo
                {
                    PersonId = Convert.ToInt32(r["PersonId"]), ByCustomer = true,
                    ByEmail = r["AddedByEmail"] is DBNull ? null : Convert.ToString(r["AddedByEmail"]),
                    On = r["AddedAt"] is DBNull ? null : Day(r["AddedAt"])
                });

            return result.Values.ToList();
        }
    }
}
