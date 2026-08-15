using System.Text;
using Validator.Domain.Calendars;
using Validator.Infrastructure.Calendars;

namespace Validator.Infrastructure.Tests.Calendars;

public sealed class CalendarJsonLoaderTests
{
    [Fact]
    public void Load_ValidSchema_ReturnsCustomCalendarWithHalfOpenSessions()
    {
        var path = WriteCalendar("""
            {
              "version": 1,
              "name": "Weekday UTC Session",
              "timeZone": "UTC",
              "sessions": [
                { "openDay": "Wednesday", "openTime": "09:00", "closeDay": "Wednesday", "closeTime": "17:00" }
              ]
            }
            """);

        var calendar = new CalendarJsonLoader().Load(path, MarketProfile.Custom);

        Assert.Equal(MarketProfile.Custom, calendar.Profile);
        Assert.False(calendar.IsOpen(Utc(2026, 2, 4, 8, 59)));
        Assert.True(calendar.IsOpen(Utc(2026, 2, 4, 9, 0)));
        Assert.True(calendar.IsOpen(Utc(2026, 2, 4, 16, 59)));
        Assert.False(calendar.IsOpen(Utc(2026, 2, 4, 17, 0)));
    }

    [Theory]
    [InlineData("{\"version\":2,\"name\":\"x\",\"timeZone\":\"UTC\",\"sessions\":[{\"openDay\":\"Monday\",\"openTime\":\"09:00\",\"closeDay\":\"Monday\",\"closeTime\":\"17:00\"}]}", "version")]
    [InlineData("{\"version\":1,\"name\":\"x\",\"timeZone\":\"UTC\",\"sessions\":[]}", "sessions")]
    [InlineData("{\"version\":1,\"name\":\"x\",\"timeZone\":\"UTC\",\"sessions\":[{\"openDay\":\"Monday\",\"openTime\":\"09:00\",\"closeDay\":\"Monday\",\"closeTime\":\"17:00\",\"extra\":true}]}", "extra")]
    [InlineData("{not-json", "JSON")]
    public void Load_SchemaViolation_ThrowsActionableConfigurationError(string json, string expectedMessage)
    {
        var path = WriteCalendar(json);

        var error = Assert.Throws<InvalidDataException>(
            () => new CalendarJsonLoader().Load(path, MarketProfile.Custom));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_OverlappingSessions_ThrowsConfigurationError()
    {
        var path = WriteCalendar("""
            {
              "version": 1,
              "name": "Overlap",
              "timeZone": "UTC",
              "sessions": [
                { "openDay": "Monday", "openTime": "09:00", "closeDay": "Monday", "closeTime": "12:00" },
                { "openDay": "Monday", "openTime": "11:00", "closeDay": "Monday", "closeTime": "13:00" }
              ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(
            () => new CalendarJsonLoader().Load(path, MarketProfile.Custom));

        Assert.Contains("overlap", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_UnknownTimeZone_ThrowsConfigurationError()
    {
        var path = WriteCalendar("""
            {
              "version": 1,
              "name": "Unknown Zone",
              "timeZone": "Not/A_Real_Zone",
              "sessions": [
                { "openDay": "Monday", "openTime": "09:00", "closeDay": "Monday", "closeTime": "17:00" }
              ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(
            () => new CalendarJsonLoader().Load(path, MarketProfile.Custom));

        Assert.Contains("time zone", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_AmbiguousLocalBoundary_ThrowsConfigurationError()
    {
        var path = WriteCalendar("""
            {
              "version": 1,
              "name": "Ambiguous",
              "timeZone": "America/New_York",
              "sessions": [
                { "openDay": "Sunday", "openTime": "01:30", "closeDay": "Sunday", "closeTime": "01:45" }
              ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(
            () => new CalendarJsonLoader().Load(path, MarketProfile.Custom));

        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private static string WriteCalendar(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"validator-calendar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "calendar.json");
        File.WriteAllText(path, json, new UTF8Encoding(false, true));
        return path;
    }
}
