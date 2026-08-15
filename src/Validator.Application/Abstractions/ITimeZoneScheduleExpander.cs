using Validator.Domain.Calendars;

namespace Validator.Application.Abstractions;

public interface ITimeZoneScheduleExpander
{
    ValueTask<ScheduleExpansion> ExpandAsync(
        string ianaTimeZoneId,
        IReadOnlyList<WeeklySession> sessions,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);
}

public sealed record ScheduleExpansion(IReadOnlyList<UtcSession> Sessions);