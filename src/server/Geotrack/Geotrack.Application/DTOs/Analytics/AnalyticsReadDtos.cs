namespace Geotrack.Application.DTOs.Analytics;

public sealed record AnalyticsTopEntityDto(
    string EventType,
    string? EntityType,
    int? EntityId,
    int Count,
    DateTime LastOccurredAt);

public sealed record AnalyticsTopDestinationDto(
    int DestinationId,
    int Count,
    DateTime LastOccurredAt);

public sealed record AnalyticsOutboundProviderClickDto(
    string Provider,
    string? EntityType,
    int? EntityId,
    int Count,
    DateTime LastOccurredAt);

public sealed record AnalyticsSearchTrendDto(
    string? Route,
    int Count,
    DateTime LastOccurredAt);
