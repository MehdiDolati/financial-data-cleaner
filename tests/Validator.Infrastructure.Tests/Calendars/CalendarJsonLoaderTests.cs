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
            var fixture = Path.Combine("Tests", "Fixtures", "calendars", "custom-weekly-calendar.json");
            // Normalize path relative to repo root during test runtime
            var fullPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\\..\\..\\..\\..\\", fixture));

            var loader = new CalendarJsonLoader();
            var calendar = loader.Load(fullPath);

            // Wednesday 10:00 UTC should be open
            var wed = new DateTimeOffset(2026, 2, 4, 10, 0, 0, TimeSpan.Zero);
            Assert.True(calendar.IsOpen(wed));

            // Saturday 12:00 UTC should be closed
            var sat = new DateTimeOffset(2026, 2, 7, 12, 0, 0, TimeSpan.Zero);
            Assert.False(calendar.IsOpen(sat));
        }
    }
}
