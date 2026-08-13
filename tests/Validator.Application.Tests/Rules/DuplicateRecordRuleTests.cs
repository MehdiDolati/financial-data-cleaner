using System;
using Xunit;
using Validator.Application.Validation.Rules;
using Validator.Domain.Candles;

namespace Validator.Application.Tests.Rules
{
    public class DuplicateRecordRuleTests
    {
        [Fact]
        public void Evaluate_DetectsDuplicateCandleRows()
        {
            var ts = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var duplicate = new PriceCandle(ts, 1m, 2m, 0.5m, 1.5m, 100m);
            var candles = new[]
            {
                duplicate,
                duplicate,
                new PriceCandle(ts.AddHours(1), 1m, 2m, 0.5m, 1.5m, 100m)
            };

            var findings = new DuplicateRecordRule().Evaluate(candles);

            Assert.Single(findings);
            Assert.Equal(1, findings[0].CountContribution);
        }
    }
}