using NodaTime;
using Validator.Application.Abstractions;
using Validator.Domain.Calendars;

namespace Validator.Infrastructure.Calendars;

public sealed class NodaTimeScheduleExpander : ITimeZoneScheduleExpander
{
    public ValueTask<ScheduleExpansion> ExpandAsync(
        string ianaTimeZoneId,
        IReadOnlyList<WeeklySession> sessions,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        if (fromUtc.Offset != TimeSpan.Zero || toUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Schedule expansion bounds must be UTC.");
        }

        if (toUtc < fromUtc)
        {
            throw new ArgumentException("Schedule expansion end must not precede its start.", nameof(toUtc));
        }

        var zone = GetZone(ianaTimeZoneId);
        var startDate = Instant.FromDateTimeOffset(fromUtc).InZone(zone).Date.PlusDays(-7);
        var endDate = Instant.FromDateTimeOffset(toUtc).InZone(zone).Date.PlusDays(7);
        var expanded = new List<UtcSession>();

        for (var date = startDate; date <= endDate; date = date.PlusDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var session in sessions)
            {
                if (ToDayOfWeek(date.DayOfWeek) != session.OpenDay)
                {
                    continue;
                }

                var open = ResolveBoundary(zone, date.At(ToLocalTime(session.OpenTime)));
                var closeDate = date.PlusDays(session.DaysUntilClose);
                var close = ResolveBoundary(zone, closeDate.At(ToLocalTime(session.CloseTime)));
                var utcSession = new UtcSession(
                    open.ToInstant().ToDateTimeOffset(),
                    close.ToInstant().ToDateTimeOffset());

                if (utcSession.CloseUtc > fromUtc && utcSession.OpenUtc <= toUtc)
                {
                    expanded.Add(utcSession);
                }
            }
        }

        return ValueTask.FromResult(
            new ScheduleExpansion(expanded.OrderBy(session => session.OpenUtc).ToArray()));
    }

    public void ValidateDefinition(string ianaTimeZoneId, IReadOnlyList<WeeklySession> sessions)
    {
        var zone = GetZone(ianaTimeZoneId);

        for (var year = 2000; year <= 2100; year++)
        {
            var date = new LocalDate(year, 1, 1);
            var end = new LocalDate(year, 12, 31);
            for (; date <= end; date = date.PlusDays(1))
            {
                foreach (var session in sessions)
                {
                    if (ToDayOfWeek(date.DayOfWeek) != session.OpenDay)
                    {
                        continue;
                    }

                    ResolveBoundary(zone, date.At(ToLocalTime(session.OpenTime)));
                    ResolveBoundary(
                        zone,
                        date.PlusDays(session.DaysUntilClose).At(ToLocalTime(session.CloseTime)));
                }
            }
        }
    }

    private static DateTimeZone GetZone(string timeZoneId) =>
        DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZoneId) ??
        throw new InvalidDataException($"Unknown IANA time zone '{timeZoneId}'.");

    private static ZonedDateTime ResolveBoundary(DateTimeZone zone, LocalDateTime boundary)
    {
        try
        {
            return zone.AtStrictly(boundary);
        }
        catch (AmbiguousTimeException exception)
        {
            throw new InvalidDataException(
                $"Calendar boundary '{boundary}' is ambiguous in time zone '{zone.Id}'.",
                exception);
        }
        catch (SkippedTimeException exception)
        {
            throw new InvalidDataException(
                $"Calendar boundary '{boundary}' is skipped in time zone '{zone.Id}'.",
                exception);
        }
    }

    private static LocalTime ToLocalTime(TimeSpan value) =>
        new(value.Hours, value.Minutes, value.Seconds);

    private static DayOfWeek ToDayOfWeek(IsoDayOfWeek value) =>
        value == IsoDayOfWeek.Sunday ? DayOfWeek.Sunday : (DayOfWeek)(int)value;
}