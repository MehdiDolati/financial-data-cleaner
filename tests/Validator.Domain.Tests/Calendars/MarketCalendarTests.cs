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

        [Fact]
        public void WeeklySession_ExposesAliasesAndCrossDayDuration()
        {
            var session = new WeeklySession(
                DayOfWeek.Friday,
                TimeSpan.FromHours(22),
                DayOfWeek.Sunday,
                TimeSpan.FromHours(22));

            Assert.Equal(DayOfWeek.Friday, session.Day);
            Assert.Equal(TimeSpan.FromHours(22), session.Open);
            Assert.Equal(TimeSpan.FromHours(22), session.Close);
            Assert.Equal(2, session.DaysUntilClose);
            Assert.False(session.Overlaps(null!));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(24)]
        public void WeeklySession_Rejects_TimeOutsideLocalDay(int hour)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeeklySession(
                DayOfWeek.Monday,
                TimeSpan.FromHours(hour),
                TimeSpan.FromHours(23)));
        }

        [Fact]
        public void MarketCalendarDefinition_ExposesMetadataAndValidatesConfiguration()
        {
            var session = new WeeklySession(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(17));
            var definition = new MarketCalendarDefinition(
                MarketProfile.Equities,
                1,
                "US Equities",
                "America/New_York",
                [session]);

            Assert.Equal(MarketProfile.Equities, definition.Profile);
            Assert.Equal(1, definition.Version);
            Assert.Equal("US Equities", definition.Name);
            Assert.Equal("America/New_York", definition.TimeZoneId);

            Assert.Throws<ArgumentOutOfRangeException>(() => new MarketCalendarDefinition(
                MarketProfile.Custom, 2, "name", "UTC", [session]));
            Assert.Throws<ArgumentException>(() => new MarketCalendarDefinition(
                MarketProfile.Custom, 1, " ", "UTC", [session]));
            Assert.Throws<ArgumentException>(() => new MarketCalendarDefinition(
                MarketProfile.Custom, 1, "name", " ", [session]));

            var empty = new MarketCalendarDefinition(null!);
            Assert.Empty(empty.Sessions);
        }

        [Fact]
        public void UtcSession_UsesHalfOpenBoundsAndValidatesUtc()
        {
            var open = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
            var close = open.AddHours(1);
            var session = new UtcSession(open, close);

            Assert.Equal(open, session.OpenUtc);
            Assert.Equal(close, session.CloseUtc);
            Assert.False(session.Contains(open.AddTicks(-1)));
            Assert.True(session.Contains(open));
            Assert.True(session.Contains(close.AddTicks(-1)));
            Assert.False(session.Contains(close));

            Assert.Throws<ArgumentException>(() => new UtcSession(open.ToOffset(TimeSpan.FromHours(1)), close));
            Assert.Throws<ArgumentException>(() => new UtcSession(open, close.ToOffset(TimeSpan.FromHours(1))));
            Assert.Throws<ArgumentException>(() => new UtcSession(open, open));
        }
    }
}
