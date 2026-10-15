using System.Text;
using System.Security.Claims;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using WorkNest.API.Configurations;
using WorkNest.API.Middleware;
using WorkNest.API.Security;
using WorkNest.API.Services;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Application.Validators;
using WorkNest.Common.Configurations;
using WorkNest.Common.Constants;
using WorkNest.Infrastructure.ExternalServices.Email;
using WorkNest.Infrastructure.ExternalServices.Pdf;
using WorkNest.Infrastructure.ExternalServices.PayFast;
using WorkNest.Infrastructure.ExternalServices.FileStorage;
using WorkNest.Infrastructure.ExternalServices.Hikvision;
using WorkNest.Infrastructure.ExternalServices.Reports;
using WorkNest.Infrastructure.ExternalServices.Unifi;
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
    // FriendlyErrorResultFilter: SQL Server / .NET error text in a failed response is logged and replaced by a friendly message.
    // CeoAccessFilter: a CEO account gets 403 on every endpoint not marked [CeoAllowed] (it only views the CEO dashboard).
    // SalesManagerAccessFilter: likewise a Sales Manager gets 403 on every endpoint not marked [SalesManagerAllowed].
    // AccessOfficerAccessFilter: and an Access Officer on every endpoint not marked [AccessOfficerAllowed].
    builder.Services.AddControllers(o =>
        {
            o.Filters.Add<WorkNest.API.Filters.FriendlyErrorResultFilter>();
            o.Filters.Add<WorkNest.API.Filters.CeoAccessFilter>();
            o.Filters.Add<WorkNest.API.Filters.SalesManagerAccessFilter>();
            o.Filters.Add<WorkNest.API.Filters.AccessOfficerAccessFilter>();
        })
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

    // ── Rate limiting (built-in; "RateLimiting" config section) ──────────────
    builder.Services.AddRateLimitingConfiguration(builder.Configuration);

    // ── Reverse proxy: X-Forwarded-For only from "ReverseProxy:TrustedProxies" (default: worknestpk.com relay) ─
    builder.Services.AddReverseProxyConfiguration(builder.Configuration);

    // ── Swagger ───────────────────────────────────────────────────────────────
    builder.Services.AddSwaggerConfiguration();
    builder.Services.AddEndpointsApiExplorer();

    // ── Settings ──────────────────────────────────────────────────────────────
    builder.Services.Configure<JwtSettings>(
        builder.Configuration.GetSection("JwtSettings"));
    builder.Services.Configure<FileStorageSettings>(
        builder.Configuration.GetSection("FileStorage"));
    builder.Services.Configure<KycStorageSettings>(
        builder.Configuration.GetSection("KycStorage"));

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
                RoleClaimType           = ClaimTypes.Role,
                NameClaimType           = ClaimTypes.NameIdentifier
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["token"].ToString();
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        accessToken = context.Request.Query["access_token"].ToString();
                    }
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
                // Signed-out tokens, and users deactivated or given another role / location since the token was issued, get a 401.
                OnTokenValidated = context => context.HttpContext.RequestServices
                    .GetRequiredService<TokenSessionValidator>().ValidateAsync(context)
            };
        });
    builder.Services.AddSingleton<TokenSessionValidator>();

    builder.Services.AddAuthorization(options =>
    {
        // Allowed roles: super_admin, admin, sales_executive
        options.AddPolicy("KycAccessPolicy", policy =>
            policy.RequireAssertion(ctx =>
            {
                var roleClaim = ctx.User.FindFirst(ClaimTypes.Role)?.Value 
                             ?? ctx.User.FindFirst("role")?.Value;
                if (string.IsNullOrWhiteSpace(roleClaim)) return false;

                var role = roleClaim.Trim().ToLowerInvariant();
                return role == Roles.SuperAdmin ||
                       role == Roles.Admin ||
                       role == Roles.SalesExecutive ||
                       role == "superadmin" ||
                       role == "administrator" ||
                       role == "salesexecutive" ||
                       role == "1" ||
                       role == "2" ||
                       role == "16";
            }));

        // Verify and Reject: admin and super_admin only
        options.AddPolicy("KycVerifyPolicy", policy =>
            policy.RequireAssertion(ctx =>
            {
                var roleClaim = ctx.User.FindFirst(ClaimTypes.Role)?.Value 
                             ?? ctx.User.FindFirst("role")?.Value;
                if (string.IsNullOrWhiteSpace(roleClaim)) return false;

                var role = roleClaim.Trim().ToLowerInvariant();
                return role == Roles.SuperAdmin ||
                       role == Roles.Admin ||
                       role == "superadmin" ||
                       role == "administrator" ||
                       role == "1" ||
                       role == "2";
            }));
    });

    // ── Infrastructure Services ───────────────────────────────────────────────
    builder.Services.AddHttpContextAccessor();
    // One business clock (Pakistan time) for every business date; "Business:TimeZone" defaults to Asia/Karachi.
    builder.Services.AddSingleton<IBusinessClock>(new BusinessClock(builder.Configuration["Business:TimeZone"]).UseAsDefault());
    builder.Services.AddScoped<IDbRepository, DbRepository>();
    builder.Services.AddScoped<IJwtService, JwtService>();
    builder.Services.AddScoped<IEncryptionService, EncryptionService>();
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddScoped<IPdfService, PdfService>();
    builder.Services.AddSingleton<IHtmlToPdfService, HtmlToPdfService>();
    builder.Services.AddScoped<IPdfMergeService, PdfMergeService>();
    builder.Services.AddScoped<IPayFastService, PayFastService>();
    builder.Services.AddScoped<IKycFileStorage, LocalKycFileStorage>();
    builder.Services.AddSingleton<IHikIsapiClient, HikIsapiClient>();
    // Hikvision machine <-> DB sync (replaces the HIK Node scheduler). Off unless HikSync:Enabled = true.
    var hikSyncOptions = new HikSyncOptions();
    builder.Configuration.GetSection("HikSync").Bind(hikSyncOptions);
    builder.Services.AddSingleton(hikSyncOptions);
    builder.Services.AddSingleton<IHikSyncRepository, HikSyncRepository>();
    builder.Services.AddSingleton<IHikSyncService, HikSyncService>();
    builder.Services.AddSingleton<IUnifiClient, UnifiClient>();

    // ── Application Services ──────────────────────────────────────────────────
    builder.Services.AddScoped<IAuthService, AuthService>();
    // Roles from dbo.WN_Roles (api/roles, roleDisplayName on sign-in / api/auth/me): dbo.WN_Roles_GetList (script SM_01).
    builder.Services.AddScoped<IRoleService, RoleService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<ISpaceService, SpaceService>();
    builder.Services.AddScoped<IBookingService, BookingService>();
    builder.Services.AddScoped<IPaymentService, PaymentService>();
    builder.Services.AddScoped<ILocationService, LocationService>();
    builder.Services.AddScoped<ISpaceTypeService, SpaceTypeService>();
    builder.Services.AddScoped<IPricingPlanService, PricingPlanService>();
    builder.Services.AddScoped<IContactService, ContactService>();
    // Complaints (WhatsApp bot + staff) and the WhatsApp bot / Inbox (WhatsApp:* settings, webhook api/whatsapp/webhook).
    builder.Services.AddScoped<IComplaintRepository, WorkNest.Infrastructure.Repositories.ComplaintRepository>();
    builder.Services.AddScoped<IComplaintService, ComplaintService>();
    builder.Services.AddScoped<IChatbotNotifier, WhatsAppComplaintNotifier>();
    builder.Services.AddScoped<IWhatsAppRepository, WorkNest.Infrastructure.Repositories.WhatsAppRepository>();
    builder.Services.AddScoped<IWhatsAppClient, WorkNest.Infrastructure.ExternalServices.WhatsApp.WhatsAppCloudClient>();
    builder.Services.AddScoped<IWhatsAppInboxService, WhatsAppInboxService>();
    builder.Services.AddScoped<IWhatsAppBotService, WhatsAppBotService>();
    builder.Services.AddScoped<IGalleryService, GalleryService>();
    builder.Services.AddScoped<ISpaceConfigService, SpaceConfigService>();
    builder.Services.AddScoped<IDashboardService, DashboardService>();
    builder.Services.AddScoped<IRoleDashboardRepository, WorkNest.Infrastructure.Repositories.RoleDashboardRepository>();
    builder.Services.AddScoped<IRecordScopeRepository, WorkNest.Infrastructure.Repositories.RecordScopeRepository>(); // location of one record (RecordScopeAttribute)
    builder.Services.AddScoped<IRoleDashboardService, RoleDashboardService>();
    // CEO dashboard (api/ceo/*): stored procedures dbo.WN_Ceo_* (script CEO_01_dashboard_procedures).
    builder.Services.AddScoped<ICeoDashboardRepository, WorkNest.Infrastructure.Repositories.CeoDashboardRepository>();
    builder.Services.AddScoped<ICeoDashboardService, CeoDashboardService>();
    // Sales Manager dashboard (api/sales-manager/*): stored procedures dbo.WN_SalesMgr_* (scripts SM_01_sales_manager,
    // SM_02_sales_manager_desks: desks, no money, and SM_03_sales_manager_complaints: the Complaints section).
    builder.Services.AddScoped<ISalesManagerDashboardRepository, WorkNest.Infrastructure.Repositories.SalesManagerDashboardRepository>();
    builder.Services.AddScoped<ISalesManagerDashboardService, SalesManagerDashboardService>();
    // Access Officer page (api/access-officer/*): dbo.WN_AccessOfficer_Lookup (script AO_01_access_officer); the door
    // access itself reuses the attendants, customer-employees and Hikvision enrolment services below.
    builder.Services.AddScoped<IAccessOfficerRepository, WorkNest.Infrastructure.Repositories.AccessOfficerRepository>();
    builder.Services.AddScoped<IAccessOfficerService, AccessOfficerService>();
    // 3-month forecast (api/dashboard/forecast) and the weekly super admin report (api/reports/weekly, WeeklyReportService).
    builder.Services.AddScoped<IReportRepository, WorkNest.Infrastructure.Repositories.ReportRepository>();
    builder.Services.AddScoped<IAuditLogRepository, WorkNest.Infrastructure.Repositories.AuditLogRepository>();
    builder.Services.AddScoped<ILoginHistoryService, WorkNest.Infrastructure.Repositories.LoginHistoryService>();
    builder.Services.AddScoped<ILoginHistoryRepository, WorkNest.Infrastructure.Repositories.LoginHistoryRepository>();
    builder.Services.AddScoped<IForecastService, ForecastService>();
    builder.Services.AddScoped<IWeeklyReportGenerator, WeeklyReportGenerator>();
    builder.Services.AddScoped<IPlanFeatureService, PlanFeatureService>();
    builder.Services.AddScoped<IBranchService, BranchService>();
    builder.Services.AddScoped<IFloorService, FloorService>();
    builder.Services.AddScoped<IAmenityService, AmenityService>();
    builder.Services.AddScoped<ICustomerService, CustomerService>();
    builder.Services.AddScoped<IMembershipService, MembershipService>();
    builder.Services.AddScoped<IAccountCoaService, AccountCoaService>();
    builder.Services.AddScoped<IFeeReceiptService, FeeReceiptService>();
    builder.Services.AddScoped<IAmountFieldService, AmountFieldService>();
    builder.Services.AddScoped<IQuotationService, QuotationService>();
    builder.Services.AddScoped<IAgreementService, AgreementService>();
    builder.Services.AddScoped<ILeaseTemplateService, LeaseTemplateService>();
    builder.Services.AddScoped<IAccessCardService, AccessCardService>();
    builder.Services.AddScoped<IAttendantService, AttendantService>();
    builder.Services.AddScoped<IBookingEmployeeRepository, WorkNest.Infrastructure.Repositories.BookingEmployeeRepository>(); // customer "Employees" (EMP_01)
    builder.Services.AddScoped<IBookingEmployeeService, BookingEmployeeService>();
    builder.Services.AddScoped<IAnnouncementService, AnnouncementService>();
    builder.Services.AddScoped<IHikDeviceService, HikDeviceService>();
    builder.Services.AddScoped<IHikEnrollmentService, HikEnrollmentService>();
    builder.Services.AddScoped<IHikStaffService, HikEnrollmentService>();
    builder.Services.AddScoped<IHikAccessSuspensionService, HikEnrollmentService>();
    builder.Services.AddScoped<IHikMobileDoorService, HikEnrollmentService>();
    builder.Services.AddScoped<IChallanService, ChallanService>();
    builder.Services.AddScoped<IOrderStatusService, OrderStatusService>();
    builder.Services.AddScoped<IHikAccessService, HikAccessService>();
    builder.Services.AddScoped<IKycService, KycService>();
    builder.Services.AddScoped<ISecurityDepositReportService, SecurityDepositReportService>();
    builder.Services.AddSingleton<IUnifiService, UnifiService>();


    // ── Background Hosted Services ────────────────────────────────────────────
    builder.Services.AddHostedService<BillingAutomationService>();
    builder.Services.AddHostedService<InvoiceDeliveryRetryService>();
    builder.Services.AddHostedService<AccessCardRestrictionService>();
    builder.Services.AddHostedService<AnnouncementDeliveryService>();
    builder.Services.AddHostedService<ChallanAccessSuspensionService>();
    builder.Services.AddHostedService<HikSyncBackgroundService>();
    builder.Services.AddHostedService<BookingAutoConfirmService>();
    builder.Services.AddHostedService<AgreementReminderService>();
    builder.Services.AddHostedService<WeeklyReportService>();   // Reports:Weekly (Monday 09:00 Pakistan time by default)
    builder.Services.AddMemoryCache();
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<WorkNest.API.Security.FirebaseTokenVerifier>();
    builder.Services.AddHostedService<UnifiPollingService>();

    // ── Build ─────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ── JWT signing key check (logged, never stops the site) ──────────────────
    if (app.Environment.IsProduction())
    {
        var devKey = JwtKeyCheck.DevelopmentKeyFrom(Path.Combine(app.Environment.ContentRootPath, "appsettings.Development.json"));
        var keyWeaknesses = JwtKeyCheck.Weaknesses(secretKey, devKey);
        if (keyWeaknesses.Count > 0)
        {
            Log.Fatal("CRITICAL: JwtSettings:SecretKey is not safe for the live server: {Weaknesses}. Anyone who guesses it can sign in " +
                      "as any user, including super admin. Generate a random key (64 random bytes, base64), put it in JwtSettings:SecretKey " +
                      "(appsettings.json on the server, or the JwtSettings__SecretKey environment variable of the app pool) and recycle the " +
                      "app pool. Everyone then signs in again once.", string.Join("; ", keyWeaknesses));
        }
    }

    // ── Forwarded Headers: first, so everything after (rate limits, logs, e-signature IP) sees the real client ─
    app.UseForwardedHeadersWithConfig();

    // ── Path base for IIS sub-application (configurable via appsettings/env) ─
    var pathBase = builder.Configuration["PathBase"];
    if (!string.IsNullOrWhiteSpace(pathBase))
    {
        app.UsePathBase(pathBase);
    }

    // ── Middleware pipeline ───────────────────────────────────────────────────
    app.UseStaticFiles();
    app.UseCorsWithConfig();
    
    app.UseRouting();
    app.UseMiddleware<ExceptionMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("v1/swagger.json", "WorkNest API v1");
        c.RoutePrefix = "swagger";
    });

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimitingWithConfig(); // after routing + auth (per-user partitions), before endpoints
    app.MapControllers();

    // Browser for PDF rendering is lazily initialized on first use in HtmlToPdfService to avoid consuming ~200MB+ RAM at startup.

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

/// <summary>
/// Writes DateTimes for the API. Only real UTC values (DateTimeKind.Utc — e.g. UTC audit columns marked by
/// DbDateTimeKinds, DateTime.UtcNow) get a "Z". Unspecified / Local values — business wall-clock times in
/// Pakistan time, which is what SQL returns for StartOn, DueOn, IssuedOn … — are written without an offset,
/// so the browser treats them as local wall-clock time instead of shifting them by +5h.
/// </summary>
public static class FlexibleDateTimeJsonWriter
{
    public static void Write(System.Text.Json.Utf8JsonWriter writer, DateTime value)
    {
        writer.WriteStringValue(value.Kind == DateTimeKind.Utc
            ? value.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture));
    }
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
        => FlexibleDateTimeJsonWriter.Write(writer, value);
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
            FlexibleDateTimeJsonWriter.Write(writer, value.Value);
        else
            writer.WriteNullValue();
    }
}

