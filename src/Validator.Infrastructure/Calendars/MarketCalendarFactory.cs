using System;
using System.IO;

namespace Validator.Infrastructure.Calendars
{
    public class MarketCalendarFactory
    {
            public Validator.Application.Abstractions.IMarketCalendar Create(string nameOrPath)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath))
                throw new ArgumentException("Calendar name or path must be provided", nameof(nameOrPath));

            // If it's a file path, attempt to load JSON calendar
            if (File.Exists(nameOrPath))
            {
                var loader = new CalendarJsonLoader();
                return loader.Load(nameOrPath);
            }

            if (string.Equals(nameOrPath, "equities", StringComparison.OrdinalIgnoreCase))
                return new EquitiesCalendar();

            if (string.Equals(nameOrPath, "forex", StringComparison.OrdinalIgnoreCase))
                return new ForexCalendar();

            throw new ArgumentException($"Unknown market calendar: {nameOrPath}", nameof(nameOrPath));
        }
    }
}
