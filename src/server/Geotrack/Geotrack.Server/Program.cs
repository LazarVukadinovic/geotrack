using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Services;
using Geotrack.Infrastructure;
using Geotrack.Infrastructure.Repositories;
using Geotrack.Infrastructure.Services;
using Geotrack.Server;
using Geotrack.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is required. Set via user-secrets, environment variable, or appsettings.*.json.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
    connectionString,
    npgsqlOptions => npgsqlOptions.UseNetTopologySuite()));
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddMemoryCache();
builder.Services.AddEndpointsApiExplorer();
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
builder.Services.AddScoped<IExploreService, ExploreService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IPlaceContributionService, PlaceContributionService>();
builder.Services.AddScoped<IPlaceContributionRepository, PlaceContributionRepository>();
builder.Services.AddScoped<IUserNotificationService, UserNotificationService>();
builder.Services.AddScoped<IPlaceContributionRepository, PlaceContributionRepository>();
builder.Services.AddHttpClient<IClientLocationResolverService, ClientLocationResolverService>();
builder.Services.AddHttpClient<IMapService, MapService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GeoTrack/1.0");
});
builder.Services.AddScoped<IUserAvatarStorageService, LocalUserAvatarStorageService>();
builder.Services.AddScoped<IContributionMediaStorageService, LocalContributionMediaStorageService>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

var forwardedHeadersEnabled = ConfigureForwardedHeaders(builder);

// builder.Services.AddScoped<UserService>();
// builder.Services.AddScoped<RoleService>();
// builder.Services.AddScoped<DestinationService>();

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException(
        "Jwt:Key is required. Set via user-secrets, environment variable, or appsettings.*.json.");
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (ctx, token) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many requests. Please try again later." }, token);
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
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    // Analytics ingestion: partition by authenticated user, then anonymous
    // session id, then IP as a last resort. 60 batches/minute is generous
    // given the 5-second client-side flush window.
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
    options.AddPolicy("AllowFrontendDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

await PostgisStartup.EnsureReadyAsync(app.Services, app.Environment.IsDevelopment());

// Auto-apply migrations and seed data in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.InitializeAsync(db);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
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
    app.UseCors("AllowFrontendDev");
}

if (forwardedHeadersEnabled)
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
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

var mediaRoot = Path.Combine(app.Environment.ContentRootPath, "media");
Directory.CreateDirectory(Path.Combine(mediaRoot, "seed"));
Directory.CreateDirectory(Path.Combine(mediaRoot, "uploads", "avatars"));
Directory.CreateDirectory(Path.Combine(mediaRoot, "uploads", "contributions"));

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

        if (context.Context.Request.Path.StartsWithSegments("/media/seed"))
        {
            headers[HeaderNames.CacheControl] = "public, max-age=604800, immutable";
            return;
        }

        headers[HeaderNames.CacheControl] = "no-store";
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers();

app.Run();

static bool ConfigureForwardedHeaders(WebApplicationBuilder builder)
{
    var enabled = builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");
    if (!enabled)
        return false;

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto |
            ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = Math.Max(1, builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1);
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
    headers["Cross-Origin-Opener-Policy"] = "same-origin";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "base-uri 'self'; " +
        "frame-ancestors 'none'; " +
        "form-action 'self'; " +
        "object-src 'none'; " +
        "img-src 'self' data: blob: https://tiles.openfreemap.org https://klokantech.github.io https://maputnik.github.io https://orangemug.github.io; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' data: https://fonts.gstatic.com https://klokantech.github.io; " +
        "connect-src 'self' https://tiles.openfreemap.org https://klokantech.github.io https://maputnik.github.io https://orangemug.github.io; " +
        "worker-src 'self' blob:; " +
        "script-src 'self';";
}

public partial class Program;
