using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Temporary passwords: dbo.WN_Customers_LoginStatus, WN_Customers_EnsureLoginUser, WN_Users_SetTempPassword,
    /// WN_Users_ChangePassword, WN_Users_ActivateLogin (script Database/Auth/AUTH_TEMP_01_customer_temp_passwords).
    /// SQL errors 2812 / 207 / 208 (procedure / column / table missing) mean AUTH_TEMP_01 has not run:
    /// <see cref="CustomerLoginMissingException"/>. Own connection; nothing here is logged (it handles passwords).
    /// </summary>
    public class CustomerLoginRepository : ICustomerLoginRepository
    {
        private readonly string _connectionString;

        public CustomerLoginRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private static string? S(object v) => v is DBNull ? null : Convert.ToString(v);
        private static bool B(object v) => v is not DBNull && Convert.ToBoolean(v);

        public Task<List<CustomerLoginInfo>> GetStatusAsync(IReadOnlyCollection<int>? customerIds, CancellationToken ct = default) =>
            Run(async c =>
            {
                await using var cmd = Proc("dbo.WN_Customers_LoginStatus", c);
                cmd.Parameters.Add("@CustomerIds", SqlDbType.NVarChar, -1).Value =
                    customerIds is null ? DBNull.Value : string.Join(",", customerIds);
                var list = new List<CustomerLoginInfo>();
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    list.Add(new CustomerLoginInfo(
                        Convert.ToInt32(r["CustomerId"]), S(r["FirstName"]), S(r["LastName"]), S(r["Company"]), S(r["Email"]),
                        B(r["CustomerActive"]),
                        r["UserId"] is DBNull ? null : Convert.ToInt32(r["UserId"]),
                        r["RoleId"] is DBNull ? null : Convert.ToInt32(r["RoleId"]),
                        B(r["UserActive"]), B(r["MustChangePassword"]), S(r["TempPassword"]), B(r["HasSignedIn"])));
                return list;
            });

        public Task<(int UserId, int? RoleId, bool IsActive)?> EnsureLoginUserAsync(int customerId, CancellationToken ct = default) =>
            Run(async c =>
            {
                await using var cmd = Proc("dbo.WN_Customers_EnsureLoginUser", c);
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct) || r["UserId"] is DBNull) return ((int, int?, bool)?)null;
                return (Convert.ToInt32(r["UserId"]), r["RoleId"] is DBNull ? null : Convert.ToInt32(r["RoleId"]), B(r["IsActive"]));
            });

        public Task<bool> SetTempPasswordAsync(int userId, string passwordHash, string tempPassword, CancellationToken ct = default) =>
            Run(async c =>
            {
                await using var cmd = Proc("dbo.WN_Users_SetTempPassword", c);
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                cmd.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 1000).Value = passwordHash;
                cmd.Parameters.Add("@TempPassword", SqlDbType.NVarChar, 64).Value = tempPassword;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                return await r.ReadAsync(ct) && Convert.ToInt32(r[0]) > 0;
            });

        public Task ChangePasswordAsync(int userId, string passwordHash, CancellationToken ct = default) =>
            Run(async c =>
            {
                await using var cmd = Proc("dbo.WN_Users_ChangePassword", c);
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                cmd.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 1000).Value = passwordHash;
                await cmd.ExecuteNonQueryAsync(ct);
                return true;
            });

        public Task ActivateAsync(int userId, CancellationToken ct = default) =>
            Run(async c =>
            {
                await using var cmd = Proc("dbo.WN_Users_ActivateLogin", c);
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                await cmd.ExecuteNonQueryAsync(ct);
                return true;
            });

        private async Task<T> Run<T>(Func<SqlConnection, Task<T>> body)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                return await body(c);
            }
            catch (SqlException ex) when (ex.Number is 2812 or 207 or 208)
            {
                throw new CustomerLoginMissingException(ex);
            }
        }

        private static SqlCommand Proc(string name, SqlConnection c) => new(name, c)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 30
        };
    }
}
