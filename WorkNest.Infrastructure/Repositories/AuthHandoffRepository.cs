using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Cross-site sign-in codes: dbo.WN_AuthHandoff_Create / dbo.WN_AuthHandoff_Redeem (script
    /// Database/Auth/AUTH_HANDOFF_01_login_handoff). Own connection, no audit SESSION_CONTEXT (the rows are hashes that live
    /// a minute). SQL errors 2812 / 208 (procedure / table missing) mean AUTH_HANDOFF_01 has not run:
    /// <see cref="AuthHandoffMissingException"/>.
    /// </summary>
    public class AuthHandoffRepository : IAuthHandoffRepository
    {
        private const int CommandTimeoutSeconds = 15;

        private readonly string _connectionString;

        public AuthHandoffRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        public async Task CreateAsync(int userId, byte[] codeHash, string target, int ttlSeconds, CancellationToken ct = default)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync(ct);
                await using var cmd = Proc("dbo.WN_AuthHandoff_Create", c);
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                cmd.Parameters.Add("@CodeHash", SqlDbType.Binary, 32).Value = codeHash;
                cmd.Parameters.Add("@Target", SqlDbType.VarChar, 10).Value = target;
                cmd.Parameters.Add("@TtlSeconds", SqlDbType.Int).Value = ttlSeconds;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (SqlException ex) when (ex.Number is 2812 or 208) // procedure / table missing: AUTH_HANDOFF_01 not run
            {
                throw new AuthHandoffMissingException(ex);
            }
        }

        public async Task<AuthHandoffRedemption?> RedeemAsync(byte[] codeHash, CancellationToken ct = default)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync(ct);
                await using var cmd = Proc("dbo.WN_AuthHandoff_Redeem", c);
                cmd.Parameters.Add("@CodeHash", SqlDbType.Binary, 32).Value = codeHash;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) return null;
                return new AuthHandoffRedemption(
                    Convert.ToInt32(r["UserId"]),
                    Convert.ToString(r["Target"]) ?? string.Empty,
                    // DATETIME2 written with SYSUTCDATETIME(): UTC.
                    DateTime.SpecifyKind(Convert.ToDateTime(r["ExpiresAt"]), DateTimeKind.Utc),
                    DateTime.SpecifyKind(Convert.ToDateTime(r["UsedAt"]), DateTimeKind.Utc),
                    r["Email"] is DBNull ? null : Convert.ToString(r["Email"]),
                    r["IsActive"] is not DBNull && Convert.ToBoolean(r["IsActive"]));
            }
            catch (SqlException ex) when (ex.Number is 2812 or 208) // procedure / table missing: AUTH_HANDOFF_01 not run
            {
                throw new AuthHandoffMissingException(ex);
            }
        }

        private static SqlCommand Proc(string name, SqlConnection c) => new(name, c)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = CommandTimeoutSeconds
        };
    }
}
