using System;
using Xunit;
using Validator.Domain.Candles;

namespace Validator.Domain.Tests.Candles
{
    public class PriceCandleTests
    {
        [Fact]
        public void Constructor_AllowsUtcTimestamp()
        {
            var ts = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);
            var candle = new PriceCandle(ts, 1.0m, 2.0m, 1.0m, 1.5m, 100m);
            Assert.Equal(ts, candle.Timestamp);
            Assert.Equal(1.0m, candle.Open);
        }

        [Fact]
        public void Constructor_RejectsNonUtcTimestamp()
        {
            var ts = new DateTimeOffset(2026, 8, 13, 3, 0, 0, TimeSpan.FromHours(+3));
            Assert.Throws<ArgumentException>(() => new PriceCandle(ts, 1.0m, 2.0m, 1.0m, 1.5m, 100m));
        }
    }
}