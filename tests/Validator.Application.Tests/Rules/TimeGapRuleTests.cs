using System;
using Xunit;
using Validator.Application.Validation.Rules;
using Validator.Domain.Candles;

namespace Validator.Application.Tests.Rules
{
    public class TimeGapRuleTests
    {
        [Fact]
        public void Evaluate_CountsMaximalGapRuns()
        {
            var candles = new[]
            {
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 1m, 2m, 0.5m, 1.5m, 100m),
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero), 1m, 2m, 0.5m, 1.5m, 100m),
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 3, 0, 0, TimeSpan.Zero), 1m, 2m, 0.5m, 1.5m, 100m)
            };

            var findings = new TimeGapRule().Evaluate(candles, TimeSpan.FromHours(1));

            Assert.Single(findings);
            Assert.Equal(1, findings[0].CountContribution);
        }
    }
}