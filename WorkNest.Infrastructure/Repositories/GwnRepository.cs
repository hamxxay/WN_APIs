using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Gwn;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Grandstream tables of script GWN_01_grandstream: dbo.WN_GWN_NetworkLocations (network → WorkNest locations),
    /// dbo.WN_GWN_ClientAliases (client names), dbo.WN_GWN_ActionLog (block / unblock / reboot). Plain parameterised
    /// T-SQL. SQL error 208 (table missing) = GWN_01 not run: <see cref="GwnTablesMissingException"/>.
    /// </summary>
    public sealed class GwnRepository : IGwnRepository
    {
        private const int CommandTimeoutSeconds = 15;
        private readonly string _connectionString;

        public GwnRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private async Task<SqlConnection> OpenAsync(CancellationToken ct)
        {
            var c = new SqlConnection(_connectionString);
            await c.OpenAsync(ct);
            return c;
        }

        private static SqlCommand Cmd(string sql, SqlConnection c, SqlTransaction? tx = null) =>
            new(sql, c, tx) { CommandTimeout = CommandTimeoutSeconds };

        private static async Task<T> Guard<T>(Func<Task<T>> action)
        {
            try { return await action(); }
            catch (SqlException ex) when (ex.Number == 208) { throw new GwnTablesMissingException(ex); }
        }

        private static object Db(string? value, int max) =>
            string.IsNullOrWhiteSpace(value) ? DBNull.Value : (value.Length > max ? value[..max] : value);

        public Task<IReadOnlyList<(long NetworkId, int LocationId, string? NetworkName)>> GetNetworkLocationsAsync(CancellationToken ct = default) =>
            Guard<IReadOnlyList<(long, int, string?)>>(async () =>
            {
                await using var c = await OpenAsync(ct);
                await using var cmd = Cmd("SELECT NetworkId, LocationId, NetworkName FROM dbo.WN_GWN_NetworkLocations WITH (NOLOCK) ORDER BY NetworkId, LocationId;", c);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                var list = new List<(long, int, string?)>();
                while (await r.ReadAsync(ct))
                    list.Add((Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture), Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                        r.IsDBNull(2) ? null : r.GetString(2)));
                return list;
            });

        public Task SetNetworkLocationsAsync(long networkId, string? networkName, IReadOnlyCollection<int> locationIds, string? updatedBy, CancellationToken ct = default) =>
            Guard(async () =>
            {
                await using var c = await OpenAsync(ct);
                await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
                await using (var del = Cmd("DELETE FROM dbo.WN_GWN_NetworkLocations WHERE NetworkId = @NetworkId;", c, tx))
                {
                    del.Parameters.Add("@NetworkId", SqlDbType.BigInt).Value = networkId;
                    await del.ExecuteNonQueryAsync(ct);
                }
                foreach (var locationId in locationIds.Where(id => id > 0).Distinct())
                {
                    await using var ins = Cmd(@"INSERT INTO dbo.WN_GWN_NetworkLocations (NetworkId, LocationId, NetworkName, UpdatedBy, UpdatedAt)
                                                VALUES (@NetworkId, @LocationId, @NetworkName, @UpdatedBy, SYSUTCDATETIME());", c, tx);
                    ins.Parameters.Add("@NetworkId", SqlDbType.BigInt).Value = networkId;
                    ins.Parameters.Add("@LocationId", SqlDbType.Int).Value = locationId;
                    ins.Parameters.Add("@NetworkName", SqlDbType.NVarChar, 200).Value = Db(networkName, 200);
                    ins.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 256).Value = Db(updatedBy, 256);
                    await ins.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
                return true;
            });

        public Task<Dictionary<string, string>> GetAliasesAsync(CancellationToken ct = default) => Guard(async () =>
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = Cmd("SELECT Mac, Alias FROM dbo.WN_GWN_ClientAliases WITH (NOLOCK);", c);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await r.ReadAsync(ct))
                if (!r.IsDBNull(0) && !r.IsDBNull(1)) map[r.GetString(0).Trim()] = r.GetString(1);
            return map;
        });

        public Task SetAliasAsync(string mac, string alias, string? updatedBy, CancellationToken ct = default) => Guard(async () =>
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = Cmd(@"
                UPDATE dbo.WN_GWN_ClientAliases SET Alias = @Alias, UpdatedBy = @By, UpdatedAt = SYSUTCDATETIME() WHERE Mac = @Mac;
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.WN_GWN_ClientAliases (Mac, Alias, UpdatedBy, UpdatedAt) VALUES (@Mac, @Alias, @By, SYSUTCDATETIME());", c);
            cmd.Parameters.Add("@Mac", SqlDbType.Char, 17).Value = mac;
            cmd.Parameters.Add("@Alias", SqlDbType.NVarChar, 100).Value = alias.Length > 100 ? alias[..100] : alias;
            cmd.Parameters.Add("@By", SqlDbType.NVarChar, 256).Value = Db(updatedBy, 256);
            await cmd.ExecuteNonQueryAsync(ct);
            return true;
        });

        public Task DeleteAliasAsync(string mac, CancellationToken ct = default) => Guard(async () =>
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = Cmd("DELETE FROM dbo.WN_GWN_ClientAliases WHERE Mac = @Mac;", c);
            cmd.Parameters.Add("@Mac", SqlDbType.Char, 17).Value = mac;
            await cmd.ExecuteNonQueryAsync(ct);
            return true;
        });

        public Task AddActionAsync(GwnActionLogEntry entry, CancellationToken ct = default) => Guard(async () =>
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = Cmd(@"
                INSERT INTO dbo.WN_GWN_ActionLog (NetworkId, Action, Target, ByEmail, ByUserId, Ok, Message)
                VALUES (@NetworkId, @Action, @Target, @ByEmail, @ByUserId, @Ok, @Message);", c);
            cmd.Parameters.Add("@NetworkId", SqlDbType.BigInt).Value = entry.NetworkId;
            cmd.Parameters.Add("@Action", SqlDbType.NVarChar, 30).Value = entry.Action;
            cmd.Parameters.Add("@Target", SqlDbType.NVarChar, 64).Value = entry.Target.Length > 64 ? entry.Target[..64] : entry.Target;
            cmd.Parameters.Add("@ByEmail", SqlDbType.NVarChar, 256).Value = Db(entry.ByEmail, 256);
            cmd.Parameters.Add("@ByUserId", SqlDbType.Int).Value = (object?)entry.ByUserId ?? DBNull.Value;
            cmd.Parameters.Add("@Ok", SqlDbType.Bit).Value = entry.Ok;
            cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 500).Value = Db(entry.Message, 500);
            await cmd.ExecuteNonQueryAsync(ct);
            return true;
        });

        public Task<List<GwnActionLogDto>> GetActionsAsync(long? networkId, int top, CancellationToken ct = default) => Guard(async () =>
        {
            await using var c = await OpenAsync(ct);
            await using var cmd = Cmd(@"
                SELECT TOP (@Top) Id, NetworkId, Action, Target, ByEmail, ByUserId, Ok, Message, CreatedAt
                  FROM dbo.WN_GWN_ActionLog WITH (NOLOCK)
                 WHERE @NetworkId IS NULL OR NetworkId = @NetworkId
                 ORDER BY Id DESC;", c);
            cmd.Parameters.Add("@Top", SqlDbType.Int).Value = Math.Clamp(top, 1, 500);
            cmd.Parameters.Add("@NetworkId", SqlDbType.BigInt).Value = (object?)networkId ?? DBNull.Value;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var list = new List<GwnActionLogDto>();
            while (await r.ReadAsync(ct))
            {
                list.Add(new GwnActionLogDto
                {
                    Id = Convert.ToInt64(r["Id"], CultureInfo.InvariantCulture),
                    NetworkId = r["NetworkId"] is DBNull ? 0 : Convert.ToInt64(r["NetworkId"], CultureInfo.InvariantCulture),
                    Action = Convert.ToString(r["Action"], CultureInfo.InvariantCulture) ?? "",
                    Target = r["Target"] is DBNull ? "" : Convert.ToString(r["Target"], CultureInfo.InvariantCulture) ?? "",
                    ByEmail = r["ByEmail"] is DBNull ? null : Convert.ToString(r["ByEmail"], CultureInfo.InvariantCulture),
                    ByUserId = r["ByUserId"] is DBNull ? null : Convert.ToInt32(r["ByUserId"], CultureInfo.InvariantCulture),
                    Ok = r["Ok"] is not DBNull && Convert.ToBoolean(r["Ok"], CultureInfo.InvariantCulture),
                    Message = r["Message"] is DBNull ? null : Convert.ToString(r["Message"], CultureInfo.InvariantCulture),
                    CreatedAt = r["CreatedAt"] is DateTime t ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture) : null
                });
            }
            return list;
        });

        private static async Task Guard(Func<Task<bool>> action) => await Guard<bool>(action);
    }
}
