using System;
using Validator.Infrastructure.Csv;

namespace Validator.Infrastructure.Tests.Csv
{
    public class TimeZoneOffsetTests
    {
        [Fact]
        public void NormalizeToUtc_ConvertsOffsetDateTimeToUtc()
        {
            var value = SourceOffsetConverter.NormalizeToUtc(new DateTime(2026, 1, 1, 12, 0, 0), TimeSpan.FromHours(2));

            Assert.Equal(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), value);
        }

        [Fact]
        public void NormalizeToUtc_RejectsOutOfRangeOffsets()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SourceOffsetConverter.NormalizeToUtc(new DateTime(2026, 1, 1, 12, 0, 0), TimeSpan.FromHours(15)));
        }
    }
}
