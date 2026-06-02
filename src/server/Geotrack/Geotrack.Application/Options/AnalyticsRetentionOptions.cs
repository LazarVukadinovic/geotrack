namespace Geotrack.Application.Options;

public class AnalyticsRetentionOptions
{
    public int RetentionDays { get; set; } = 180;
    public int BatchSize { get; set; } = 1_000;
}
