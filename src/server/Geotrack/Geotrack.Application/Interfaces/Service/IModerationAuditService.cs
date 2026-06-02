namespace Geotrack.Application.Interfaces.Service;

public interface IModerationAuditService
{
    Task RecordAsync(
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
        CancellationToken cancellationToken = default);
}
