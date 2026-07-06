namespace WorkNest.Infrastructure.Database
{
    /// <summary>
    /// Strongly-typed settings bound from appsettings.json ConnectionStrings section.
    /// Mirrors the Python DB_SERVER / DB_PORT / DB_USER / DB_PASSWORD / DB_NAME env vars.
    /// </summary>
    public class DatabaseSettings
    {
        public string Server { get; set; } = string.Empty;
        public int Port { get; set; } = 1433;
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;

        /// <summary>Builds a standard ADO.NET SqlConnection connection string.</summary>
        public string ToConnectionString() =>
            $"Server={Server},{Port};Database={Database};User Id={User};Password={Password};TrustServerCertificate=True;";
    }
}
