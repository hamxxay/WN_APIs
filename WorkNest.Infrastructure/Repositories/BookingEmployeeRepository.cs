using System.Data;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Attendant;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Customer-added employees (dbo.WN_BookingEmployeeRequests) through the dbo.WN_BookingEmployees_* procedures
    /// (Database script EMP_01_customer_employees). SQL errors 208 / 2812 (table / procedure missing) mean the script
    /// has not been run yet: they become <see cref="BookingEmployeesNotInstalledException"/>.
    /// </summary>
    public class BookingEmployeeRepository : IBookingEmployeeRepository
    {
        private readonly string _connectionString;

        public BookingEmployeeRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        public Task<(BookingEmployeeBookingRow? Booking, List<BookingEmployeeRequestRow> Requests)> GetAsync(int bookingId, int userId) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_BookingEmployees_Get", c);
                cmd.Parameters.Add("@BookingId", SqlDbType.Int).Value = bookingId;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                await using var r = await cmd.ExecuteReaderAsync();

                BookingEmployeeBookingRow? booking = null;
                if (await r.ReadAsync())
                {
                    booking = new BookingEmployeeBookingRow
                    {
                        BookingId = Int(r, "BookingId") ?? 0,
                        BookingStatusId = Int(r, "BookingStatusId") ?? 0,
                        StartOn = Date(r, "StartOn"),
                        EndOn = Date(r, "EndOn"),
                        BookingDetailId = Int(r, "BookingDetailId"),
                        DetailEnd = Date(r, "DetailEnd"),
                        SpaceName = Str(r, "SpaceName"),
                        LocationName = Str(r, "LocationName"),
                        CustomerId = Int(r, "CustomerId")
                    };
                }

                var requests = new List<BookingEmployeeRequestRow>();
                if (await r.NextResultAsync())
                {
                    while (await r.ReadAsync())
                    {
                        requests.Add(new BookingEmployeeRequestRow
                        {
                            RequestId = Int(r, "RequestId") ?? 0,
                            PersonId = Int(r, "PersonId") ?? 0,
                            Status = Str(r, "Status") ?? "",
                            AddedAt = DateTime.SpecifyKind(Date(r, "AddedAt") ?? DateTime.UtcNow, DateTimeKind.Utc),
                            BookingDetailId = Int(r, "BookingDetailId"),
                            Name = Str(r, "Name") ?? "",
                            Email = Str(r, "Email") ?? "",
                            Phone = Str(r, "Phone") ?? "",
                            IdType = Str(r, "IdType") ?? "",
                            IdNumber = Str(r, "IdNumber") ?? ""
                        });
                    }
                }
                return (booking, requests);
            });

        public Task<BookingEmployeePersonMatch?> FindPersonAsync(string idType, string idNumber, int customerId) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_BookingEmployees_FindPerson", c);
                cmd.Parameters.Add("@IdType", SqlDbType.VarChar, 10).Value = idType;
                cmd.Parameters.Add("@IdNumber", SqlDbType.VarChar, 30).Value = idNumber;
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return (BookingEmployeePersonMatch?)null;
                return new BookingEmployeePersonMatch
                {
                    PersonId = Int(r, "PersonId") ?? 0,
                    Name = Str(r, "Name") ?? "",
                    LinkedToCustomer = r["LinkedToCustomer"] is not DBNull && Convert.ToBoolean(r["LinkedToCustomer"])
                };
            });

        public Task<(string Result, int? RequestId, int Used)> AddRequestAsync(int bookingId, int bookingDetailId, int? customerId, int personId, int? addedByUserId, int seats) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_BookingEmployees_AddRequest", c);
                cmd.Parameters.Add("@BookingId", SqlDbType.Int).Value = bookingId;
                cmd.Parameters.Add("@BookingDetailId", SqlDbType.Int).Value = bookingDetailId;
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = (object?)customerId ?? DBNull.Value;
                cmd.Parameters.Add("@PersonId", SqlDbType.Int).Value = personId;
                cmd.Parameters.Add("@AddedByUserId", SqlDbType.Int).Value = (object?)addedByUserId ?? DBNull.Value;
                cmd.Parameters.Add("@Seats", SqlDbType.Int).Value = seats;
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return ("error", (int?)null, 0);
                return (Str(r, "Result") ?? "error", Int(r, "RequestId"), Int(r, "Used") ?? 0);
            });

        public Task<int> RemoveRequestsAsync(int bookingId, int personId) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_BookingEmployees_RemoveRequests", c);
                cmd.Parameters.Add("@BookingId", SqlDbType.Int).Value = bookingId;
                cmd.Parameters.Add("@PersonId", SqlDbType.Int).Value = personId;
                return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            });

        public Task<BookingEmployeePendingMatch?> FindPendingByIdNumberAsync(int bookingDetailId, string idNumberDigits) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_BookingEmployees_FindPendingByIdNumber", c);
                cmd.Parameters.Add("@BookingDetailId", SqlDbType.Int).Value = bookingDetailId;
                cmd.Parameters.Add("@IdNumber", SqlDbType.VarChar, 30).Value = idNumberDigits;
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return (BookingEmployeePendingMatch?)null;
                return new BookingEmployeePendingMatch
                {
                    RequestId = Int(r, "RequestId") ?? 0,
                    BookingId = Int(r, "BookingId") ?? 0,
                    PersonId = Int(r, "PersonId") ?? 0,
                    CustomerId = Int(r, "CustomerId"),
                    Name = Str(r, "Name") ?? "",
                    Email = Str(r, "Email") ?? "",
                    Phone = Str(r, "Phone") ?? "",
                    IdType = Str(r, "IdType") ?? "",
                    IdNumber = Str(r, "IdNumber") ?? ""
                };
            });

        public Task<int> VerifyAsync(int? requestId, int? bookingDetailId, int? personId, int? verifiedById) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_BookingEmployees_Verify", c);
                cmd.Parameters.Add("@RequestId", SqlDbType.Int).Value = (object?)requestId ?? DBNull.Value;
                cmd.Parameters.Add("@BookingDetailId", SqlDbType.Int).Value = (object?)bookingDetailId ?? DBNull.Value;
                cmd.Parameters.Add("@PersonId", SqlDbType.Int).Value = (object?)personId ?? DBNull.Value;
                cmd.Parameters.Add("@VerifiedById", SqlDbType.Int).Value = (object?)verifiedById ?? DBNull.Value;
                return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            });

        public Task<bool> EmailUsedByOtherAsync(string email, int exceptPersonId) =>
            Run(async c =>
            {
                await using var cmd = new SqlCommand(
                    "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.WN_Persons o WITH (NOLOCK) WHERE o.PersonId <> @P AND LOWER(LTRIM(RTRIM(o.Email))) = @E) THEN 1 ELSE 0 END", c);
                cmd.Parameters.Add("@P", SqlDbType.Int).Value = exceptPersonId;
                cmd.Parameters.Add("@E", SqlDbType.NVarChar, 256).Value = email.Trim().ToLowerInvariant();
                return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0) == 1;
            });

        public Task<bool> SetPersonEmailAsync(int personId, string email, int actorUserId) =>
            Run(async c =>
            {
                // The audit trigger reads the acting user from SESSION_CONTEXT (as DbRepository.Open sets it).
                const string sql = @"
                    EXEC sp_set_session_context @key = N'AppUserId', @value = @U;
                    DECLARE @UserEmail NVARCHAR(256) = (SELECT TOP 1 Email FROM dbo.WN_Users WITH (NOLOCK) WHERE Id = @U);
                    IF @UserEmail IS NOT NULL EXEC sp_set_session_context @key = N'AppUserEmail', @value = @UserEmail;
                    EXEC sp_set_session_context @key = N'SourceApp', @value = N'WebAPI';
                    UPDATE dbo.WN_Persons SET Email = @E
                     WHERE PersonId = @P
                       AND NOT EXISTS (SELECT 1 FROM dbo.WN_Persons o WHERE o.PersonId <> @P AND LOWER(LTRIM(RTRIM(o.Email))) = LOWER(@E));
                    SELECT @@ROWCOUNT;";
                await using var cmd = new SqlCommand(sql, c);
                cmd.Parameters.Add("@U", SqlDbType.Int).Value = actorUserId;
                cmd.Parameters.Add("@P", SqlDbType.Int).Value = personId;
                cmd.Parameters.Add("@E", SqlDbType.NVarChar, 150).Value = email.Trim();
                return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0) > 0;
            });

        // ---- employee history (CMS): read-only sources ----

        // The people of a booking: on one of its rooms now or before (WN_BookingAttendants) or added as a request.
        private const string PeopleOfBooking = @"
            SELECT ba.PersonId FROM dbo.WN_BookingAttendants ba WITH (NOLOCK) WHERE ba.BookingDetailId IN " + DetailsOfBooking + @"
            UNION
            SELECT r.PersonId FROM dbo.WN_BookingEmployeeRequests r WITH (NOLOCK) WHERE r.BookingId = @B";
        private const string DetailsOfBooking = @"
            (SELECT d.Id FROM dbo.WN_BookingDetails d WITH (NOLOCK)
               JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.IdGUID = d.BookingGuid WHERE b.Id = @B)";

        public async Task<EmployeeHistoryRaw> GetHistoryAsync(int bookingId)
        {
            var raw = new EmployeeHistoryRaw();
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();

            async Task Read(string sql, Action<SqlDataReader> each)
            {
                try
                {
                    await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 30 };
                    cmd.Parameters.Add("@B", SqlDbType.Int).Value = bookingId;
                    await using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync()) each(r);
                }
                catch (SqlException ex) when (ex.Number is 208 or 207 or 2812) { /* that source is not on this database: skip it */ }
            }

            await Read($@"SELECT p.PersonId, p.Name, p.IdNumber FROM dbo.WN_Persons p WITH (NOLOCK)
                         WHERE p.PersonId IN ({PeopleOfBooking})",
                r => raw.People.Add(new EmployeeHistoryPerson { PersonId = Convert.ToInt32(r["PersonId"]), Name = Str(r, "Name") ?? "", IdNumber = Str(r, "IdNumber") ?? "" }));

            var machine = new Dictionary<int, string>();
            await Read($"SELECT m.PersonId, m.MachineID FROM dbo.WN_HIK_PersonMap m WITH (NOLOCK) WHERE m.PersonId IN ({PeopleOfBooking})",
                r => machine[Convert.ToInt32(r["PersonId"])] = Str(r, "MachineID") ?? "");
            foreach (var p in raw.People) if (machine.TryGetValue(p.PersonId, out var m)) p.MachineId = m;

            await Read($@"SELECT r.PersonId, r.AddedAt, r.RemovedAt, u.Email AS AddedByEmail
                          FROM dbo.WN_BookingEmployeeRequests r WITH (NOLOCK)
                          LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = r.AddedByUserId
                         WHERE r.BookingId = @B",
                r => raw.Requests.Add(new EmployeeHistoryRequest
                {
                    PersonId = Convert.ToInt32(r["PersonId"]),
                    AddedAtUtc = Utc(r["AddedAt"]),
                    AddedByEmail = Str(r, "AddedByEmail"),
                    RemovedAtUtc = r["RemovedAt"] is DBNull ? null : Utc(r["RemovedAt"])
                }));

            // Audit rows (WN_AuditLog.ChangedAt is UTC). The JSON of these small tables is read here.
            await Read($@"SELECT a.ChangedAt, a.Operation, a.OldValues, a.NewValues, a.ChangedByUserEmail
                          FROM dbo.WN_AuditLog a WITH (NOLOCK)
                         WHERE a.EntityName = N'WN_BookingAttendants'
                           AND ISJSON(COALESCE(a.NewValues, a.OldValues)) = 1
                           AND TRY_CAST(JSON_VALUE(COALESCE(a.NewValues, a.OldValues), '$.BookingDetailId') AS INT) IN {DetailsOfBooking}",
                r =>
                {
                    var o = Json(Str(r, "OldValues")); var n = Json(Str(r, "NewValues"));
                    var person = JInt(n, "PersonId") ?? JInt(o, "PersonId");
                    if (person == null) return;
                    raw.Assignments.Add(new EmployeeHistoryAssignmentAudit
                    {
                        AtUtc = Utc(r["ChangedAt"]), Operation = Str(r, "Operation") ?? "", PersonId = person.Value,
                        OldAssignedTo = JDate(o, "AssignedTo"), NewAssignedTo = JDate(n, "AssignedTo"), ByEmail = Str(r, "ChangedByUserEmail")
                    });
                });

            await Read($@"SELECT a.ChangedAt, a.Operation, a.OldValues, a.NewValues, a.ChangedByUserEmail
                          FROM dbo.WN_AuditLog a WITH (NOLOCK)
                         WHERE a.EntityName = N'WN_AccessStatus'
                           AND ISJSON(COALESCE(a.NewValues, a.OldValues)) = 1
                           AND TRY_CAST(JSON_VALUE(COALESCE(a.NewValues, a.OldValues), '$.BookingDetailId') AS INT) IN {DetailsOfBooking}",
                r =>
                {
                    var o = Json(Str(r, "OldValues")); var n = Json(Str(r, "NewValues"));
                    var person = JInt(n, "PersonId") ?? JInt(o, "PersonId");
                    if (person == null) return;
                    raw.Access.Add(new EmployeeHistoryAccessAudit
                    {
                        AtUtc = Utc(r["ChangedAt"]), Operation = Str(r, "Operation") ?? "", PersonId = person.Value,
                        OldEnabled = JBool(o, "IsEnabled"), NewEnabled = JBool(n, "IsEnabled"), ByEmail = Str(r, "ChangedByUserEmail")
                    });
                });

            await Read($@"SELECT a.ChangedAt, a.RecordId, a.OldValues, a.NewValues, a.ChangedByUserEmail
                          FROM dbo.WN_AuditLog a WITH (NOLOCK)
                         WHERE a.EntityName = N'WN_Persons' AND a.Operation = 'U'
                           AND a.RecordId IN (SELECT CAST(x.PersonId AS NVARCHAR(64)) FROM ({PeopleOfBooking}) x)",
                r =>
                {
                    if (!int.TryParse(Str(r, "RecordId"), out var person)) return;
                    var o = Json(Str(r, "OldValues")); var n = Json(Str(r, "NewValues"));
                    raw.Emails.Add(new EmployeeHistoryEmailAudit
                    {
                        AtUtc = Utc(r["ChangedAt"]), PersonId = person,
                        OldEmail = JStr(o, "Email"), NewEmail = JStr(n, "Email"), ByEmail = Str(r, "ChangedByUserEmail")
                    });
                });

            // Fingerprints / faces: the vault tables' audit rows (written when the template is vaulted after a capture).
            await Read($@"SELECT a.ChangedAt, a.EntityName, JSON_VALUE(a.NewValues, '$.employee_no') AS EmployeeNo,
                                 JSON_VALUE(a.NewValues, '$.finger_no') AS FingerNo, a.ChangedByUserEmail
                          FROM dbo.WN_AuditLog a WITH (NOLOCK)
                         WHERE a.EntityName IN (N'WN_HIK_FpVault', N'WN_HIK_FaceVault') AND a.Operation IN ('I', 'U')
                           AND JSON_VALUE(a.NewValues, '$.employee_no') IN
                               (SELECT m.MachineID FROM dbo.WN_HIK_PersonMap m WITH (NOLOCK) WHERE m.PersonId IN ({PeopleOfBooking}))",
                r =>
                {
                    var face = string.Equals(Str(r, "EntityName"), "WN_HIK_FaceVault", StringComparison.OrdinalIgnoreCase);
                    raw.Credentials.Add(new EmployeeHistoryCredential
                    {
                        AtUtc = Utc(r["ChangedAt"]), Kind = face ? "face" : "fingerprint", EmployeeNo = Str(r, "EmployeeNo") ?? "",
                        Detail = !face && Str(r, "FingerNo") is { Length: > 0 } f ? $"Finger {f}" : null,
                        ByEmail = Str(r, "ChangedByUserEmail")
                    });
                });

            await Read($@"SELECT k.Created_at, k.Employee_no, k.Card_no, u.Email AS ByEmail
                          FROM dbo.WN_HIK_Cards k WITH (NOLOCK)
                          LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = k.Created_by
                         WHERE k.Created_at IS NOT NULL AND k.Employee_no IN
                               (SELECT m.MachineID FROM dbo.WN_HIK_PersonMap m WITH (NOLOCK) WHERE m.PersonId IN ({PeopleOfBooking}))",
                r =>
                {
                    var no = Str(r, "Card_no") ?? "";
                    raw.Credentials.Add(new EmployeeHistoryCredential
                    {
                        AtUtc = Utc(r["Created_at"]), Kind = "card", EmployeeNo = Str(r, "Employee_no") ?? "",
                        Detail = no.Length >= 4 ? $"Card ending {no[^4..]}" : null, ByEmail = Str(r, "ByEmail")
                    });
                });

            return raw;
        }

        private static DateTime Utc(object v) => DateTime.SpecifyKind(Convert.ToDateTime(v), DateTimeKind.Utc);
        private static JsonElement? Json(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            try { return JsonDocument.Parse(s).RootElement.Clone(); } catch (JsonException) { return null; }
        }
        private static JsonElement? Prop(JsonElement? j, string name) =>
            j is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;
        private static int? JInt(JsonElement? j, string name) => Prop(j, name) is { } v && v.TryGetInt32(out var i) ? i : null;
        private static string? JStr(JsonElement? j, string name) => Prop(j, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
        private static bool? JBool(JsonElement? j, string name) =>
            Prop(j, name) is { } v && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : null;
        private static DateTime? JDate(JsonElement? j, string name) =>
            JStr(j, name) is { } s && DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;

        public Task ReopenAsync(int requestId) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_BookingEmployees_Reopen", c);
                cmd.Parameters.Add("@RequestId", SqlDbType.Int).Value = requestId;
                await cmd.ExecuteNonQueryAsync();
                return true;
            });

        public Task<List<(int PersonId, int? BookingDetailId, bool IsActiveSomewhere)>> GetPendingPersonsAsync(int? customerId, int? bookingDetailId) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_BookingEmployees_PendingPersons", c);
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = (object?)customerId ?? DBNull.Value;
                cmd.Parameters.Add("@BookingDetailId", SqlDbType.Int).Value = (object?)bookingDetailId ?? DBNull.Value;
                var list = new List<(int, int?, bool)>();
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    list.Add((Convert.ToInt32(r["PersonId"]), Int(r, "BookingDetailId"), Convert.ToInt32(r["IsActiveSomewhere"]) == 1));
                return list;
            });

        // ---- helpers ----

        private async Task<T> Run<T>(Func<SqlConnection, Task<T>> body)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                return await body(c);
            }
            catch (SqlException ex) when (ex.Number is 208 or 2812) // invalid object / procedure not found: EMP_01 not run
            {
                throw new BookingEmployeesNotInstalledException(ex);
            }
        }

        private static SqlCommand SP(string name, SqlConnection c) => new(name, c) { CommandType = CommandType.StoredProcedure };

        private static int? Int(SqlDataReader r, string col) => r[col] is DBNull ? null : Convert.ToInt32(r[col]);
        private static string? Str(SqlDataReader r, string col) => r[col] is DBNull ? null : Convert.ToString(r[col]);
        private static DateTime? Date(SqlDataReader r, string col) => r[col] is DBNull ? null : Convert.ToDateTime(r[col]);
    }
}
