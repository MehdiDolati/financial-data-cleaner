using System;
using System.Collections.Generic;
using System.Linq;

namespace Validator.Domain.Calendars
{
    // Represents a weekly recurring session on a single day with [Open, Close) semantics
    public sealed class WeeklySession
    {
        public DayOfWeek Day { get; }
        public TimeSpan Open { get; }
        public TimeSpan Close { get; }

        public WeeklySession(DayOfWeek day, TimeSpan open, TimeSpan close)
        {
            if (open >= close)
                throw new ArgumentException("WeeklySession must have Open < Close to represent a non-empty interval.");

            Day = day;
            Open = open;
            Close = close;
        }

        public bool Overlaps(WeeklySession other)
        {
            if (other is null) return false;
            if (other.Day != Day) return false;
            return !(Close <= other.Open || Open >= other.Close);
        }
    }
}