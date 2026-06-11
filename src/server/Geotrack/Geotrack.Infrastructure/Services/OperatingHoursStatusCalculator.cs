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

        var yesterdayHours = FindDay(hours, localNow.AddDays(-1).DayOfWeek).ToList();
        if (yesterdayHours.Any(entry => IsOpenAt(entry, now, fromPreviousDay: true)))
            return OperatingHoursStatus.Open;

        var todayHours = FindDay(hours, localNow.DayOfWeek).ToList();
        if (todayHours.Count == 0)
            return null;
        if (todayHours.All(hour => !hour.IsOpen))
            return OperatingHoursStatus.Closed;

        var isOpen = todayHours.Any(entry => IsOpenAt(entry, now, fromPreviousDay: false));
        return isOpen ? OperatingHoursStatus.Open : OperatingHoursStatus.Closed;
    }

    private static bool IsOpenAt(OperatingHourEntry entry, TimeSpan now, bool fromPreviousDay)
    {
        if (!entry.IsOpen || !TryReadTimes(entry, out var openTime, out var closeTime))
            return false;

        if (openTime == closeTime)
            return openTime == TimeSpan.Zero;

        if (closeTime < openTime)
            return fromPreviousDay
                ? now < closeTime
                : now >= openTime;

        return !fromPreviousDay && now >= openTime && now < closeTime;
    }

    private static IEnumerable<OperatingHourEntry> FindDay(IEnumerable<OperatingHourEntry> hours, DayOfWeek day) =>
        hours.Where(hour =>
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
