using System.Text.Json;
using Geotrack.Infrastructure.Services;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class OperatingHoursStatusCalculatorTests
{
    [Fact]
    public void Evaluates_regular_hours_in_belgrade_time()
    {
        var json = Hours(("Monday", true, "09:00", "17:00"));

        Assert.Equal(OperatingHoursStatus.Open,
            OperatingHoursStatusCalculator.Evaluate(true, json, Utc(2026, 6, 8, 10)));
        Assert.Equal(OperatingHoursStatus.Closed,
            OperatingHoursStatusCalculator.Evaluate(true, json, Utc(2026, 6, 8, 16)));
    }

    [Fact]
    public void Treats_midnight_to_midnight_as_open_24_hours()
    {
        var json = Hours(("Monday", true, "00:00", "00:00"));

        Assert.Equal(OperatingHoursStatus.Open,
            OperatingHoursStatusCalculator.Evaluate(true, json, Utc(2026, 6, 8, 12)));
    }

    [Fact]
    public void Carries_previous_days_overnight_hours_past_midnight()
    {
        var json = Hours(
            ("Monday", true, "20:00", "02:00"),
            ("Tuesday", false, null, null));

        Assert.Equal(OperatingHoursStatus.Open,
            OperatingHoursStatusCalculator.Evaluate(true, json, Utc(2026, 6, 8, 23)));
        Assert.Equal(OperatingHoursStatus.Closed,
            OperatingHoursStatusCalculator.Evaluate(true, json, Utc(2026, 6, 9, 1)));
    }

    [Fact]
    public void Does_not_open_todays_overnight_window_before_its_start()
    {
        var json = Hours(
            ("Monday", false, null, null),
            ("Tuesday", true, "20:00", "02:00"));

        Assert.Equal(OperatingHoursStatus.Closed,
            OperatingHoursStatusCalculator.Evaluate(true, json, Utc(2026, 6, 8, 23)));
    }

    [Fact]
    public void Uses_inclusive_opening_and_exclusive_closing_boundaries()
    {
        var json = Hours(("Monday", true, "09:00", "17:00"));

        Assert.Equal(OperatingHoursStatus.Open,
            OperatingHoursStatusCalculator.Evaluate(true, json, Utc(2026, 6, 8, 7)));
        Assert.Equal(OperatingHoursStatus.Closed,
            OperatingHoursStatusCalculator.Evaluate(true, json, Utc(2026, 6, 8, 15)));
    }

    [Fact]
    public void Treats_midnight_as_the_end_of_an_overnight_window()
    {
        var json = Hours(
            ("Monday", true, "20:00", "00:00"),
            ("Tuesday", false, null, null));

        Assert.Equal(OperatingHoursStatus.Open,
            OperatingHoursStatusCalculator.Evaluate(true, json, Utc(2026, 6, 8, 21)));
        Assert.Equal(OperatingHoursStatus.Closed,
            OperatingHoursStatusCalculator.Evaluate(true, json, Utc(2026, 6, 8, 22)));
    }

    private static DateTime Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static string Hours(params (string Day, bool IsOpen, string? Open, string? Close)[] values) =>
        JsonSerializer.Serialize(values.Select(value => new
        {
            dayOfWeek = value.Day,
            isOpen = value.IsOpen,
            openTime = value.Open,
            closeTime = value.Close
        }));
}
