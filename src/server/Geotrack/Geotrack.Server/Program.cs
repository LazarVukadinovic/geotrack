using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Application.Security;
using Geotrack.Application.Services;
using Geotrack.Infrastructure;
using Geotrack.Infrastructure.Repositories;
using Geotrack.Infrastructure.Services;
using Geotrack.Infrastructure.Services.Agent;
using Geotrack.Server;
using Geotrack.Server.Health;
using Geotrack.Server.Middleware;
using Geotrack.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var uploadLimits = builder.Configuration
    .GetSection("UploadLimits")
    .Get<UploadLimitsOptions>()
    ?? new UploadLimitsOptions();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = uploadLimits.GlobalMaxRequestBodyBytes;
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is required. Set via user-secrets, environment variable, or appsettings.*.json.");
}

builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var configuredConnectionString = serviceProvider
        .GetRequiredService<IConfiguration>()
        .GetConnectionString("DefaultConnection");

    if (string.IsNullOrWhiteSpace(configuredConnectionString))
    {
        throw new InvalidOperationException(
            "ConnectionStrings:DefaultConnection is required. Set via user-secrets, environment variable, or appsettings.*.json.");
    }

    options.UseNpgsql(
        configuredConnectionString,
        npgsqlOptions =>
        {
            npgsqlOptions.UseNetTopologySuite();
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null);
        });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var message = context.ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? error.Exception?.Message
                : error.ErrorMessage)
            .FirstOrDefault(errorMessage => !string.IsNullOrWhiteSpace(errorMessage))
            ?? "Invalid request.";

        if (message.Contains("Multipart body length limit", StringComparison.OrdinalIgnoreCase))
        {
            return new ObjectResult(new { message = "Uploaded file is too large." })
            {
                StatusCode = StatusCodes.Status413PayloadTooLarge
            };
        }

        return new BadRequestObjectResult(new { message });
    };
});

builder.Services.AddMemoryCache();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/json",
        "application/problem+json"
    });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<McpOptions>(builder.Configuration.GetSection("Mcp"));
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection("Notifications"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<BoundaryImportOptions>(builder.Configuration.GetSection("BoundaryImport"));
builder.Services.Configure<RegionBoundaryBackfillOptions>(builder.Configuration.GetSection("RegionBoundaryBackfill"));
builder.Services.Configure<RegionalScopeValidationOptions>(builder.Configuration.GetSection("RegionalScopeValidation"));
builder.Services.Configure<UploadLimitsOptions>(builder.Configuration.GetSection("UploadLimits"));
builder.Services.Configure<AnalyticsRetentionOptions>(builder.Configuration.GetSection("AnalyticsRetention"));
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = uploadLimits.GlobalMultipartBodyLengthLimitBytes;
});
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Process is alive."), tags: new[] { "live" })
    .AddCheck<DatabaseReadinessHealthCheck>("database", tags: new[] { "ready" });
builder.Services.AddDataProtection()
    .SetApplicationName("GeoTrack");

builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter a JWT token as: Bearer {token}"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IDestinationRepository, DestinationRepository>();
builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<ILocationRepository, LocationRepository>();
builder.Services.AddScoped<IPlaceRepository, PlaceRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRegionRepository, RegionRepository>();

builder.Services.AddScoped<IDestinationService, DestinationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IPlaceService, PlaceService>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<ILocationService, LocationService>();
builder.Services.AddScoped<IRegionService, RegionService>();

builder.Services.AddScoped<ITerritoryRepository, TerritoryRepository>();
builder.Services.AddScoped<ITerritoryService, TerritoryService>();

builder.Services.AddScoped<IFavoriteService, FavoriteService>();
builder.Services.AddSingleton<IAnalyticsProcessingQueue, AnalyticsProcessingQueue>();
builder.Services.AddHostedService<AnalyticsEventProcessingWorker>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IAnalyticsReadService, AnalyticsReadService>();
builder.Services.AddScoped<IAnalyticsRetentionService, AnalyticsRetentionService>();
builder.Services.AddScoped<IUserInterestSignalUpdater, UserInterestSignalUpdater>();

builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IModerationTermCache, ModerationTermCache>();
builder.Services.AddScoped<IModerationTermService, ModerationTermService>();
builder.Services.AddScoped<IReviewContentModerationService, ReviewContentModerationService>();
builder.Services.AddScoped<IReviewReportService, ReviewReportService>();
builder.Services.AddScoped<IModerationAuditService, ModerationAuditService>();
builder.Services.AddScoped<IContentHtmlSanitizer, ContentHtmlSanitizer>();

builder.Services.AddScoped<ITagService, TagService>();
builder.Services.AddScoped<IInterestTopicService, InterestTopicService>();
builder.Services.AddSingleton<IAgentTaxonomyProvider, JsonAgentTaxonomyProvider>();
builder.Services.AddScoped<IAgentDatabaseCapabilityService, AgentDatabaseCapabilityService>();
builder.Services.AddScoped<IAgentAreaResolver, AgentAreaResolver>();
builder.Services.AddSingleton<IAgentSemanticSearchService, NoOpAgentSemanticSearchService>();
builder.Services.AddScoped<IAgentQueryPlanner, AgentQueryPlanner>();
builder.Services.AddScoped<IGeoTrackToolService, GeoTrackToolService>();
builder.Services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
builder.Services.AddSingleton<IGeminiKeyPool, GeminiKeyPool>();
builder.Services.AddScoped<IGeminiAskAgentService, GeminiAskAgentService>();
builder.Services.AddHttpClient<GeminiAgentProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GeoTrack/1.0");
});
builder.Services.AddScoped<IExploreService, ExploreService>();
builder.Services.AddScoped<IExploreSearchService, ExploreSearchService>();
builder.Services.AddScoped<ISearchService, SearchService>();

builder.Services.AddScoped<IPlaceContributionService, PlaceContributionService>();
builder.Services.AddScoped<IPlaceContributionRepository, PlaceContributionRepository>();

builder.Services.AddScoped<IUserNotificationService, UserNotificationService>();
builder.Services.AddScoped<IUserNotificationRepository, UserNotificationRepository>();
builder.Services.AddScoped<IAdminNotificationService, AdminNotificationService>();
builder.Services.AddScoped<IAdminProfileService, AdminProfileService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IContentModeratorDashboardService, ContentModeratorDashboardService>();
builder.Services.AddScoped<IContentModeratorEntityReviewService, ContentModeratorEntityReviewService>();
builder.Services.AddScoped<IAdminUserManagementService, AdminUserManagementService>();
builder.Services.AddScoped<IAdminReviewManagementService, AdminReviewManagementService>();
builder.Services.AddScoped<IAdminSupportTicketService, AdminSupportTicketService>();
builder.Services.AddHttpClient<IGeoBoundariesService, GeoBoundariesService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GeoTrack/1.0");
});
builder.Services.AddScoped<LocalBoundaryImportService>();
builder.Services.AddScoped<ILocalBoundaryImportService>(serviceProvider =>
    serviceProvider.GetRequiredService<LocalBoundaryImportService>());
builder.Services.AddScoped<IAdminTerritoryManagementService, AdminTerritoryManagementService>();
builder.Services.AddScoped<IRegionalDashboardService, RegionalDashboardService>();
builder.Services.AddScoped<IHealthScoreService, HealthScoreService>();
builder.Services.AddScoped<ITerritoryDashboardService, TerritoryDashboardService>();
builder.Services.AddScoped<IRegionBoundaryBackfillService, RegionBoundaryBackfillService>();
builder.Services.AddScoped<IAdmBoundaryRegionSyncService, AdmBoundaryRegionSyncService>();
builder.Services.AddScoped<IRegionContentAssignmentService, RegionContentAssignmentService>();
builder.Services.AddScoped<ITerritoryAnalyticsService, TerritoryAnalyticsService>();
builder.Services.AddScoped<ITerritoryUserAdminService, TerritoryUserAdminService>();
builder.Services.AddScoped<IRegionalRequestQueueService, RegionalRequestQueueService>();
builder.Services.AddScoped<RegionalBoundaryGuard>();
builder.Services.AddScoped<RegionalScopeValidationService>();
builder.Services.AddScoped<IRegionalScopeValidationService>(serviceProvider =>
    serviceProvider.GetRequiredService<RegionalScopeValidationService>());
builder.Services.AddScoped<DestinationBoundaryResolver>();
builder.Services.AddHttpClient("DestinationBoundaryProvider", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GeoTrack/1.0 (destination-boundary-search)");
});
builder.Services.AddScoped<IRegionalDestinationService, RegionalDestinationService>();
builder.Services.AddScoped<IRegionalLocationService, RegionalLocationService>();
builder.Services.AddScoped<IRegionalPlaceService, RegionalPlaceService>();
builder.Services.AddScoped<IRegionalEventService, RegionalEventService>();
builder.Services.AddScoped<IRegionalMapService, RegionalMapService>();
builder.Services.AddScoped<IRegionalAnalyticsService, RegionalAnalyticsService>();
builder.Services.AddScoped<IRegionalMediaService, RegionalMediaService>();
builder.Services.AddHttpClient<IClientLocationResolverService, ClientLocationResolverService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GeoTrack/1.0");
});

builder.Services.AddHttpClient<IMapService, MapService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GeoTrack/1.0");
});

builder.Services.AddScoped<IUserAvatarStorageService, LocalUserAvatarStorageService>();
builder.Services.AddScoped<IContributionMediaStorageService, LocalContributionMediaStorageService>();
builder.Services.AddScoped<IRegionalEntityImageStorageService, LocalRegionalEntityImageStorageService>();

builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IAuthEmailService, AuthEmailService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IFavoriteRepository, FavoriteRepository>();
builder.Services.AddScoped<IReviewRepository, ReviewRepository>();
builder.Services.AddScoped<ITagRepository, TagRepository>();
builder.Services.AddScoped<IInterestTopicRepository, InterestTopicRepository>();

var forwardedHeadersEnabled = ConfigureForwardedHeaders(builder);

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "Jwt:Key is required. Set via user-secrets, environment variable, or appsettings.*.json.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Jwt:Issuer is required.");

var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Jwt:Audience is required.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy =>
        policy.RequireRole(RoleNames.Admin));
    options.AddPolicy("RequireContentModerator", policy =>
        policy.RequireRole(RoleNames.ContentModerator));
    options.AddPolicy("RequireReviewModerator", policy =>
        policy.RequireRole(RoleNames.Admin, RoleNames.ContentModerator));
    options.AddPolicy("RequireRegionalManager", policy =>
        policy.RequireRole(RoleNames.RegionalManager));
    options.AddPolicy("RequireTerritoryManager", policy =>
        policy.RequireRole(RoleNames.TerritoryManager, RoleNames.TerritoryAssociate));
    options.AddPolicy("RequireAdminAppUser", policy =>
        policy.RequireRole(RoleNames.AdminAppRoles));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (ctx, token) =>
    {
        int? retryAfterSeconds = null;
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
        {
            retryAfterSeconds = (int)retry.TotalSeconds;
            ctx.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        }

        var logger = ctx.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiting");
        logger.LogWarning(
            "Rate limit rejected {Method} {Path}; retry after {RetryAfterSeconds} seconds.",
            ctx.HttpContext.Request.Method,
            ctx.HttpContext.Request.Path.Value,
            retryAfterSeconds);

        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many requests. Please try again later." },
            token);
    };

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    options.AddPolicy("agent", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetUserOrIpRateLimitPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    options.AddPolicy("mcp", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetUserOrIpRateLimitPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    // Backend-mediated map providers and spatial queries are public, so keep
    // separate budgets for normal viewport loads and costlier external lookups.
    options.AddPolicy("map", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetUserOrIpRateLimitPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    options.AddPolicy("mapSearch", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetUserOrIpRateLimitPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    options.AddPolicy("mapExternalLookup", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetUserOrIpRateLimitPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    options.AddPolicy("approximateLocation", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetUserOrIpRateLimitPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    options.AddPolicy("analytics", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey:
                httpContext.User.FindFirst("userId")?.Value
                ?? httpContext.Request.Headers["X-Anonymous-Session"].ToString()
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    options.AddPolicy("reviews", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetUserAndIpRateLimitPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    options.AddPolicy("reports", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetUserAndIpRateLimitPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    options.AddPolicy("contributions", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetUserAndIpRateLimitPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 12,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    options.AddPolicy("uploads", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetUserAndIpRateLimitPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    options.AddPolicy("support", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetUserAndIpRateLimitPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    options.AddPolicy("userActions", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetUserAndIpRateLimitPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));
});

var allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray()
    ?? Array.Empty<string>();

if (allowedCorsOrigins.Length == 0)
{
    allowedCorsOrigins = builder.Environment.IsDevelopment()
        ? new[]
        {
            "http://localhost:4200",
            "http://127.0.0.1:4200",
            "https://localhost:4200",
            "https://127.0.0.1:4200",
            "http://localhost:4300",
            "http://127.0.0.1:4300",
            "https://localhost:4300",
            "https://127.0.0.1:4300"
        }
        : new[]
        {
            "https://softeng.pmf.kg.ac.rs:10131",
            "https://softeng.pmf.kg.ac.rs:10132"
        };
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredOrigins", policy =>
    {
        policy.WithOrigins(allowedCorsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

await PostgisStartup.EnsureReadyAsync(app.Services, app.Environment.IsDevelopment());

var shouldSeedOperationalUsers = OperationalUserSeeder.IsEnabled(app.Environment, app.Configuration);
var shouldSeedDemoContent = IsDemoContentSeedEnabledForEnvironment(app.Environment, app.Configuration);
var shouldSeedPublicContent = IsPublicContentSeedEnabledForEnvironment(app.Environment, app.Configuration);
var shouldAutoMigrate =
    app.Environment.IsDevelopment()
    || app.Configuration.GetValue<bool>("Database:AutoMigrate")
    || shouldSeedOperationalUsers
    || shouldSeedDemoContent
    || shouldSeedPublicContent;

if (shouldAutoMigrate || shouldSeedOperationalUsers || shouldSeedDemoContent || shouldSeedPublicContent)
{
    using var strategyScope = app.Services.CreateScope();
    var strategyDb = strategyScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var strategy = strategyDb.Database.CreateExecutionStrategy();

    await strategy.ExecuteAsync(async () =>
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync();

        if ((shouldSeedDemoContent || shouldSeedPublicContent) &&
            app.Configuration.GetValue<bool>("Seed:ImportLocalBoundaries:Enabled"))
        {
            var localBoundaryImport = scope.ServiceProvider.GetRequiredService<ILocalBoundaryImportService>();

            if (app.Configuration.GetValue("Seed:ImportLocalBoundaries:Adm0", true))
            {
                await localBoundaryImport.ImportLocalBoundariesAsync("ADM0");
            }

            if (app.Configuration.GetValue("Seed:ImportLocalBoundaries:Adm1", true))
            {
                await localBoundaryImport.ImportLocalBoundariesAsync("ADM1");
            }
        }

        if (shouldSeedOperationalUsers)
        {
            var logger = scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(OperationalUserSeeder));
            await OperationalUserSeeder.EnsureAsync(db, app.Configuration, logger);
        }

        if (shouldSeedDemoContent)
        {
            await SeedData.InitializeAsync(db);
        }
        else if (shouldSeedPublicContent)
        {
            await SeedData.EnsureRequiredPublicContentAsync(db);
        }

        if ((shouldSeedDemoContent || shouldSeedPublicContent) &&
            app.Configuration.GetValue("Seed:SyncAdm1Regions:Enabled", true))
        {
            foreach (var syncCountryIso3 in ResolveSeedAdm1SyncCountryIso3s(app.Configuration))
            {
                var syncTerritoryId = await db.Territories
                    .AsNoTracking()
                    .Where(territory => territory.CountryIso3 == syncCountryIso3)
                    .OrderBy(territory => territory.Id)
                    .Select(territory => (int?)territory.Id)
                    .FirstOrDefaultAsync();

                if (syncTerritoryId.HasValue)
                {
                    var regionSync = scope.ServiceProvider.GetRequiredService<IAdmBoundaryRegionSyncService>();
                    var syncSummary = await regionSync.SyncRegionsFromAdm1CandidatesAsync(
                        syncTerritoryId.Value,
                        dryRun: false,
                        forceUpdateGeometry: false,
                        createMissingRegions: true,
                        linkExistingRegions: true);

                    var logger = scope.ServiceProvider
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("SeedAdm1RegionSync");
                    logger.LogInformation(
                        "Seed ADM1 region sync for {CountryIso3}: {CreatedCount} created, {LinkedExistingCount} linked, {UpdatedCount} updated, {ExistingLinkedCount} already linked, {AmbiguousCount} ambiguous, {FailedCount} failed.",
                        syncSummary.CountryIso3,
                        syncSummary.CreatedCount,
                        syncSummary.LinkedExistingCount,
                        syncSummary.UpdatedCount,
                        syncSummary.ExistingLinkedCount,
                        syncSummary.AmbiguousCount,
                        syncSummary.FailedCount);
                }
            }
        }
    });
}

// Interest topics and their tag taxonomy are required reference data for onboarding
// and the My Interests page. Keep this independent from optional demo/public seeds so
// production or local databases cannot remain stuck with an old, partial catalogue.
using (var interestCatalogScope = app.Services.CreateScope())
{
    var interestCatalogDb = interestCatalogScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var interestCatalogStrategy = interestCatalogDb.Database.CreateExecutionStrategy();

    // await interestCatalogStrategy.ExecuteAsync(async () =>
    // {
    //     await SeedData.EnsureInterestCatalogAsync(interestCatalogDb);
    // });
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { message = "An unexpected error occurred." });
        });
    });
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (forwardedHeadersEnabled)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseCors("ConfiguredOrigins");

app.UseResponseCompression();

// HTTPS can be terminated either by Kestrel itself or by a reverse proxy.
// Keep it config-driven so local HTTP development still works unchanged.
if (!app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Hsts:Enabled"))
{
    app.UseHsts();
}

if (app.Configuration.GetValue<bool>("HttpsRedirection:Enabled"))
{
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (!context.Request.Path.StartsWithSegments("/swagger"))
        {
            ApplySecurityHeaders(context.Response.Headers);
        }

        return Task.CompletedTask;
    });

    await next();
});

var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(webRoot))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

var mediaRoot = Path.Combine(app.Environment.ContentRootPath, "media");

Directory.CreateDirectory(Path.Combine(mediaRoot, "seed"));
Directory.CreateDirectory(Path.Combine(mediaRoot, "uploads", "avatars"));
Directory.CreateDirectory(Path.Combine(mediaRoot, "uploads", "contributions"));
Directory.CreateDirectory(Path.Combine(mediaRoot, "uploads", "regional"));

var mediaContentTypes = new FileExtensionContentTypeProvider();
mediaContentTypes.Mappings[".svg"] = "image/svg+xml";
mediaContentTypes.Mappings[".webp"] = "image/webp";

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mediaRoot),
    RequestPath = "/media",
    ContentTypeProvider = mediaContentTypes,
    OnPrepareResponse = context =>
    {
        var headers = context.Context.Response.Headers;
        headers[HeaderNames.XContentTypeOptions] = "nosniff";

        if (context.Context.Request.Path.StartsWithSegments("/media/uploads/avatars"))
        {
            headers[HeaderNames.CacheControl] = "public, max-age=86400, immutable";
            return;
        }

        if (context.Context.Request.Path.StartsWithSegments("/media/uploads/contributions"))
        {
            headers[HeaderNames.CacheControl] = "no-store";
            headers["X-Robots-Tag"] = "noindex, noarchive, nosnippet";
            return;
        }

        if (context.Context.Request.Path.StartsWithSegments("/media/uploads/regional"))
        {
            headers[HeaderNames.CacheControl] = "no-store";
            headers["X-Robots-Tag"] = "noindex, noarchive, nosnippet";
            return;
        }

        if (context.Context.Request.Path.StartsWithSegments("/media/seed"))
        {
            headers[HeaderNames.CacheControl] = "public, max-age=604800, immutable";
            return;
        }

        headers[HeaderNames.CacheControl] = "no-store";
    }
});

app.Use(async (context, next) =>
{
    var path = context.Request.Path;

    var isAdminSpaRequest =
        HttpMethods.IsGet(context.Request.Method) &&
        (
            path.Equals("/admin", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/admin/", StringComparison.OrdinalIgnoreCase) ||
            (
                path.StartsWithSegments("/admin", out var adminRemainingPath) &&
                !Path.HasExtension(adminRemainingPath.Value)
            )
        );

    if (isAdminSpaRequest)
    {
        var adminIndexPath = Path.Combine(
            app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
            "admin",
            "index.html");

        if (!File.Exists(adminIndexPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Admin index.html not found.");
            return;
        }

        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(adminIndexPath);
        return;
    }

    await next();
});

app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? context.User.FindFirstValue("userId");

        if (!int.TryParse(userIdValue, out var userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Invalid user session." });
            return;
        }

        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var accountState = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.IsSuspended, user.IsDeleted })
            .SingleOrDefaultAsync(context.RequestAborted);

        if (accountState is null || accountState.IsDeleted)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Invalid user session." });
            return;
        }

        if (accountState.IsSuspended)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "Account is suspended." });
            return;
        }
    }

    await next();
});
app.UseAuthorization();

app.UseRateLimiter();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = WriteHealthCheckResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthCheckResponse
});

app.MapControllers();

if (File.Exists(Path.Combine(webRoot, "index.html")))
{
    app.MapFallbackToFile("index.html");
}

app.Run();

static bool ConfigureForwardedHeaders(WebApplicationBuilder builder)
{
    var enabled = builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");
    if (!enabled)
    {
        return false;
    }

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto |
            ForwardedHeaders.XForwardedHost;

        options.ForwardLimit = Math.Max(
            1,
            builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1);

        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
        {
            if (IPAddress.TryParse(proxy, out var proxyAddress))
            {
                options.KnownProxies.Add(proxyAddress);
            }
        }

        foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? Array.Empty<string>())
        {
            if (TryParseNetwork(network, out var ipNetwork))
            {
                options.KnownNetworks.Add(ipNetwork);
            }
        }
    });

    return true;
}

static string GetUserOrIpRateLimitPartition(HttpContext httpContext)
{
    var userId = httpContext.User.FindFirst("userId")?.Value;

    if (!string.IsNullOrWhiteSpace(userId))
    {
        return $"user:{userId}";
    }

    return $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

static string GetUserAndIpRateLimitPartition(HttpContext httpContext)
{
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var userId = httpContext.User.FindFirst("userId")?.Value
        ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!string.IsNullOrWhiteSpace(userId))
    {
        return $"user:{userId}:ip:{ipAddress}";
    }

    return $"ip:{ipAddress}";
}

static bool IsDemoContentSeedEnabledForEnvironment(IHostEnvironment environment, IConfiguration configuration)
{
    var configuredEnabled =
        configuration.GetValue<bool?>("Seed:EnableDemoContentSeed")
        ?? configuration.GetValue<bool?>("Seed:Enabled")
        ?? configuration.GetValue<bool?>("SeedData:Enabled");

    var enabled = configuredEnabled ?? environment.IsDevelopment();
    if (!enabled)
    {
        return false;
    }

    return environment.IsDevelopment()
        || configuration.GetValue<bool>("Seed:RunInProduction");
}

static bool IsPublicContentSeedEnabledForEnvironment(IHostEnvironment environment, IConfiguration configuration)
{
    var enabled = configuration.GetValue<bool>("Seed:EnablePublicContentSeed");
    if (!enabled)
    {
        return false;
    }

    return environment.IsDevelopment()
        || configuration.GetValue<bool>("Seed:RunPublicContentInProduction")
        || configuration.GetValue<bool>("Seed:RunInProduction");
}

static IReadOnlyList<string> ResolveSeedAdm1SyncCountryIso3s(IConfiguration configuration)
{
    var configuredList = configuration
        .GetSection("Seed:SyncAdm1Regions:CountryIso3s")
        .Get<string[]>();

    var values = configuredList is { Length: > 0 }
        ? configuredList
        : (configuration["Seed:SyncAdm1Regions:CountryIso3"] ?? "HRV,SRB")
            .Split(',', ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    return values
        .Select(value => value.Trim().ToUpperInvariant())
        .Where(value => value.Length == 3 && value.All(ch => ch is >= 'A' and <= 'Z'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static bool TryParseNetwork(string value, out Microsoft.AspNetCore.HttpOverrides.IPNetwork network)
{
    network = default!;

    var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    if (parts.Length != 2 ||
        !IPAddress.TryParse(parts[0], out var address) ||
        !int.TryParse(parts[1], out var prefixLength))
    {
        return false;
    }

    network = new Microsoft.AspNetCore.HttpOverrides.IPNetwork(address, prefixLength);
    return true;
}

static void ApplySecurityHeaders(IHeaderDictionary headers)
{
    headers[HeaderNames.XContentTypeOptions] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "geolocation=(self), microphone=(), camera=()";

    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "base-uri 'self'; " +
        "frame-ancestors 'none'; " +
        "form-action 'self'; " +
        "object-src 'none'; " +
        "img-src 'self' data: blob: https:; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' data: https://fonts.gstatic.com https://klokantech.github.io; " +
        "connect-src 'self' https:; " +
        "worker-src 'self' blob:; " +
        "script-src 'self';";
}

static Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description
        })
    });
}

public partial class Program;
