using System;
using Validator.Infrastructure.Calendars;

namespace Validator.Infrastructure.Tests.Calendars
{
    public class EquitiesCalendarTests
    {
        [Fact]
        public void EquitiesMarket_IsClosed_OnWeekendAndOpenOnWeekday()
        {
            var factory = new MarketCalendarFactory();
            var calendar = factory.Create("equities");

            // Saturday midday should be closed
            var sat = new DateTimeOffset(2026, 1, 3, 12, 0, 0, TimeSpan.Zero);
            Assert.False(calendar.IsOpen(sat));

            // Wednesday 10:00 should be open
            var wed = new DateTimeOffset(2026, 1, 7, 10, 0, 0, TimeSpan.Zero);
            Assert.True(calendar.IsOpen(wed));
        }
    }
}
