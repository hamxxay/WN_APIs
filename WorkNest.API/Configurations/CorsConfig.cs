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

            services.AddCors(options =>
            {
                options.AddPolicy(PolicyName, policy =>
                    policy
                        .WithOrigins(origins)
                        .SetIsOriginAllowedToAllowWildcardSubdomains()
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials()
                        .WithExposedHeaders("*"));
            });

            return services;
        }

        public static void UseCorsWithConfig(this WebApplication app)
        {
            app.UseCors(PolicyName);
        }
    }
}
