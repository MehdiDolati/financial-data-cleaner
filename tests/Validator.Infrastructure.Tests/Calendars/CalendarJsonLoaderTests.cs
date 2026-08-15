using System;
using System.IO;
using Validator.Infrastructure.Calendars;
using Xunit;

namespace Validator.Infrastructure.Tests.Calendars
{
    public class CalendarJsonLoaderTests
    {
        [Fact]
        public void CalendarJsonLoader_LoadsWeeklyCalendarFromJson()
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "custom-weekly-calendar.json");

            var loader = new CalendarJsonLoader();
            var calendar = loader.Load(fixturePath);

            // Wednesday 10:00 UTC should be open
            var wed = new DateTimeOffset(2026, 2, 4, 10, 0, 0, TimeSpan.Zero);
            Assert.True(calendar.IsOpen(wed));

            // Saturday 12:00 UTC should be closed
            var sat = new DateTimeOffset(2026, 2, 7, 12, 0, 0, TimeSpan.Zero);
            Assert.False(calendar.IsOpen(sat));
        }
    }
}
