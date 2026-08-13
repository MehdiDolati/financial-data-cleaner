using System;

namespace Validator.Infrastructure.Calendars
{
    public interface IMarketCalendar
    {
        bool IsOpen(DateTimeOffset timestamp);
    }
}
