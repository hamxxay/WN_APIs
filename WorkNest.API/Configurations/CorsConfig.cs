namespace WorkNest.API.Configurations
{
    /// <summary>
    /// CORS configuration.
    /// Mirrors the Python ALLOWED_ORIGINS list and Vercel wildcard regex exactly.
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
                    policy
                        .WithOrigins(
                            "http://localhost:4200",
                            "http://localhost:5173",
                            "https://worknest.vercel.app",
                            "https://work-nest-api-s.vercel.app",
                            "https://worknestpk.com",
                            "https://www.worknestpk.com"
                        )
                        .SetIsOriginAllowedToAllowWildcardSubdomains()
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials()
                        .WithExposedHeaders("*");
                });
            });

            return services;
        }
    }
}
