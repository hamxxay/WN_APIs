using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// CEO dashboard Seats section: calls the read-only procedure dbo.WN_Ceo_Seats (script SEAT_01_ceo_seats). Until
    /// SEAT_01 has run the procedure is missing (SQL error 2812) and the call throws
    /// <see cref="CeoSeatsDatabaseUpdateMissingException"/>, which api/ceo/seats answers with available = false.
    /// </summary>
    public class CeoSeatsRepository : ICeoSeatsRepository
    {
        private readonly string _connectionString;

        public CeoSeatsRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        public async Task<List<List<IDictionary<string, object?>>>> GetSeatsAsync(int? locationId, int periodMonths, DateTime businessNow,
            DateTime? from = null, DateTime? to = null)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                await using var cmd = new SqlCommand("dbo.WN_Ceo_Seats", c) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
                cmd.Parameters.Add("@LocationId", SqlDbType.Int).Value = locationId is > 0 ? (object)locationId.Value : DBNull.Value;
                cmd.Parameters.Add("@PMonths", SqlDbType.Int).Value = periodMonths;
                cmd.Parameters.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                // A custom range only when both days are given (the calendar periods send none, as the other CEO calls).
                cmd.Parameters.Add("@From", SqlDbType.Date).Value = from != null && to != null ? (object)from.Value.Date : DBNull.Value;
                cmd.Parameters.Add("@To", SqlDbType.Date).Value = from != null && to != null ? (object)to.Value.Date : DBNull.Value;
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
            catch (SqlException ex) when (ex.Number == 2812) // Could not find stored procedure: SEAT_01 not run yet
            {
                throw new CeoSeatsDatabaseUpdateMissingException(ex);
            }
        }
    }
}
