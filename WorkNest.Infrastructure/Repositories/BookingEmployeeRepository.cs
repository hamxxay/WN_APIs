using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Attendant;
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

        public Task ReopenAsync(int requestId) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_BookingEmployees_Reopen", c);
                cmd.Parameters.Add("@RequestId", SqlDbType.Int).Value = requestId;
                await cmd.ExecuteNonQueryAsync();
                return true;
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
