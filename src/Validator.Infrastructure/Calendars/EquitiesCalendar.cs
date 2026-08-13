using System;
using Validator.Application.Abstractions;
using Validator.Domain.Calendars;

namespace Validator.Infrastructure.Calendars
{
    public class EquitiesCalendar : Validator.Application.Abstractions.IMarketCalendar
    {
        // Simplified: open Monday-Friday (UTC); closed on Saturday/Sunday
        public MarketProfile Profile => MarketProfile.Equities;

        public bool IsOpen(DateTimeOffset timestamp)
        {
            var utc = timestamp.ToOffset(TimeSpan.Zero);
            var day = utc.DayOfWeek;
            if (day == DayOfWeek.Saturday || day == DayOfWeek.Sunday)
                return false;

            // For now, assume market hours cover the whole weekday to keep test simple
            return true;
        }
    }
}
