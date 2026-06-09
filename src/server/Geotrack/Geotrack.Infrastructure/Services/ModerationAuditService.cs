using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Reviews;
using Microsoft.AspNetCore.Http;

namespace Geotrack.Infrastructure.Services;

public sealed class ModerationAuditService : IModerationAuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ModerationAuditService(
        AppDbContext db,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task RecordAsync(
        int moderatorUserId,
        string targetType,
        int targetId,
        string action,
        string? previousStatus,
        string? newStatus,
        string? reason = null,
        string? internalNote = null,
        string? sourceArea = null,
        string? metadataJson = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        _db.ModerationAuditLogs.Add(new ModerationAuditLog
        {
            ModeratorUserId = moderatorUserId,
            TargetType = NormalizeRequired(targetType, 80),
            TargetId = targetId,
            Action = NormalizeRequired(action, 80),
            PreviousStatus = NormalizeOptional(previousStatus, 80),
            NewStatus = NormalizeOptional(newStatus, 80),
            Reason = NormalizeOptional(reason, 1000),
            InternalNote = NormalizeOptional(internalNote, 2000),
            CreatedAt = DateTime.UtcNow,
            CorrelationId = NormalizeOptional(correlationId ?? ResolveCorrelationId(), 128),
            SourceArea = NormalizeOptional(sourceArea, 80),
            MetadataJson = NormalizeOptional(metadataJson, 4000)
        });

        return Task.CompletedTask;
    }

    private string? ResolveCorrelationId()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
            return null;

        if (context.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId) &&
            !string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId.ToString();
        }

        if (context.Request.Headers.TryGetValue("X-Request-ID", out var requestId) &&
            !string.IsNullOrWhiteSpace(requestId))
        {
            return requestId.ToString();
        }

        return context.TraceIdentifier;
    }

    private static string NormalizeRequired(string value, int maxLength)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
            throw new InvalidOperationException("Audit value is required.");

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
