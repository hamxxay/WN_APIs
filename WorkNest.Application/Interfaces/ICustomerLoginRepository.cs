namespace WorkNest.Application.Interfaces
{
    /// <summary>One customer with the state of their login (dbo.WN_Customers_LoginStatus, script AUTH_TEMP_01).</summary>
    public sealed record CustomerLoginInfo(
        int CustomerId, string? FirstName, string? LastName, string? Company, string? Email, bool CustomerActive,
        int? UserId, int? RoleId, bool UserActive, bool MustChangePassword, string? TempPassword, bool HasSignedIn)
    {
        public const string None = "none", Pending = "pending", Active = "active";

        public string DisplayName
        {
            get
            {
                var n = $"{FirstName} {LastName}".Trim();
                return n.Length > 0 ? n : (Company ?? Email ?? $"Customer {CustomerId}");
            }
        }

        /// <summary>True when a login user exists and is a customer (general) account.</summary>
        public bool HasCustomerUser => UserId.HasValue && RoleId == WorkNest.Common.Constants.Roles.GeneralId;

        /// <summary>"pending" = temporary password handed out, not yet replaced; "active" = signed in, own password / Google; else "none".</summary>
        public string Status =>
            !HasCustomerUser ? None : MustChangePassword ? Pending : HasSignedIn ? Active : None;
    }

    /// <summary>dbo.WN_Users.MustChangePassword / TempPassword and their procedures are missing: AUTH_TEMP_01 has not run.</summary>
    public sealed class CustomerLoginMissingException : Exception
    {
        public const string Text = "Database update needed: run the script AUTH_TEMP_01_customer_temp_passwords.";
        public CustomerLoginMissingException(Exception inner) : base(Text, inner) { }
    }

    /// <summary>Temporary passwords for customers (script AUTH_TEMP_01). Every method throws <see cref="CustomerLoginMissingException"/> before it has run.</summary>
    public interface ICustomerLoginRepository
    {
        /// <summary>Login state of the given customers (null = every customer).</summary>
        Task<List<CustomerLoginInfo>> GetStatusAsync(IReadOnlyCollection<int>? customerIds, CancellationToken ct = default);
        /// <summary>The customer's login user: found by the customer's email, or created (general role) and linked. Null when the customer has no email.</summary>
        Task<(int UserId, int? RoleId, bool IsActive)?> EnsureLoginUserAsync(int customerId, CancellationToken ct = default);
        /// <summary>Stores the hash (PasswordHash) and the readable temporary password, sets MustChangePassword. False when the user is not a customer account.</summary>
        Task<bool> SetTempPasswordAsync(int userId, string passwordHash, string tempPassword, CancellationToken ct = default);
        /// <summary>The user chose a password: stores its hash, clears MustChangePassword and the readable temporary password.</summary>
        Task ChangePasswordAsync(int userId, string passwordHash, CancellationToken ct = default);
        /// <summary>Google / Firebase proved the email: clears MustChangePassword and the temporary password (hash included, so it stops working).</summary>
        Task ActivateAsync(int userId, CancellationToken ct = default);
    }
}
