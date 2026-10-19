using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.ServiceSwitch;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Background service switches: dbo.WN_ServiceSwitches_GetAll / dbo.WN_ServiceSwitches_Set (script
    /// SS_01_service_switches). Own connection (singleton; used by the hosted services, outside any request). Before a
    /// change it sets the audit SESSION_CONTEXT (AppUserId, AppUserEmail, SourceApp = 'WebAPI') like DbRepository.Open(),
    /// so dbo.TR_WN_ServiceSwitches_Audit records who switched it. SQL errors 2812 / 208 (procedure / table missing) mean
    /// SS_01 has not run: <see cref="ServiceSwitchesMissingException"/>.
    /// </summary>
    public class ServiceSwitchRepository : IServiceSwitchRepository
    {
        private const int CommandTimeoutSeconds = 15; // the hosted services wait for this read at the top of a cycle

        private readonly string _connectionString;

        public ServiceSwitchRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        public async Task<IReadOnlyList<ServiceSwitchRow>> GetAllAsync(CancellationToken ct = default)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync(ct);
                await using var cmd = new SqlCommand("dbo.WN_ServiceSwitches_GetAll", c)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = CommandTimeoutSeconds
                };
                await using var r = await cmd.ExecuteReaderAsync(ct);
                var rows = new List<ServiceSwitchRow>();
                while (await r.ReadAsync(ct)) rows.Add(Map(r));
                return rows;
            }
            catch (SqlException ex) when (ex.Number is 2812 or 208) // procedure / table missing: SS_01 not run
            {
                throw new ServiceSwitchesMissingException(ex);
            }
        }

        public async Task<ServiceSwitchRow?> SetAsync(string serviceName, bool isEnabled, string? updatedBy, CancellationToken ct = default)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync(ct);
                await SetAuditContextAsync(c, updatedBy, ct);

                await using var cmd = new SqlCommand("dbo.WN_ServiceSwitches_Set", c)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = CommandTimeoutSeconds
                };
                cmd.Parameters.Add("@ServiceName", SqlDbType.NVarChar, 100).Value = serviceName;
                cmd.Parameters.Add("@IsEnabled", SqlDbType.Bit).Value = isEnabled;
                cmd.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 100).Value = (object?)Truncate(updatedBy, 100) ?? DBNull.Value;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                return await r.ReadAsync(ct) ? Map(r) : null;
            }
            catch (SqlException ex) when (ex.Number is 2812 or 208) // procedure / table missing: SS_01 not run
            {
                throw new ServiceSwitchesMissingException(ex);
            }
        }

        /// <summary>
        /// The audit identity for dbo.WN_AuditLog, as DbRepository.Open() sets it from the JWT: AppUserId (the WN_Users row
        /// with this e-mail, when there is one), AppUserEmail and SourceApp = 'WebAPI'. A failure here never blocks the change.
        /// </summary>
        private static async Task SetAuditContextAsync(SqlConnection c, string? userEmail, CancellationToken ct)
        {
            try
            {
                await using var setCtx = new SqlCommand(@"
                    SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
                    DECLARE @ResolvedId INT = NULL;
                    IF @UserEmail IS NOT NULL
                        SELECT TOP 1 @ResolvedId = Id FROM dbo.WN_Users WITH (NOLOCK) WHERE Email = @UserEmail;
                    IF @ResolvedId IS NOT NULL
                        EXEC sp_set_session_context @key = N'AppUserId', @value = @ResolvedId;
                    IF @UserEmail IS NOT NULL
                        EXEC sp_set_session_context @key = N'AppUserEmail', @value = @UserEmail;
                    EXEC sp_set_session_context @key = N'SourceApp', @value = N'WebAPI';", c);
                setCtx.Parameters.Add("@UserEmail", SqlDbType.NVarChar, 256).Value = (object?)userEmail ?? DBNull.Value;
                await setCtx.ExecuteNonQueryAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Same as DbRepository.Open(): the audit context never blocks the change itself.
            }
        }

        private static ServiceSwitchRow Map(SqlDataReader r) => new()
        {
            ServiceName = Convert.ToString(r["ServiceName"]) ?? string.Empty,
            IsEnabled = r["IsEnabled"] is not DBNull && Convert.ToBoolean(r["IsEnabled"]),
            UpdatedBy = r["UpdatedBy"] is DBNull ? null : Convert.ToString(r["UpdatedBy"]),
            // DATETIME2 written with SYSUTCDATETIME(): UTC (JSON "Z").
            UpdatedAt = r["UpdatedAt"] is DBNull ? null : DateTime.SpecifyKind(Convert.ToDateTime(r["UpdatedAt"]), DateTimeKind.Utc)
        };

        private static string? Truncate(string? value, int max) =>
            value == null ? null : value.Length <= max ? value : value[..max];
    }
}
