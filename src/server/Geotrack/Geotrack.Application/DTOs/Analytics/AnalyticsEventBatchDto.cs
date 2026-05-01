namespace Geotrack.Application.DTOs.Analytics;

public class AnalyticsEventBatchDto
{
    public Guid? AnonymousSessionId { get; set; }

    public List<AnalyticsEventDto> Events { get; set; } = new();
}
