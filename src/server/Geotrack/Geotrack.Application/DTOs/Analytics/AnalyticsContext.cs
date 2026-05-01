namespace Geotrack.Application.DTOs.Analytics;

/// <summary>
/// Internal record carrying server-derived enrichment context for an
/// analytics batch. Constructed by <c>AnalyticsController</c> from the JWT
/// and request headers; never trusted from request body.
/// </summary>
public sealed record AnalyticsContext(
    int? UserId,
    Guid? AnonymousSessionId,
    string UserAgentFamily,
    string? CoarseRegion,
    string? CoarseCountry);
