using System.Text.Json;

namespace Geotrack.Infrastructure.Services;

public enum OperatingHoursStatus
{
    Open,
    Closed
}

public static class OperatingHoursStatusCalculator
{
    private static readonly TimeZoneInfo AppTimeZone = ResolveAppTimeZone();

    public static OperatingHoursStatus? Evaluate(bool enabled, string? operatingHoursJson, DateTime? utcNow = null)
    {
        if (!enabled || string.IsNullOrWhiteSpace(operatingHoursJson))
            return null;

        List<OperatingHourEntry>? hours;
        try
        {
            hours = JsonSerializer.Deserialize<List<OperatingHourEntry>>(
                operatingHoursJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }

        if (hours is null || hours.Count == 0)
            return null;

        var effectiveUtc = utcNow ?? DateTime.UtcNow;
        if (effectiveUtc.Kind != DateTimeKind.Utc)
            effectiveUtc = DateTime.SpecifyKind(effectiveUtc, DateTimeKind.Utc);

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(effectiveUtc, AppTimeZone);
        var now = localNow.TimeOfDay;
        var yesterdayHours = FindDay(hours, localNow.AddDays(-1).DayOfWeek);

        if (yesterdayHours is { IsOpen: true } &&
            TryReadTimes(yesterdayHours, out var yesterdayOpen, out var yesterdayClose) &&
            yesterdayClose < yesterdayOpen &&
            now < yesterdayClose)
        {
            return OperatingHoursStatus.Open;
        }

        var todayHours = FindDay(hours, localNow.DayOfWeek);
        if (todayHours is null)
            return null;
        if (!todayHours.IsOpen)
            return OperatingHoursStatus.Closed;
        if (!TryReadTimes(todayHours, out var openTime, out var closeTime))
            return null;

        if (openTime == closeTime)
            return openTime == TimeSpan.Zero ? OperatingHoursStatus.Open : OperatingHoursStatus.Closed;

        var isOpen = closeTime < openTime
            ? now >= openTime
            : now >= openTime && now < closeTime;

        return isOpen ? OperatingHoursStatus.Open : OperatingHoursStatus.Closed;
    }

    private static OperatingHourEntry? FindDay(IEnumerable<OperatingHourEntry> hours, DayOfWeek day) =>
        hours.FirstOrDefault(hour =>
            string.Equals(hour.DayOfWeek, day.ToString(), StringComparison.OrdinalIgnoreCase));

    private static bool TryReadTimes(OperatingHourEntry entry, out TimeSpan openTime, out TimeSpan closeTime)
    {
        var hasOpenTime = TimeSpan.TryParse(entry.OpenTime, out openTime);
        var hasCloseTime = TimeSpan.TryParse(entry.CloseTime, out closeTime);
        return hasOpenTime && hasCloseTime;
    }

    private static TimeZoneInfo ResolveAppTimeZone()
    {
        foreach (var id in new[] { "Europe/Belgrade", "Central European Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    private sealed class OperatingHourEntry
    {
        public string? DayOfWeek { get; set; }
        public bool IsOpen { get; set; }
        public string? OpenTime { get; set; }
        public string? CloseTime { get; set; }
    }
}
