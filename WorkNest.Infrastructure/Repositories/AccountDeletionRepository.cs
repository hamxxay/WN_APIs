using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Account;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Self-service account deletion: dbo.WN_Account_Delete (script QA_04_account_delete), which checks and, unless
    /// @CheckOnly = 1, anonymises the customer account in one transaction and records it in dbo.WN_AuditLog itself (it
    /// sets the audit session context, so this uses its own connection). SQL error 2812 (procedure missing) means QA_04
    /// has not run: <see cref="AccountDeletionUnavailableException"/>.
    /// </summary>
    public class AccountDeletionRepository : IAccountDeletionRepository
    {
        private readonly string _connectionString;

        public AccountDeletionRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        public async Task<AccountDeletionRow> RunAsync(int userId, bool checkOnly, DateTime today, CancellationToken ct = default)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync(ct);
                await using var cmd = new SqlCommand("dbo.WN_Account_Delete", c)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 120 // the deletion also cleans the user's rows in the audit log
                };
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                cmd.Parameters.Add("@CheckOnly", SqlDbType.Bit).Value = checkOnly;
                cmd.Parameters.Add("@Today", SqlDbType.Date).Value = today.Date;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct))
                    throw new InvalidOperationException("dbo.WN_Account_Delete returned no row.");
                return new AccountDeletionRow
                {
                    UserId = r["UserId"] is DBNull ? userId : Convert.ToInt32(r["UserId"]),
                    CanDelete = r["CanDelete"] is not DBNull && Convert.ToBoolean(r["CanDelete"]),
                    Deleted = r["Deleted"] is not DBNull && Convert.ToBoolean(r["Deleted"]),
                    ReasonCode = r["ReasonCode"] is DBNull ? null : Convert.ToString(r["ReasonCode"]),
                    ActiveBookings = r["ActiveBookings"] is DBNull ? 0 : Convert.ToInt32(r["ActiveBookings"]),
                    PendingBookings = r["PendingBookings"] is DBNull ? 0 : Convert.ToInt32(r["PendingBookings"]),
                    UnpaidInvoices = r["UnpaidInvoices"] is DBNull ? 0 : Convert.ToInt32(r["UnpaidInvoices"]),
                    BalanceDue = r["BalanceDue"] is DBNull ? 0m : Convert.ToDecimal(r["BalanceDue"])
                };
            }
            catch (SqlException ex) when (ex.Number == 2812) // Could not find stored procedure: QA_04 not run yet
            {
                throw new AccountDeletionUnavailableException(ex);
            }
        }
    }
}
