using Validator.Application.Validation;
using Validator.Domain.Candles;

namespace Validator.Application.Tests.Timeframes;

public sealed class TimeframeDetectorBoundaryTests
{
    [Fact]
    public void Detect_ReturnsNullWhenThereAreFewerThanTwoDistinctTimestamps()
    {
        var candle = Candle(TimeSpan.Zero);

        Assert.Null(TimeframeDetector.Detect([]));
        Assert.Null(TimeframeDetector.Detect([candle]));
        Assert.Null(TimeframeDetector.Detect([candle, candle with { SourceLine = 2 }]));
    }

    [Fact]
    public void Detect_ReturnsNullWhenDeltaModesTie()
    {
        var candles = new[]
        {
            Candle(TimeSpan.Zero),
            Candle(TimeSpan.FromHours(1)),
            Candle(TimeSpan.FromHours(3))
        };

        Assert.Null(TimeframeDetector.Detect(candles));
    }

    [Theory]
    [InlineData(1, 0, 0, "D1")]
    [InlineData(0, 36, 0, "H36")]
    [InlineData(0, 1, 0, "H1")]
    [InlineData(0, 0, 1, "M1")]
    public void Detect_ReturnsCanonicalWholeUnitCode(int days, int hours, int minutes, string expected)
    {
        var delta = TimeSpan.FromDays(days) + TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);

        var timeframe = TimeframeDetector.Detect([Candle(TimeSpan.Zero), Candle(delta)]);

        Assert.NotNull(timeframe);
        Assert.Equal(expected, timeframe.ToString());
    }

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    public void Detect_ReturnsNullForSubMinuteOrFractionalMinuteDelta(int seconds)
    {
        Assert.Null(TimeframeDetector.Detect(
            [Candle(TimeSpan.Zero), Candle(TimeSpan.FromSeconds(seconds))]));
    }

    [Fact]
    public void Detect_UsesTheUniqueModeWhenOtherDeltasExist()
    {
        var timeframe = TimeframeDetector.Detect(
        [
            Candle(TimeSpan.Zero),
            Candle(TimeSpan.FromHours(1)),
            Candle(TimeSpan.FromHours(2)),
            Candle(TimeSpan.FromHours(4))
        ]);

        Assert.Equal("H1", timeframe?.ToString());
    }

    private static PriceCandle Candle(TimeSpan offset) =>
        new(
            new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero).Add(offset),
            1m,
            2m,
            0.5m,
            1.5m,
            10m,
            Math.Max(1, offset.Ticks + 1));
}