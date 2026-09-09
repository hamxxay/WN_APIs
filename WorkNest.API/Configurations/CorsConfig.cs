namespace WorkNest.API.Configurations
{
    /// <summary>
    /// CORS configuration — reads allowed origins from appsettings.json Cors:AllowedOrigins.
    /// </summary>
    public static class CorsConfig
    {
        public const string PolicyName = "WorkNestCors";

        public static IServiceCollection AddCorsConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            var origins = configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? [];

            var isDevelopment = configuration["ASPNETCORE_ENVIRONMENT"] == "Development";

            services.AddCors(options =>
            {
                options.AddPolicy(PolicyName, policy =>
                {
                    if (isDevelopment)
                    {
                        // In development, allow all origins with any method and header
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    }
                    else
                    {
                        // In production, allow configured origins and all .vercel.app deployment URLs
                        policy.SetIsOriginAllowed(origin =>
                        {
                            if (string.IsNullOrWhiteSpace(origin)) return false;
                            if (origins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase))) return true;
                            if (origin.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase)) return true;
                            if (origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)) return true;
                            return false;
                        })
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials()
                        .WithExposedHeaders("*");
                    }
                });
            });

            return services;
        }

        public static void UseCorsWithConfig(this WebApplication app)
        {
            app.UseCors(PolicyName);
        }
    }
}
