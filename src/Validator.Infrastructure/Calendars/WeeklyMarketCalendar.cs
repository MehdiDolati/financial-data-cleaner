using Validator.Application.Abstractions;
using Validator.Domain.Calendars;

namespace Validator.Infrastructure.Calendars;

public sealed class WeeklyMarketCalendar : Validator.Application.Abstractions.IMarketCalendar
{
    private readonly MarketCalendarDefinition _definition;
    private readonly ITimeZoneScheduleExpander _expander;

    public WeeklyMarketCalendar(
        MarketCalendarDefinition definition,
        ITimeZoneScheduleExpander? expander = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _expander = expander ?? new NodaTimeScheduleExpander();
    }

    public MarketProfile Profile => _definition.Profile;

    public bool IsOpen(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        var expansion = _expander.ExpandAsync(
            _definition.TimeZoneId,
            _definition.Sessions,
            utc,
            utc).GetAwaiter().GetResult();

        return expansion.Sessions.Any(session => session.Contains(utc));
    }
}
