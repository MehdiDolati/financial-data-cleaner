using Validator.Application.Validation.Rules;
using Validator.Domain.Candles;

namespace Validator.Application.Tests.Rules;

public sealed class RuleBoundaryCoverageTests
{
    [Fact]
    public void DuplicateRule_ClassifiesEveryOhlcvDifferenceAsConflicting()
    {
        var timestamp = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var candles = new List<PriceCandle>();
        for (var index = 0; index < 5; index++)
        {
            var first = Candle(timestamp.AddHours(index), index * 2 + 1);
            var second = first with { SourceLine = first.SourceLine + 1 };
            second = index switch
            {
                0 => second with { Open = 1.1m },
                1 => second with { High = 2.1m },
                2 => second with { Low = 0.4m },
                3 => second with { Close = 1.4m },
                _ => second with { Volume = 11m }
            };
            candles.Add(first);
            candles.Add(second);
        }

        var findings = new DuplicateRecordRule().Evaluate(candles);

        Assert.Equal(5, findings.Length);
        Assert.All(findings, finding => Assert.Contains("Conflicting", finding.Message));
    }

    [Fact]
    public void InvalidOhlcRule_ReportsNonPositiveHighLowAndClose()
    {
        var findings = new InvalidOhlcRule().Evaluate(
            [new PriceCandle(Utc(0), 0m, 0m, 0m, 0m, 0m)]);

        var finding = Assert.Single(findings);
        Assert.Contains("High <= 0", finding.Message);
        Assert.Contains("Low <= 0", finding.Message);
        Assert.Contains("Close <= 0", finding.Message);
    }

    [Fact]
    public void MissingAndGapRules_ReturnNoFindingsWhenSequenceIsTooShortOrContiguous()
    {
        var one = new[] { Candle(Utc(0), 1) };
        var contiguous = new[] { Candle(Utc(0), 1), Candle(Utc(1), 2) };

        Assert.Empty(new MissingCandleRule().Evaluate([], TimeSpan.FromHours(1)));
        Assert.Empty(new MissingCandleRule().Evaluate(one, TimeSpan.FromHours(1)));
        Assert.Empty(new MissingCandleRule().Evaluate(contiguous, TimeSpan.FromHours(1)));
        Assert.Empty(new TimeGapRule().Evaluate([], TimeSpan.FromHours(1)));
        Assert.Empty(new TimeGapRule().Evaluate(one, TimeSpan.FromHours(1)));
        Assert.Empty(new TimeGapRule().Evaluate(contiguous, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void ForexClosedMarketHelper_CoversWeekdayAndBothOpenBoundarySides()
    {
        Assert.False(ClosedMarketRecordRule.IsClosedMarket(Utc(0)));
        Assert.False(ClosedMarketRecordRule.IsClosedMarket(
            new DateTimeOffset(2026, 1, 9, 21, 59, 59, TimeSpan.Zero)));
        Assert.True(ClosedMarketRecordRule.IsClosedMarket(
            new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero)));
        Assert.False(ClosedMarketRecordRule.IsClosedMarket(
            new DateTimeOffset(2026, 1, 11, 22, 0, 0, TimeSpan.Zero)));
    }

    private static PriceCandle Candle(DateTimeOffset timestamp, long sourceLine) =>
        new(timestamp, 1m, 2m, 0.5m, 1.5m, 10m, sourceLine);

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 1, 5, hour, 0, 0, TimeSpan.Zero);
}