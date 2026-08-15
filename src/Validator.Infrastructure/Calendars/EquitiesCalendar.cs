using Validator.Application.Abstractions;
using Validator.Domain.Calendars;

namespace Validator.Infrastructure.Calendars;

public sealed class EquitiesCalendar : Validator.Application.Abstractions.IMarketCalendar
{
    private static readonly MarketCalendarDefinition Definition = new(
        MarketProfile.Equities,
        1,
        "US Equities",
        "America/New_York",
        CreateSessions());

    private readonly WeeklyMarketCalendar _calendar;

    public EquitiesCalendar(ITimeZoneScheduleExpander? expander = null)
    {
        _calendar = new WeeklyMarketCalendar(Definition, expander);
    }

    public MarketProfile Profile => MarketProfile.Equities;

    public bool IsOpen(DateTimeOffset timestamp) => _calendar.IsOpen(timestamp);

    private static IEnumerable<WeeklySession> CreateSessions()
    {
        var open = new TimeSpan(9, 30, 0);
        var close = new TimeSpan(16, 0, 0);
        yield return new WeeklySession(DayOfWeek.Monday, open, close);
        yield return new WeeklySession(DayOfWeek.Tuesday, open, close);
        yield return new WeeklySession(DayOfWeek.Wednesday, open, close);
        yield return new WeeklySession(DayOfWeek.Thursday, open, close);
        yield return new WeeklySession(DayOfWeek.Friday, open, close);
    }
}
