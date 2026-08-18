using Validator.Application.Validation;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;

namespace Validator.Application.Tests;

// Missing candles and the gaps that contain them are distinct categories: each
// expected slot counts once as a missing candle, each contiguous run counts once
// as a time gap, and both directions of every gap/candle edge exist.
public sealed class MissingCandleAndTimeGapTests
{
    private static readonly Timeframe H1 = Timeframe.Parse("H1");
    private static readonly Timeframe M1 = Timeframe.Parse("M1");

    private static DateTimeOffset Ts(int hour, int minute = 0) =>
        new(2024, 8, 1, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void AdjacentObservations_ProduceNoGapAndNoMissingCandle()
    {
        Assert.False(TimeGapProcessor.TryBuild(Ts(10), Ts(11), H1, out var gap));
        Assert.Null(gap);
        Assert.Empty(MissingCandleProcessor.Generate(
            Ts(10),
            Ts(11),
            H1,
            new FindingReference("time-gap:x")));
    }

    [Fact]
    public void OneAbsentSlot_ProducesOneGapAndOneMissingCandle()
    {
        Assert.True(TimeGapProcessor.TryBuild(Ts(10), Ts(12), H1, out var gap));

        Assert.Equal(1, gap!.MissingCandleCount);
        Assert.Equal(1, gap.Header.CountContribution);
        Assert.Equal(FindingCategory.TimeGap, gap.Header.Category);
        Assert.Equal(Ts(11), gap.Evidence.FirstMissingTimestampUtc);
        Assert.Equal(Ts(11), gap.Evidence.LastMissingTimestampUtc);
        Assert.Equal(7200, gap.Evidence.ElapsedSeconds);

        var candles = MissingCandleProcessor.Generate(Ts(10), Ts(12), H1, gap.Reference).ToArray();
        var candle = Assert.Single(candles);
        Assert.Equal(1, candle.Header.CountContribution);
        Assert.Equal(FindingCategory.MissingCandle, candle.Header.Category);
    }

    [Fact]
    public void MissingCandle_NeverInventsAPhysicalSourceLine()
    {
        Assert.True(TimeGapProcessor.TryBuild(Ts(10), Ts(13), H1, out var gap));

        var candles = MissingCandleProcessor.Generate(Ts(10), Ts(13), H1, gap!.Reference).ToArray();

        Assert.All(candles, candle => Assert.Empty(candle.Header.Location.SourceLines));
        Assert.Empty(gap.Header.Location.SourceLines);
        Assert.Equal([Ts(11), Ts(12)], candles.Select(candle => candle.Header.Location.TimestampUtc));
    }

    [Fact]
    public void MissingCandleEvidence_CarriesNeighboringObservedTimestamps()
    {
        Assert.True(TimeGapProcessor.TryBuild(Ts(10), Ts(13), H1, out var gap));

        var candle = MissingCandleProcessor.Generate(Ts(10), Ts(13), H1, gap!.Reference).First();
        var evidence = Assert.IsType<FindingEvidenceRecord.MissingCandle>(candle.Evidence).Evidence;

        Assert.Equal(Ts(11), evidence.ExpectedTimestampUtc);
        Assert.Equal(H1, evidence.ExpectedTimeframe);
        Assert.Equal(gap.Reference, evidence.TimeGapReference);
        Assert.Equal(Ts(10), evidence.PreviousObservedTimestampUtc);
        Assert.Equal(Ts(13), evidence.NextObservedTimestampUtc);
    }

    [Fact]
    public void GapAndCandles_ExposeBothDirectionsOfTheRelationship()
    {
        Assert.True(TimeGapProcessor.TryBuild(Ts(10), Ts(13), H1, out var gap));

        var candles = MissingCandleProcessor.Generate(Ts(10), Ts(13), H1, gap!.Reference).ToArray();

        Assert.All(candles, candle =>
        {
            Assert.Equal(RelationshipKind.PartOfGap, candle.PartOfGap.Kind);
            Assert.Equal(gap.Reference, candle.PartOfGap.TargetReference);
        });

        Assert.Equal(
            candles.Select(candle => candle.Reference.Value),
            gap.Relationships
                .Where(relationship => relationship.Kind == RelationshipKind.ContainsMissingCandle)
                .Select(relationship => relationship.TargetReference.Value));

        Assert.Equal(
            candles.Select(candle => candle.Reference.Value),
            gap.Records
                .OfType<FindingEvidenceRecord.TimeGapMissingReference>()
                .Select(record => record.TargetReference.Value));
    }

    [Fact]
    public void LargeGap_StreamsEveryMissingCandleWithoutInflatingTheGapCount()
    {
        var next = Ts(10) + TimeSpan.FromMinutes(5000);

        Assert.True(TimeGapProcessor.TryBuild(Ts(10), next, M1, out var gap));

        Assert.Equal(4999, gap!.MissingCandleCount);
        Assert.Equal(1, gap.Header.CountContribution);
        Assert.Equal(4999, gap.Evidence.MissingCandleCount);
        Assert.Equal(
            gap.MissingCandleCount,
            TimeGapProcessor.MissingCandlesOf(Ts(10), next, M1, gap.Reference).Count());
        Assert.Equal(
            gap.MissingCandleCount,
            gap.Records.OfType<FindingEvidenceRecord.TimeGapMissingReference>().Count());
    }

    [Fact]
    public void GapReferences_AreDeterministicAndDerivedFromTheMissingSpan()
    {
        Assert.True(TimeGapProcessor.TryBuild(Ts(10), Ts(13), H1, out var first));
        Assert.True(TimeGapProcessor.TryBuild(Ts(10), Ts(13), H1, out var second));

        Assert.Equal(first!.Reference.Value, second!.Reference.Value);
        Assert.Equal(
            FindingReferenceFactory.TimeGap(Ts(11), Ts(12)).Value,
            first.Reference.Value);
        Assert.Equal(
            FindingReferenceFactory.MissingCandle(Ts(11)).Value,
            MissingCandleProcessor.Generate(Ts(10), Ts(13), H1, first.Reference).First().Reference.Value);
    }

    [Fact]
    public void Processors_RejectNonUtcOrNonForwardBoundaries()
    {
        var local = new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.FromHours(2));

        Assert.Throws<ArgumentException>(() => TimeGapProcessor.TryBuild(local, Ts(13), H1, out _));
        Assert.Throws<ArgumentException>(() => TimeGapProcessor.TryBuild(Ts(13), Ts(10), H1, out _));
        Assert.Throws<ArgumentNullException>(() => TimeGapProcessor.TryBuild(Ts(10), Ts(13), null!, out _));
        Assert.Throws<ArgumentException>(() =>
            MissingCandleProcessor.Generate(Ts(13), Ts(10), H1, new FindingReference("time-gap:x")).ToArray());
    }
}
