using System;
using Validator.Domain.Calendars;

namespace Validator.Application.Abstractions
{
    public interface IMarketCalendar
    {
        MarketProfile Profile { get; }
        bool IsOpen(DateTimeOffset timestamp);
    }
}