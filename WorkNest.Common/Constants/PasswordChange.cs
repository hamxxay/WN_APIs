namespace WorkNest.Common.Constants
{
    /// <summary>The first-login password change (AUTH_TEMP_01).</summary>
    public static class PasswordChange
    {
        /// <summary>JWT claim, "1" while the user must set a new password first.</summary>
        public const string ClaimType = "pwd_change";
        public const string Code = "PASSWORD_CHANGE_REQUIRED";
        public const string Message = "Please set a new password first.";
    }
}
