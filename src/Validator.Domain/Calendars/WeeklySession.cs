using System;
namespace Validator.Domain.Calendars
{
    public sealed record WeeklySession
    {
        public DayOfWeek OpenDay { get; }
        public TimeSpan OpenTime { get; }
        public DayOfWeek CloseDay { get; }
        public TimeSpan CloseTime { get; }

        public DayOfWeek Day => OpenDay;
        public TimeSpan Open => OpenTime;
        public TimeSpan Close => CloseTime;

        public WeeklySession(DayOfWeek day, TimeSpan open, TimeSpan close)
            : this(day, open, day, close)
        {
        }

        public WeeklySession(
            DayOfWeek openDay,
            TimeSpan openTime,
            DayOfWeek closeDay,
            TimeSpan closeTime)
        {
            ValidateTime(openTime, nameof(openTime));
            ValidateTime(closeTime, nameof(closeTime));

            if (GetDuration(openDay, openTime, closeDay, closeTime) <= TimeSpan.Zero)
            {
                throw new ArgumentException("WeeklySession must have Open < Close to represent a non-empty interval.");
            }

            OpenDay = openDay;
            OpenTime = openTime;
            CloseDay = closeDay;
            CloseTime = closeTime;
        }

        public bool Overlaps(WeeklySession other)
        {
            if (other is null)
            {
                return false;
            }

            var week = TimeSpan.FromDays(7);
            var thisStart = GetWeekOffset(OpenDay, OpenTime);
            var thisEnd = thisStart + GetDuration(OpenDay, OpenTime, CloseDay, CloseTime);
            var otherStart = GetWeekOffset(other.OpenDay, other.OpenTime);
            var otherEnd = otherStart + GetDuration(other.OpenDay, other.OpenTime, other.CloseDay, other.CloseTime);

            return Intersects(thisStart, thisEnd, otherStart, otherEnd) ||
                   Intersects(thisStart, thisEnd, otherStart - week, otherEnd - week) ||
                   Intersects(thisStart, thisEnd, otherStart + week, otherEnd + week);
        }

        public int DaysUntilClose => ((int)CloseDay - (int)OpenDay + 7) % 7;

        private static TimeSpan GetDuration(
            DayOfWeek openDay,
            TimeSpan openTime,
            DayOfWeek closeDay,
            TimeSpan closeTime)
        {
            var days = ((int)closeDay - (int)openDay + 7) % 7;
            return days == 0 && closeTime <= openTime
                ? TimeSpan.Zero
                : TimeSpan.FromDays(days) + closeTime - openTime;
        }

        private static TimeSpan GetWeekOffset(DayOfWeek day, TimeSpan time) =>
            TimeSpan.FromDays((int)day) + time;

        private static bool Intersects(
            TimeSpan firstStart,
            TimeSpan firstEnd,
            TimeSpan secondStart,
            TimeSpan secondEnd) =>
            firstStart < secondEnd && secondStart < firstEnd;

        private static void ValidateTime(TimeSpan time, string parameterName)
        {
            if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
            {
                throw new ArgumentOutOfRangeException(parameterName, "Session times must be within one local day.");
            }
        }
    }
}