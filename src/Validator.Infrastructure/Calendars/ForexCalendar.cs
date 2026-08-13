using System;
using Validator.Application.Abstractions;
using Validator.Domain.Calendars;

namespace Validator.Infrastructure.Calendars
{
    public sealed class ForexCalendar : IMarketCalendar
    {
        public MarketProfile Profile => MarketProfile.Forex;

        public bool IsOpen(DateTimeOffset timestamp)
        {
            var utc = timestamp.ToUniversalTime();
            var day = utc.DayOfWeek;
            var time = utc.TimeOfDay;

            if (day == DayOfWeek.Saturday)
            {
                return false;
            }

            if (day == DayOfWeek.Sunday)
            {
                return time >= TimeSpan.FromHours(22);
            }

            if (day == DayOfWeek.Friday)
            {
                return time < TimeSpan.FromHours(22);
            }

            return true;
        }
    }
}