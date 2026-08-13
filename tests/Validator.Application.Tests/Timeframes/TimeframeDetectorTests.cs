using System;
using Xunit;
using Validator.Application.Validation;
using Validator.Domain.Candles;

namespace Validator.Application.Tests.Timeframes
{
    public class TimeframeDetectorTests
    {
        [Fact]
        public void Detect_ReturnsModalTimeframe()
        {
            var candles = new[]
            {
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 1m, 2m, 0.5m, 1.5m, 100m),
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero), 1m, 2m, 0.5m, 1.5m, 100m),
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero), 1m, 2m, 0.5m, 1.5m, 100m),
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 3, 0, 0, TimeSpan.Zero), 1m, 2m, 0.5m, 1.5m, 100m)
            };

            var tf = TimeframeDetector.Detect(candles);

            Assert.NotNull(tf);
            Assert.Equal('H', tf!.Unit);
            Assert.Equal(1, tf.Value);
        }
    }
}