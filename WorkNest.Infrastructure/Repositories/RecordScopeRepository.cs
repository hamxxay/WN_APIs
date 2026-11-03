using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Location of a single record (booking / payment / agreement / customer / user / quotation / invoice / space /
    /// location), read via stored procedures.
    /// </summary>
    public class RecordScopeRepository : IRecordScopeRepository
    {
        private readonly string _connectionString;

        public RecordScopeRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        public async Task<List<int>?> GetLocationIdsAsync(RecordKind kind, string key)
        {
            int? id = int.TryParse(key, out var n) ? n : null;
            Guid? guid = Guid.TryParse(key, out var g) ? g : null;
            string? email = kind == RecordKind.User && id == null && guid == null && key.Contains('@') ? key : null;
            if (id == null && guid == null && email == null) return null;
            if (kind is RecordKind.Agreement or RecordKind.BookingDetail or RecordKind.Quotation or RecordKind.Membership && id == null) return null;

            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_RecordScope_GetEntityLocations", c) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@Kind", SqlDbType.NVarChar, 50).Value = kind.ToString();
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
            cmd.Parameters.Add("@Guid", SqlDbType.UniqueIdentifier).Value = (object?)guid ?? DBNull.Value;
            cmd.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = (object?)email ?? DBNull.Value;

            await using var r = await cmd.ExecuteReaderAsync();
            var found = false;
            var locations = new HashSet<int>();
            do
            {
                while (await r.ReadAsync())
                {
                    found = true;
                    if (!r.IsDBNull(0)) locations.Add(r.GetInt32(0));
                }
            } while (await r.NextResultAsync());
            return found ? locations.ToList() : null;
        }

        public async Task<int?> GetPaymentIdByPublicIdAsync(Guid publicId)
        {
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_RecordScope_GetPaymentIdByGuid", c) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@Guid", SqlDbType.UniqueIdentifier).Value = publicId;
            return await cmd.ExecuteScalarAsync() is int id ? id : null;
        }

        public async Task<HashSet<int>> GetPaymentIdsInLocationsAsync(IEnumerable<int> locationIds)
        {
            var ids = locationIds.Distinct().ToList();
            var result = new HashSet<int>();
            if (ids.Count == 0) return result;
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_RecordScope_GetPaymentIdsInLocations", c) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = string.Join(",", ids);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) result.Add(r.GetInt32(0));
            return result;
        }
    }
}
