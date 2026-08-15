using System;
using Xunit;
using Validator.Application.Validation.Rules;
using Validator.Domain.Candles;

namespace Validator.Application.Tests.Rules
{
    public class InvalidOhlcRuleTests
    {
        [Fact]
        public void Evaluate_FindsInvalidPriceAndVolumeRows()
        {
            var candles = new[]
            {
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 10m, 12m, 13m, 11m, 100m),
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero), 0m, 2m, 1m, 1.5m, 100m),
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero), 10m, 11m, 9m, 10m, -1m)
            };

            var findings = new InvalidOhlcRule().Evaluate(candles);

            Assert.Equal(3, findings.Length);
        }
    }
}