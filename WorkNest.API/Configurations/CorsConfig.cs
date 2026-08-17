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
                        // In production, restrict to configured origins only
                        if (origins.Length > 0)
                        {
                            policy.WithOrigins(origins)
                                  .AllowAnyMethod()
                                  .AllowAnyHeader()
                                  .AllowCredentials()
                                  .WithExposedHeaders("*");
                        }
                        else
                        {
                            policy.AllowAnyOrigin()
                                  .AllowAnyMethod()
                                  .AllowAnyHeader();
                        }
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
