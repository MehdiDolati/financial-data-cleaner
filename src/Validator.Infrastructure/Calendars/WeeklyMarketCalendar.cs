using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Abstractions;
using Validator.Domain.Calendars;

namespace Validator.Infrastructure.Calendars
{
    public class WeeklyMarketCalendar : Validator.Application.Abstractions.IMarketCalendar
    {
        private readonly HashSet<DayOfWeek> _openDays;
        private readonly int _openHourUtc;
        private readonly int _closeHourUtc;

        public WeeklyMarketCalendar(IEnumerable<DayOfWeek> openDays, int openHourUtc, int closeHourUtc)
        {
            _openDays = new HashSet<DayOfWeek>(openDays);
            _openHourUtc = openHourUtc;
            _closeHourUtc = closeHourUtc;
        }

        public MarketProfile Profile => MarketProfile.Custom; // derived from JSON config

        public bool IsOpen(DateTimeOffset timestamp)
        {
            // Normalize to UTC
            var utc = timestamp.ToOffset(TimeSpan.Zero);
            if (!_openDays.Contains(utc.DayOfWeek))
                return false;

            var hour = utc.Hour;
            // inclusive openHour, exclusive closeHour
            return hour >= _openHourUtc && hour < _closeHourUtc;
        }
    }
}
