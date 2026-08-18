using System;

namespace Validator.Domain.Findings.Evidence
{
    // Concrete UTC boundary that classified a timestamp as closed, when known.
    public sealed record UtcBoundary
    {
        public DateTimeOffset ClosedFromUtc { get; }
        public DateTimeOffset NextOpenUtc { get; }

        public UtcBoundary(DateTimeOffset closedFromUtc, DateTimeOffset nextOpenUtc)
        {
            if (closedFromUtc.Offset != TimeSpan.Zero || nextOpenUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Boundary timestamps must be UTC.");
            }

            if (nextOpenUtc <= closedFromUtc)
            {
                throw new ArgumentException("Next open must follow the closed boundary.", nameof(nextOpenUtc));
            }

            ClosedFromUtc = closedFromUtc;
            NextOpenUtc = nextOpenUtc;
        }
    }

    // Evidence for one closed-market record: the selected calendar identity and
    // the concrete boundary or recurring closed rule that classified the row.
    public sealed record ClosedMarketRecordEvidence
    {
        public string MarketProfile { get; }
        public string CalendarName { get; }
        public string? CalendarTimeZone { get; }
        public string ClosedRule { get; }
        public UtcBoundary? Boundary { get; }

        public ClosedMarketRecordEvidence(
            string marketProfile,
            string calendarName,
            string closedRule,
            string? calendarTimeZone = null,
            UtcBoundary? boundary = null)
        {
            if (string.IsNullOrWhiteSpace(marketProfile))
            {
                throw new ArgumentException("Market profile must be a non-empty value.", nameof(marketProfile));
            }

            if (string.IsNullOrWhiteSpace(calendarName))
            {
                throw new ArgumentException("Calendar name must be a non-empty value.", nameof(calendarName));
            }

            if (string.IsNullOrWhiteSpace(closedRule))
            {
                throw new ArgumentException("Closed rule must be a non-empty value.", nameof(closedRule));
            }

            MarketProfile = marketProfile;
            CalendarName = calendarName;
            CalendarTimeZone = calendarTimeZone;
            ClosedRule = closedRule;
            Boundary = boundary;
        }
    }
}