using System;
using Xunit;
using Validator.Application.Validation.Rules;
using Validator.Domain.Candles;

namespace Validator.Application.Tests.Rules
{
    public class ClosedMarketRecordRuleTests
    {
        [Fact]
        public void IsClosedMarket_RejectsFriday2200AndSunday215959BoundaryValues()
        {
            var fridayClose = new PriceCandle(new DateTimeOffset(2026, 1, 2, 22, 0, 0, TimeSpan.Zero), 1m, 2m, 0.5m, 1.5m, 100m);
            var sundayLate = new PriceCandle(new DateTimeOffset(2026, 1, 4, 21, 59, 59, TimeSpan.Zero), 1m, 2m, 0.5m, 1.5m, 100m);
            var sundayOpen = new PriceCandle(new DateTimeOffset(2026, 1, 4, 22, 0, 0, TimeSpan.Zero), 1m, 2m, 0.5m, 1.5m, 100m);

            Assert.True(new ClosedMarketRecordRule().Evaluate(new[] { fridayClose }).Length > 0);
            Assert.True(new ClosedMarketRecordRule().Evaluate(new[] { sundayLate }).Length > 0);
            Assert.False(new ClosedMarketRecordRule().Evaluate(new[] { sundayOpen }).Length > 0);
        }
    }
}