using System.Text;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using WorkNest.API.Configurations;
using WorkNest.API.Middleware;
using WorkNest.API.Services;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Application.Validators;
using WorkNest.Infrastructure.ExternalServices.Email;
using WorkNest.Infrastructure.ExternalServices.Pdf;
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
            o.JsonSerializerOptions.Converters.Add(new FlexibleDateTimeJsonConverter());
            o.JsonSerializerOptions.Converters.Add(new FlexibleNullableDateTimeJsonConverter());
        });

    // ── FluentValidation ──────────────────────────────────────────────────────
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<UserSyncRequestValidator>();

    // ── CORS ──────────────────────────────────────────────────────────────────
    builder.Services.AddCorsConfiguration(builder.Configuration);

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

    // ── Infrastructure Services ───────────────────────────────────────────────
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IDbRepository, DbRepository>();
    builder.Services.AddScoped<IJwtService, JwtService>();
    builder.Services.AddScoped<IEncryptionService, EncryptionService>();
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddScoped<IPdfService, PdfService>();
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
    builder.Services.AddScoped<IContactService, ContactService>();
    builder.Services.AddScoped<IGalleryService, GalleryService>();
    builder.Services.AddScoped<ISpaceConfigService, SpaceConfigService>();
    builder.Services.AddScoped<IDashboardService, DashboardService>();
    builder.Services.AddScoped<IPlanFeatureService, PlanFeatureService>();
    builder.Services.AddScoped<IBranchService, BranchService>();
    builder.Services.AddScoped<IFloorService, FloorService>();
    builder.Services.AddScoped<IAmenityService, AmenityService>();
    builder.Services.AddScoped<ICustomerService, CustomerService>();
    builder.Services.AddScoped<IMembershipService, MembershipService>();
    builder.Services.AddScoped<IAccountCoaService, AccountCoaService>();
    builder.Services.AddScoped<IAmountFieldService, AmountFieldService>();
    builder.Services.AddScoped<IQuotationService, QuotationService>();
    builder.Services.AddScoped<IAccessCardService, AccessCardService>();
    builder.Services.AddScoped<IAttendantService, AttendantService>();

    // ── Background Hosted Services ────────────────────────────────────────────
    builder.Services.AddHostedService<BillingAutomationService>();
    builder.Services.AddHostedService<InvoiceDeliveryRetryService>();
    builder.Services.AddHostedService<AccessCardRestrictionService>();

    // ── Build ─────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ── Path base for IIS sub-application (production only) ──────────────────
    if (!app.Environment.IsDevelopment())
        app.UsePathBase("/WorkNest");

    // ── Middleware pipeline ───────────────────────────────────────────────────
    app.UseStaticFiles();
    app.UseCorsWithConfig();
    
    app.UseRouting();
    app.UseMiddleware<ExceptionMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        var swaggerPath = app.Environment.IsDevelopment()
            ? "/swagger/v1/swagger.json"
            : "/WorkNest/swagger/v1/swagger.json";
        c.SwaggerEndpoint(swaggerPath, "WorkNest API v1");
        c.RoutePrefix = "swagger";
    });

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    Log.Information("WorkNest API starting on {Env}", app.Environment.EnvironmentName);
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "WorkNest API failed to start.");
}
finally
{
    Log.CloseAndFlush();
}

public class FlexibleDateTimeJsonConverter : System.Text.Json.Serialization.JsonConverter<DateTime>
{
    public override DateTime Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str)) return default;

            if (DateTime.TryParse(str, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                return dt;

            if (DateTime.TryParse(str, out var dtLocal))
                return dtLocal;
        }
        else if (reader.TokenType == System.Text.Json.JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out var unixEpoch))
                return DateTimeOffset.FromUnixTimeMilliseconds(unixEpoch).UtcDateTime;
        }

        return default;
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, DateTime value, System.Text.Json.JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }
}

public class FlexibleNullableDateTimeJsonConverter : System.Text.Json.Serialization.JsonConverter<DateTime?>
{
    public override DateTime? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.Null)
            return null;

        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str)) return null;

            if (DateTime.TryParse(str, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                return dt;

            if (DateTime.TryParse(str, out var dtLocal))
                return dtLocal;
        }

        return null;
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, DateTime? value, System.Text.Json.JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        else
            writer.WriteNullValue();
    }
}

