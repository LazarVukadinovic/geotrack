namespace Geotrack.Application.Analytics;

public sealed record AnalyticsProcessingJob(
    int UserId,
    IReadOnlyList<int> AnalyticsEventIds);
