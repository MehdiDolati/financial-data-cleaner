using Validator.Infrastructure.Calendars;

namespace Validator.Infrastructure.Tests.Calendars;

public sealed class EquitiesCalendarTests
{
    [Theory]
    [InlineData(2026, 3, 6, 14, 29, false)]
    [InlineData(2026, 3, 6, 14, 30, true)]
    [InlineData(2026, 3, 6, 20, 59, true)]
    [InlineData(2026, 3, 6, 21, 0, false)]
    [InlineData(2026, 3, 9, 13, 29, false)]
    [InlineData(2026, 3, 9, 13, 30, true)]
    [InlineData(2026, 3, 9, 19, 59, true)]
    [InlineData(2026, 3, 9, 20, 0, false)]
    public void IsOpen_UsesNewYorkSessionOnBothSidesOfDst(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        bool expected)
    {
        var calendar = new EquitiesCalendar();
        var timestamp = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);

        Assert.Equal(expected, calendar.IsOpen(timestamp));
    }

    [Fact]
    public void IsOpen_RejectsWeekendEvenDuringSessionHours()
    {
        var calendar = new EquitiesCalendar();

        Assert.False(calendar.IsOpen(new DateTimeOffset(2026, 3, 7, 15, 0, 0, TimeSpan.Zero)));
    }
}
