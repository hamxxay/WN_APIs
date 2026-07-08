using Microsoft.OpenApi.Models;
using System.Reflection;
using System.IO;

namespace WorkNest.API.Configurations
{
    /// <summary>
    /// Swagger/OpenAPI configuration with JWT bearer authentication support.
    /// </summary>
    public static class SwaggerConfig
    {
        public static IServiceCollection AddSwaggerConfiguration(this IServiceCollection services)
        {
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title       = "WorkNest API",
                    Version     = "v1.0.0",
                    Description = "WorkNest Coworking Space Management API — migrated from FastAPI to ASP.NET Core 8",
                });
                // Include XML comments from this and referenced projects (if present)
                // so controller and DTO <summary> comments appear in Swagger UI.
                var baseDir = AppContext.BaseDirectory;
                var thisXml = Path.Combine(baseDir, Assembly.GetExecutingAssembly().GetName().Name + ".xml");
                if (File.Exists(thisXml)) c.IncludeXmlComments(thisXml);

                var otherXmls = new[]
                {
                    "WorkNest.Application.xml",
                    "WorkNest.Common.xml",
                    "WorkNest.Domain.xml",
                    "WorkNest.Infrastructure.xml"
                };

                foreach (var xml in otherXmls)
                {
                    var path = Path.Combine(baseDir, xml);
                    if (File.Exists(path)) c.IncludeXmlComments(path);
                }

                // JWT Bearer auth in Swagger UI
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name         = "Authorization",
                    Type         = SecuritySchemeType.Http,
                    Scheme       = "Bearer",
                    BearerFormat = "JWT",
                    In           = ParameterLocation.Header,
                    Description  = "Enter your JWT token. Example: Bearer {token}",
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id   = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
            });

            return services;
        }
    }
}
