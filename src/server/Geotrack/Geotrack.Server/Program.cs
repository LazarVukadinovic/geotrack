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
using Geotrack.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

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
        npgsqlOptions => npgsqlOptions.UseNetTopologySuite());
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddMemoryCache();
builder.Services.AddEndpointsApiExplorer();
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.Configure<McpOptions>(builder.Configuration.GetSection("Mcp"));
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
builder.Services.AddScoped<IUserInterestSignalUpdater, UserInterestSignalUpdater>();

builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IModerationTermCache, ModerationTermCache>();
builder.Services.AddScoped<IModerationTermService, ModerationTermService>();
builder.Services.AddScoped<IReviewContentModerationService, ReviewContentModerationService>();
builder.Services.AddScoped<IReviewReportService, ReviewReportService>();

builder.Services.AddScoped<ITagService, TagService>();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<IApiKeyEncryptionService, ApiKeyEncryptionService>();
builder.Services.AddScoped<IAiProviderKeyService, AiProviderKeyService>();
builder.Services.AddScoped<IAgentQueryPlanner, AgentQueryPlanner>();
builder.Services.AddScoped<IGeoTrackToolService, GeoTrackToolService>();
builder.Services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
builder.Services.AddScoped<IAgentProviderRegistry, AgentProviderRegistry>();
builder.Services.AddHttpClient<GeminiAgentProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GeoTrack/1.0");
});
builder.Services.AddScoped<IAgentProvider>(serviceProvider => serviceProvider.GetRequiredService<GeminiAgentProvider>());
builder.Services.AddScoped<IAgentProvider>(serviceProvider =>
    new ConfiguredUnavailableAgentProvider(
        "openai",
        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>()));
builder.Services.AddScoped<IAgentProvider>(serviceProvider =>
    new ConfiguredUnavailableAgentProvider(
        "claude",
        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>()));
builder.Services.AddScoped<IExploreService, ExploreService>();
builder.Services.AddScoped<IExploreSearchService, ExploreSearchService>();
builder.Services.AddScoped<ISearchService, SearchService>();

builder.Services.AddScoped<IPlaceContributionService, PlaceContributionService>();
builder.Services.AddScoped<IPlaceContributionRepository, PlaceContributionRepository>();

builder.Services.AddScoped<IUserNotificationService, UserNotificationService>();
builder.Services.AddScoped<IUserNotificationRepository, UserNotificationRepository>();
builder.Services.AddScoped<IAdminProfileService, AdminProfileService>();
builder.Services.AddScoped<IRegionalDashboardService, RegionalDashboardService>();
builder.Services.AddScoped<ITerritoryDashboardService, TerritoryDashboardService>();
builder.Services.AddScoped<ITerritoryUserAdminService, TerritoryUserAdminService>();
builder.Services.AddScoped<IRegionalRequestQueueService, RegionalRequestQueueService>();
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

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IFavoriteRepository, FavoriteRepository>();
builder.Services.AddScoped<IReviewRepository, ReviewRepository>();
builder.Services.AddScoped<ITagRepository, TagRepository>();

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
    options.AddPolicy("RequireRegionalManager", policy =>
        policy.RequireRole(RoleNames.RegionalManager));
    options.AddPolicy("RequireTerritoryManager", policy =>
        policy.RequireRole(RoleNames.TerritoryManager, RoleNames.TerritoryAssociate));
    options.AddPolicy("RequireAdminAppUser", policy =>
        policy.RequireRole(RoleNames.TerritoryManager, RoleNames.TerritoryAssociate, RoleNames.RegionalManager));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (ctx, token) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
        {
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
        }

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
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

await PostgisStartup.EnsureReadyAsync(app.Services, app.Environment.IsDevelopment());

var shouldSeedDatabase =
    app.Environment.IsDevelopment()
    || app.Configuration.GetValue<bool>("SeedData:Enabled");
var shouldSeedDemoRegionalManager =
    DemoRegionalManagerSeeder.IsEnabled(app.Environment, app.Configuration);
var shouldSeedDemoTerritoryManager =
    DemoTerritoryManagerSeeder.IsEnabled(app.Environment, app.Configuration);

if (shouldSeedDatabase || shouldSeedDemoRegionalManager || shouldSeedDemoTerritoryManager)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await db.Database.MigrateAsync();

    if (shouldSeedDatabase)
    {
        await SeedData.InitializeAsync(db);
    }

    if (shouldSeedDemoRegionalManager)
    {
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DemoRegionalManagerSeeder));
        await DemoRegionalManagerSeeder.EnsureAsync(db, app.Configuration, logger);
    }

    if (shouldSeedDemoTerritoryManager)
    {
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DemoTerritoryManagerSeeder));
        await DemoTerritoryManagerSeeder.EnsureAsync(db, app.Configuration, logger);
    }
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

app.UseCors("AllowAllOrigins");

if (forwardedHeadersEnabled)
{
    app.UseForwardedHeaders();
}

// Local dev and PMF deployment run over plain HTTP. Enable this explicitly
// only for hosts where the ASP.NET app terminates HTTPS itself.
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
app.UseAuthorization();

app.UseRateLimiter();

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
        "script-src 'self' 'unsafe-inline';";
}

public partial class Program;
