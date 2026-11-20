using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.SecurityDeposit;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Adjust Security Deposit Against Invoice: calls the new dbo.WN_SDA_* procedures. Posting and every rule live in
    /// SQL (one transaction). SQL errors raised with RAISERROR severity 16 map by state: 1 rule, 2 not found, 3 location.
    /// </summary>
    public class SecurityDepositAdjustmentRepository : ISecurityDepositAdjustmentRepository
    {
        private readonly string _connectionString;

        public SecurityDepositAdjustmentRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        public Task<List<SdaResultSet>> GetContextAsync(int bookingId, string? allowed) =>
            ExecAsync("dbo.WN_SDA_Context", null, c =>
            {
                c.Parameters.Add("@BookingId", SqlDbType.Int).Value = bookingId;
                c.Parameters.Add("@AllowedLocationIds", SqlDbType.NVarChar, -1).Value = (object?)allowed ?? DBNull.Value;
            });

        public Task<List<SdaResultSet>> ListAsync(int? bookingId, string? status, string? allowed) =>
            ExecAsync("dbo.WN_SDA_Requests_List", null, c =>
            {
                c.Parameters.Add("@BookingId", SqlDbType.Int).Value = (object?)bookingId ?? DBNull.Value;
                c.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = string.IsNullOrWhiteSpace(status) ? DBNull.Value : status!.Trim();
                c.Parameters.Add("@AllowedLocationIds", SqlDbType.NVarChar, -1).Value = (object?)allowed ?? DBNull.Value;
            });

        public Task<List<SdaResultSet>> CreateAsync(SecurityDepositAdjustmentCreateDto dto, int userId, string? allowed) =>
            ExecAsync("dbo.WN_SDA_Request_Create", userId, c =>
            {
                c.Parameters.Add("@BookingId", SqlDbType.Int).Value = dto.BookingId;
                c.Parameters.Add("@InvoiceId", SqlDbType.Int).Value = dto.InvoiceId;
                c.Parameters.Add("@TopUpDueOn", SqlDbType.Date).Value = dto.TopUpDueOn.HasValue ? dto.TopUpDueOn.Value.Date : DBNull.Value;
                c.Parameters.Add("@Remarks", SqlDbType.NVarChar, 500).Value = (object?)dto.Remarks ?? DBNull.Value;
                c.Parameters.Add("@RequestedById", SqlDbType.Int).Value = userId;
                c.Parameters.Add("@AllowedLocationIds", SqlDbType.NVarChar, -1).Value = (object?)allowed ?? DBNull.Value;
            });

        public Task<List<SdaResultSet>> DecideAsync(int id, bool approve, string? note, int userId, string? allowed) =>
            ExecAsync("dbo.WN_SDA_Request_Decide", userId, c =>
            {
                c.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                c.Parameters.Add("@Approve", SqlDbType.Bit).Value = approve;
                c.Parameters.Add("@DecidedById", SqlDbType.Int).Value = userId;
                c.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = (object?)note ?? DBNull.Value;
                c.Parameters.Add("@AllowedLocationIds", SqlDbType.NVarChar, -1).Value = (object?)allowed ?? DBNull.Value;
            });

        public Task<List<SdaResultSet>> GetNoticeDepositAsync(int noticeId) =>
            ExecAsync("dbo.WN_SDA_NoticeDeposit", null, c => c.Parameters.Add("@NoticeId", SqlDbType.Int).Value = noticeId);

        private async Task<List<SdaResultSet>> ExecAsync(string proc, int? auditUserId, Action<SqlCommand> bind)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                if (auditUserId.HasValue)
                {
                    // The audit triggers read these (as DbRepository.Open() sets them).
                    await using var ctx = new SqlCommand(
                        "EXEC sp_set_session_context N'AppUserId', @U; EXEC sp_set_session_context N'SourceApp', N'WebAPI'; " +
                        "DECLARE @E NVARCHAR(256) = (SELECT Email FROM dbo.WN_Users WITH (NOLOCK) WHERE Id = @U); " +
                        "IF @E IS NOT NULL EXEC sp_set_session_context N'AppUserEmail', @E;", conn);
                    ctx.Parameters.Add("@U", SqlDbType.Int).Value = auditUserId.Value;
                    await ctx.ExecuteNonQueryAsync();
                }

                await using var cmd = new SqlCommand(proc, conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
                bind(cmd);

                var sets = new List<SdaResultSet>();
                await using var r = await cmd.ExecuteReaderAsync();
                do
                {
                    var set = new SdaResultSet();
                    while (await r.ReadAsync())
                    {
                        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        for (var i = 0; i < r.FieldCount; i++)
                        {
                            var name = r.GetName(i);
                            // Dictionary keys are not run through the camelCase naming policy: do it here (BookingId -> bookingId).
                            row[char.ToLowerInvariant(name[0]) + name[1..]] = await r.IsDBNullAsync(i) ? null : r.GetValue(i);
                        }
                        set.Add(row);
                    }
                    sets.Add(set);
                } while (await r.NextResultAsync());
                return sets;
            }
            catch (SqlException ex) when (ex.Number == 2812 || ex.Number == 208 || ex.Number == 4121)
            {
                throw new SecurityDepositAdjustmentException(SdaOutcome.BadRequest,
                    "Adjust Security Deposit is not set up on this database yet (run SDA_01_adjust_deposit_against_invoice.sql).");
            }
            catch (SqlException ex) when (ex.Class == 16 && ex.Number == 50000)
            {
                var outcome = ex.State switch { 2 => SdaOutcome.NotFound, 3 => SdaOutcome.Forbidden, _ => SdaOutcome.BadRequest };
                throw new SecurityDepositAdjustmentException(outcome, ex.Message);
            }
        }
    }
}
