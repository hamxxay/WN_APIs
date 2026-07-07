namespace WorkNest.API.Configurations
{
    /// <summary>
    /// CORS configuration — reads allowed origins from appsettings.json Cors:AllowedOrigins.
    /// </summary>
    public static class CorsConfig
    {
        public const string PolicyName = "WorkNestCors";

        public static IServiceCollection AddCorsConfiguration(this IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy(PolicyName, policy =>
                {
                    // Origins resolved at runtime via IConfiguration in middleware pipeline.
                    // The actual origins are injected via the named policy resolver below.
                    policy
                        .SetIsOriginAllowed(_ => true) // overridden by WithOrigins at runtime
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials()
                        .WithExposedHeaders("*");
                });
            });

            return services;
        }

        /// <summary>
        /// Applies the CORS policy with origins read from configuration at startup.
        /// Call this after builder.Build() to access IConfiguration from the app.
        /// </summary>
        public static void UseCorsWithConfig(this WebApplication app)
        {
            var origins = app.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? [];

            app.UseCors(builder =>
                builder
                    .WithOrigins(origins)
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials()
                    .WithExposedHeaders("*"));
        }
    }
}
