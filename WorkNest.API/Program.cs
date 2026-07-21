using System.Text;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using WorkNest.API.Configurations;
using WorkNest.API.Middleware;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Application.Validators;
using WorkNest.Infrastructure.Database;
using WorkNest.Infrastructure.ExternalServices.Email;
using WorkNest.Infrastructure.ExternalServices.PayFast;
using WorkNest.Infrastructure.Repositories;
using WorkNest.Infrastructure.Security.Encryption;
using WorkNest.Infrastructure.Security.JWT;

// ── Serilog bootstrap ─────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog full configuration ────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, lc) =>
        lc.ReadFrom.Configuration(ctx.Configuration));

    // ── Controllers + JSON camelCase ──────────────────────────────────────────
    builder.Services.AddControllers()
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.PropertyNamingPolicy  = System.Text.Json.JsonNamingPolicy.CamelCase;
            o.JsonSerializerOptions.DictionaryKeyPolicy   = System.Text.Json.JsonNamingPolicy.CamelCase;
        });

    // ── FluentValidation ──────────────────────────────────────────────────────
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<UserSyncRequestValidator>();

    // ── CORS ──────────────────────────────────────────────────────────────────
    builder.Services.AddCorsConfiguration();

    // ── Swagger ───────────────────────────────────────────────────────────────
    builder.Services.AddSwaggerConfiguration();
    builder.Services.AddEndpointsApiExplorer();

    // ── JWT Settings ──────────────────────────────────────────────────────────
    builder.Services.Configure<JwtSettings>(
        builder.Configuration.GetSection("JwtSettings"));

    // ── JWT Authentication ────────────────────────────────────────────────────
    var jwtSection = builder.Configuration.GetSection("JwtSettings");
    var secretKey  = jwtSection["SecretKey"] ?? throw new Exception("JwtSettings:SecretKey missing");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey        = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ValidateIssuer          = true,
                ValidIssuer             = jwtSection["Issuer"],
                ValidateAudience        = true,
                ValidAudience           = jwtSection["Audience"],
                ValidateLifetime        = true,
                ClockSkew               = TimeSpan.Zero,
            };
        });

    builder.Services.AddAuthorization();

    // ── Database Settings ─────────────────────────────────────────────────────
    builder.Services.Configure<DatabaseSettings>(
        builder.Configuration.GetSection("DatabaseSettings"));

    // ── Infrastructure Services ───────────────────────────────────────────────
    builder.Services.AddScoped<IDbRepository, DbRepository>();
    builder.Services.AddScoped<IJwtService, JwtService>();
    builder.Services.AddScoped<IEncryptionService, EncryptionService>();
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddScoped<IPayFastService, PayFastService>();

    // ── Application Services ──────────────────────────────────────────────────
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<ISpaceService, SpaceService>();
    builder.Services.AddScoped<IBookingService, BookingService>();
    builder.Services.AddScoped<IPaymentService, PaymentService>();
    builder.Services.AddScoped<ILocationService, LocationService>();
    builder.Services.AddScoped<ISpaceTypeService, SpaceTypeService>();
    builder.Services.AddScoped<IPricingPlanService, PricingPlanService>();
    builder.Services.AddScoped<IMembershipService, MembershipService>();
    builder.Services.AddScoped<IContactService, ContactService>();
    builder.Services.AddScoped<IGalleryService, GalleryService>();
    builder.Services.AddScoped<ISpaceConfigService, SpaceConfigService>();
    builder.Services.AddScoped<IDashboardService, DashboardService>();
    builder.Services.AddScoped<IPlanFeatureService, PlanFeatureService>();
    builder.Services.AddScoped<IBranchService, BranchService>();
    builder.Services.AddScoped<IFloorService, FloorService>();
    builder.Services.AddScoped<IAmenityService, AmenityService>();
    builder.Services.AddScoped<ICustomerService, CustomerService>();
    builder.Services.AddScoped<IAccountCoaService, AccountCoaService>();
    builder.Services.AddScoped<IAmountFieldService, AmountFieldService>();

    // ── Build ─────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ── Middleware pipeline ───────────────────────────────────────────────────
    app.UseMiddleware<ExceptionMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "WorkNest API v1");
        c.RoutePrefix = "swagger";
    });

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }
    app.UseCorsWithConfig();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    Log.Information("WorkNest API starting on {Env}", app.Environment.EnvironmentName);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "WorkNest API failed to start.");
}
finally
{
    Log.CloseAndFlush();
}
