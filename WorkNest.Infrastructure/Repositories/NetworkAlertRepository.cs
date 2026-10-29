using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Network alert tables and procedures of script NET_ALERT_01_network_alerts (dbo.WN_NetworkAlert_*). Raw SqlClient,
    /// one procedure per call. SQL error 208 / 2812 (table / procedure missing) = the script has not run:
    /// <see cref="NetworkAlertsMissingException"/>.
    /// </summary>
    public sealed class NetworkAlertRepository : INetworkAlertRepository
    {
        private const int CommandTimeoutSeconds = 15;
        private readonly string _connectionString;

        public NetworkAlertRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private async Task<T> Run<T>(string procedure, Func<SqlCommand, CancellationToken, Task<T>> read, CancellationToken ct,
            params (string Name, SqlDbType Type, object? Value)[] ps)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync(ct);
                await using var cmd = new SqlCommand(procedure, c) { CommandType = CommandType.StoredProcedure, CommandTimeout = CommandTimeoutSeconds };
                foreach (var (name, type, value) in ps) cmd.Parameters.Add(name, type).Value = value ?? DBNull.Value;
                return await read(cmd, ct);
            }
            catch (SqlException ex) when (ex.Number is 208 or 2812) { throw new NetworkAlertsMissingException(ex); }
        }

        private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
        private static DateTime? Utc(IDataRecord r, string col) => r[col] is DateTime d ? Utc(d) : null;
        private static string? Str(IDataRecord r, string col) => r[col] is string s ? s : null;

        private static async Task<List<T>> Rows<T>(SqlCommand cmd, Func<IDataRecord, T> map, CancellationToken ct)
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var list = new List<T>();
            while (await r.ReadAsync(ct)) list.Add(map(r));
            return list;
        }

        private static NetworkAlertRecipient Recipient(IDataRecord r) =>
            new((int)r["Id"], (int)r["LocationId"], Str(r, "LocationName"), (string)r["Email"], (bool)r["IsActive"], (DateTime)r["CreatedOn"], Str(r, "CreatedBy"));

        public async Task<IReadOnlyList<NetworkAlertRecipient>> GetRecipientsAsync(int? locationId = null, CancellationToken ct = default) =>
            await Run("dbo.WN_NetworkAlert_Recipients_Get", (c, t) => Rows(c, Recipient, t), ct, ("@LocationId", SqlDbType.Int, locationId));

        public async Task<NetworkAlertRecipient> AddRecipientAsync(int locationId, string email, string? createdBy, CancellationToken ct = default)
        {
            try
            {
                return (await Run("dbo.WN_NetworkAlert_Recipient_Add", (c, t) => Rows(c, Recipient, t), ct,
                    ("@LocationId", SqlDbType.Int, locationId), ("@Email", SqlDbType.NVarChar, email), ("@CreatedBy", SqlDbType.NVarChar, createdBy))).First();
            }
            catch (SqlException ex) when (ex.Number == 50011) { throw new NetworkAlertRecipientException("Location not found.", false); }
            catch (SqlException ex) when (ex.Number is 50010 or 2601 or 2627) { throw new NetworkAlertRecipientException("This e-mail is already in the list.", true); }
        }

        public async Task<bool> SetRecipientActiveAsync(int id, bool isActive, CancellationToken ct = default) =>
            await Run("dbo.WN_NetworkAlert_Recipient_Set", async (c, t) => Convert.ToInt32(await c.ExecuteScalarAsync(t)) > 0, ct,
                ("@Id", SqlDbType.Int, id), ("@IsActive", SqlDbType.Bit, isActive));

        public async Task<bool> DeleteRecipientAsync(int id, CancellationToken ct = default) =>
            await Run("dbo.WN_NetworkAlert_Recipient_Delete", async (c, t) => Convert.ToInt32(await c.ExecuteScalarAsync(t)) > 0, ct,
                ("@Id", SqlDbType.Int, id));

        public async Task<IReadOnlyList<NetworkAlertLocation>> GetActiveLocationsAsync(CancellationToken ct = default)
        {
            var rows = await Run("dbo.WN_NetworkAlert_Locations_GetActive",
                (c, t) => Rows(c, r => (Id: (int)r["LocationId"], Name: (string)r["LocationName"], Email: (string)r["Email"]), t), ct);
            return rows.GroupBy(r => r.Id).Select(g => new NetworkAlertLocation(g.Key, g.First().Name, g.Select(x => x.Email).ToList())).ToList();
        }

        public async Task<NetworkAlertClaim> ClaimAsync(int locationId, int leaseSeconds, int minGapSeconds, CancellationToken ct = default) =>
            (await Run("dbo.WN_NetworkAlert_Claim", (c, t) => Rows(c, r => new NetworkAlertClaim((bool)r["Claimed"], Utc((DateTime)r["DbNow"])), t), ct,
                ("@LocationId", SqlDbType.Int, locationId), ("@LeaseSeconds", SqlDbType.Int, leaseSeconds), ("@MinGapSeconds", SqlDbType.Int, minGapSeconds))).First();

        public async Task ReleaseAsync(int locationId, CancellationToken ct = default) =>
            await Run("dbo.WN_NetworkAlert_Release", async (c, t) => await c.ExecuteNonQueryAsync(t), ct, ("@LocationId", SqlDbType.Int, locationId));

        public async Task<IReadOnlyList<NetworkAlertHikDevice>> GetHikDevicesAsync(int locationId, CancellationToken ct = default) =>
            await Run("dbo.WN_NetworkAlert_HikDevices_Get",
                (c, t) => Rows(c, r => new NetworkAlertHikDevice((int)r["Id"], (string)r["Name"], Str(r, "Host"), (bool)r["Online"], r["LastSeen"] as DateTime?), t),
                ct, ("@LocationId", SqlDbType.Int, locationId));

        public async Task<IReadOnlyList<NetworkAlertState>> GetStatesAsync(int locationId, CancellationToken ct = default) =>
            await Run("dbo.WN_NetworkAlert_State_Get",
                (c, t) => Rows(c, r => new NetworkAlertState((string)r["Kind"], (string)r["ItemKey"], (string)r["Name"], Str(r, "Detail"), Utc((DateTime)r["DownSince"]),
                    Utc(r, "FirstAlertAt"), Utc(r, "LastReminderAt"), Utc(r, "RecoveredAt")), t),
                ct, ("@LocationId", SqlDbType.Int, locationId));

        public async Task SaveStateAsync(int locationId, NetworkAlertState s, CancellationToken ct = default) =>
            await Run("dbo.WN_NetworkAlert_State_Save", async (c, t) => await c.ExecuteNonQueryAsync(t), ct,
                ("@Kind", SqlDbType.NVarChar, s.Kind), ("@ItemKey", SqlDbType.NVarChar, Cut(s.ItemKey, 200)), ("@LocationId", SqlDbType.Int, locationId),
                ("@Name", SqlDbType.NVarChar, Cut(s.Name, 200)), ("@Detail", SqlDbType.NVarChar, Cut(s.Detail, 500)), ("@DownSince", SqlDbType.DateTime2, s.DownSince),
                ("@FirstAlertAt", SqlDbType.DateTime2, s.FirstAlertAt), ("@LastReminderAt", SqlDbType.DateTime2, s.LastReminderAt),
                ("@RecoveredAt", SqlDbType.DateTime2, s.RecoveredAt));

        public async Task AddLogAsync(NetworkAlertLogEntry e, CancellationToken ct = default) =>
            await Run("dbo.WN_NetworkAlert_Log_Add", async (c, t) => await c.ExecuteNonQueryAsync(t), ct,
                ("@LocationId", SqlDbType.Int, e.LocationId), ("@Kind", SqlDbType.NVarChar, e.Kind), ("@Subject", SqlDbType.NVarChar, e.Subject),
                ("@Summary", SqlDbType.NVarChar, e.Summary), ("@Recipients", SqlDbType.NVarChar, e.Recipients), ("@Status", SqlDbType.NVarChar, e.Status),
                ("@Error", SqlDbType.NVarChar, e.Error));

        public async Task<IReadOnlyList<NetworkAlertLogRow>> GetLogAsync(int top, CancellationToken ct = default) =>
            await Run("dbo.WN_NetworkAlert_Log_Get",
                (c, t) => Rows(c, r => new NetworkAlertLogRow((int)r["Id"], (int)r["LocationId"], Str(r, "LocationName"), (string)r["Kind"], (string)r["Subject"],
                    (string)r["Summary"], (string)r["Recipients"], (string)r["Status"], Str(r, "Error"), Utc((DateTime)r["SentAt"])), t),
                ct, ("@Top", SqlDbType.Int, top));

        private static string? Cut(string? s, int max) => s == null ? null : (s.Length > max ? s[..max] : s);
    }
}
