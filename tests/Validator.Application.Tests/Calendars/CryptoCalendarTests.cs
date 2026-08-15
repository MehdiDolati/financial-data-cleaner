using Validator.Application.Validation.Rules;
using Validator.Application.Abstractions;
using Validator.Domain.Candles;
using Validator.Domain.Calendars;

namespace Validator.Application.Tests.Calendars;

public sealed class CryptoCalendarTests
{
    [Theory]
    [InlineData(2026, 2, 6, 22)]
    [InlineData(2026, 2, 7, 12)]
    [InlineData(2026, 2, 8, 21)]
    public void ClosedMarketRule_NeverFindsAlwaysOpenCrypto(int year, int month, int day, int hour)
    {
        var candle = new PriceCandle(
            new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero),
            1m,
            2m,
            0.5m,
            1.5m,
            10m);

        var findings = new ClosedMarketRecordRule(new AlwaysOpenCryptoCalendar()).Evaluate([candle]);

        Assert.Empty(findings);
    }

    private sealed class AlwaysOpenCryptoCalendar : IMarketCalendar
    {
        public MarketProfile Profile => MarketProfile.Crypto;

        public bool IsOpen(DateTimeOffset timestamp) => true;
    }
}