using System;
using Xunit;
using Validator.Domain.Calendars;

namespace Validator.Domain.Tests.Calendars
{
    public class MarketCalendarTests
    {
        [Fact]
        public void WeeklySession_Enforces_OpenBeforeClose()
        {
            Assert.Throws<ArgumentException>(() => new WeeklySession(DayOfWeek.Monday, TimeSpan.FromHours(12), TimeSpan.FromHours(12)));
            Assert.Throws<ArgumentException>(() => new WeeklySession(DayOfWeek.Monday, TimeSpan.FromHours(13), TimeSpan.FromHours(12)));
        }

        [Fact]
        public void MarketCalendarDefinition_Rejects_OverlappingSessions()
        {
            var s1 = new WeeklySession(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(12));
            var s2 = new WeeklySession(DayOfWeek.Monday, TimeSpan.FromHours(11), TimeSpan.FromHours(14));
            Assert.Throws<ArgumentException>(() => new MarketCalendarDefinition(new[] { s1, s2 }));
        }

        [Fact]
        public void MarketCalendarDefinition_Allows_NonOverlappingSessions()
        {
            var s1 = new WeeklySession(DayOfWeek.Tuesday, TimeSpan.FromHours(9), TimeSpan.FromHours(12));
            var s2 = new WeeklySession(DayOfWeek.Wednesday, TimeSpan.FromHours(11), TimeSpan.FromHours(14));
            var def = new MarketCalendarDefinition(new[] { s1, s2 });
            Assert.Equal(2, def.Sessions.Count);
        }
    }
}
