using Geotrack.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Geotrack.Server.Health;

public sealed class DatabaseReadinessHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;
    private readonly ILogger<DatabaseReadinessHealthCheck> _logger;

    public DatabaseReadinessHealthCheck(AppDbContext db, ILogger<DatabaseReadinessHealthCheck> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Database connection is available.")
                : HealthCheckResult.Unhealthy("Database connection is unavailable.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Readiness database check failed with {ExceptionType}.",
                ex.GetType().Name);
            return HealthCheckResult.Unhealthy("Database connection is unavailable.");
        }
    }
}
