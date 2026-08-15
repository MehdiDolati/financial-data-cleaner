using System;

namespace Validator.Application.Reporting
{
    public sealed record DateRange
    {
        public DateTimeOffset Start { get; init; }
        public DateTimeOffset End { get; init; }

        public DateRange(DateTimeOffset start, DateTimeOffset end)
        {
            if (end < start) throw new ArgumentException("End must be >= Start");
            Start = start;
            End = end;
        }
    }
}