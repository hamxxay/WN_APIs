using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Discounts section of the CEO dashboard: calls the read-only procedure dbo.WN_Ceo_Discounts (script
    /// DD_01_ceo_discounts). Until DD_01 has run the procedure is missing (SQL error 2812) and the call throws
    /// <see cref="CeoDiscountsUpdateMissingException"/>. A custom range goes as @From / @To (the procedure has them from
    /// the start).
    /// </summary>
    public class CeoDiscountsRepository : ICeoDiscountsRepository
    {
        private readonly string _connectionString;

        public CeoDiscountsRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private static object Db(object? value) => value ?? DBNull.Value;

        public async Task<List<List<IDictionary<string, object?>>>> GetDiscountsAsync(int? locationId, int periodMonths, DateTime businessNow,
            IReadOnlyCollection<int> voidStatusIds, DateTime? from = null, DateTime? to = null)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                await using var cmd = new SqlCommand("dbo.WN_Ceo_Discounts", c) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
                cmd.Parameters.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                cmd.Parameters.Add("@PMonths", SqlDbType.Int).Value = periodMonths;
                cmd.Parameters.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                cmd.Parameters.Add("@From", SqlDbType.Date).Value = from != null && to != null ? from.Value.Date : DBNull.Value;
                cmd.Parameters.Add("@To", SqlDbType.Date).Value = from != null && to != null ? to.Value.Date : DBNull.Value;
                cmd.Parameters.Add("@VoidStatusIds", SqlDbType.NVarChar, 400).Value = string.Join(",", voidStatusIds.Distinct());
                await using var r = await cmd.ExecuteReaderAsync();
                var sets = new List<List<IDictionary<string, object?>>>();
                do
                {
                    var rows = new List<IDictionary<string, object?>>();
                    while (await r.ReadAsync())
                    {
                        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                        rows.Add(row);
                    }
                    sets.Add(rows);
                } while (await r.NextResultAsync());
                return sets;
            }
            catch (SqlException ex) when (ex.Number == 2812) // Could not find stored procedure: DD_01 not run yet
            {
                throw new CeoDiscountsUpdateMissingException(ex);
            }
        }
    }
}
