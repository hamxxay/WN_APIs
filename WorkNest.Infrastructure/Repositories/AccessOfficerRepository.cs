using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.AccessOfficer;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Access Officer page: the read-only procedure dbo.WN_AccessOfficer_Lookup (script AO_01_access_officer). SQL errors
    /// 2812 / 208 (procedure / table missing) mean AO_01 has not run: <see cref="AccessOfficerDatabaseUpdateMissingException"/>
    /// (503). The writes go through the existing attendants / customer-employees procedures.
    /// </summary>
    public class AccessOfficerRepository : IAccessOfficerRepository
    {
        private readonly string _connectionString;

        public AccessOfficerRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        public async Task<List<AccessOfficerMatchRow>> LookupAsync(string? idKey, int? personId, int? bookingDetailId, string? locationIds)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                await using var cmd = new SqlCommand("dbo.WN_AccessOfficer_Lookup", c) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.Add("@Cnic", SqlDbType.NVarChar, 40).Value = (object?)idKey ?? DBNull.Value;
                cmd.Parameters.Add("@PersonId", SqlDbType.Int).Value = (object?)personId ?? DBNull.Value;
                cmd.Parameters.Add("@BookingDetailId", SqlDbType.Int).Value = (object?)bookingDetailId ?? DBNull.Value;
                cmd.Parameters.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = (object?)locationIds ?? DBNull.Value;

                var list = new List<AccessOfficerMatchRow>();
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(new AccessOfficerMatchRow
                    {
                        BookingDetailId = Int(r, "BookingDetailId") ?? 0,
                        BookingId = Int(r, "BookingId") ?? 0,
                        PersonId = Int(r, "PersonId") ?? 0,
                        State = Str(r, "State") ?? AccessOfficerStates.Pending,
                        Name = Str(r, "Name") ?? "",
                        Email = Str(r, "Email"),
                        Phone = Str(r, "Phone"),
                        IdType = Str(r, "IdType") ?? "",
                        IdNumber = Str(r, "IdNumber") ?? "",
                        Room = Str(r, "Room") ?? "",
                        SpaceCode = Str(r, "SpaceCode"),
                        SpaceName = Str(r, "SpaceName"),
                        LocationId = Int(r, "LocationId"),
                        Location = Str(r, "Location"),
                        Customer = Str(r, "Customer"),
                        CustomerId = Int(r, "CustomerId"),
                        BookingStatus = Str(r, "BookingStatus"),
                        StartOn = Date(r, "StartOn"),
                        EndOn = Date(r, "EndOn"),
                        AccessEnabled = r["AccessEnabled"] is not DBNull && Convert.ToBoolean(r["AccessEnabled"]),
                        MachineId = Str(r, "MachineId"),
                        ActiveCount = Int(r, "ActiveCount") ?? 0,
                        PendingCount = Int(r, "PendingCount") ?? 0
                    });
                }
                return list;
            }
            catch (SqlException ex) when (ex.Number is 2812 or 208) // procedure (or a table it reads) missing: AO_01 not run
            {
                throw new AccessOfficerDatabaseUpdateMissingException(ex);
            }
        }

        private static int? Int(SqlDataReader r, string col) => r[col] is DBNull ? null : Convert.ToInt32(r[col]);
        private static string? Str(SqlDataReader r, string col) => r[col] is DBNull ? null : Convert.ToString(r[col]);
        private static DateTime? Date(SqlDataReader r, string col) => r[col] is DBNull ? null : Convert.ToDateTime(r[col]);
    }
}
