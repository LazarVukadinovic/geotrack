namespace Geotrack.Application.DTOs.Analytics;

public sealed class AnalyticsRetentionCleanupResultDto
{
    public int RetentionDays { get; set; }
    public DateTime CutoffUtc { get; set; }
    public int DeletedAnalyticsEvents { get; set; }
    public int DeletedAnonymousSessions { get; set; }
    public bool Execute { get; set; }
}
