using System;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;
using Xunit;

namespace Validator.Domain.Tests.Findings.Evidence;

// US5 (T050): a bracketing source line is present exactly when its paired
// observed timestamp is present, absent at a dataset boundary, and rejected
// when zero or negative (FR-039, FR-040).
public sealed class AbsenceAnchorEvidenceTests
{
    private static readonly Timeframe H1 = Timeframe.Parse("H1");
    private static readonly FindingReference GapReference =
        new("time-gap:20240801T1000000000000Z:20240801T1200000000000Z");

    private static DateTimeOffset Ts(byte hour) =>
        new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MissingCandleEvidence_LineIsPresentExactlyWhenPairedTimestampIsPresent()
    {
        var withBoth = new MissingCandleEvidence(
            Ts(11), H1, GapReference,
            previousObservedTimestampUtc: Ts(10),
            nextObservedTimestampUtc: Ts(12),
            previousObservedSourceLine: 7,
            nextObservedSourceLine: 9);

        Assert.Equal(7, withBoth.PreviousObservedSourceLine);
        Assert.Equal(9, withBoth.NextObservedSourceLine);

        var withoutPrevious = new MissingCandleEvidence(
            Ts(11), H1, GapReference,
            previousObservedTimestampUtc: null,
            nextObservedTimestampUtc: Ts(12),
            nextObservedSourceLine: 9);

        Assert.Null(withoutPrevious.PreviousObservedSourceLine);
        Assert.Equal(9, withoutPrevious.NextObservedSourceLine);

        var withoutNext = new MissingCandleEvidence(
            Ts(11), H1, GapReference,
            previousObservedTimestampUtc: Ts(10),
            nextObservedTimestampUtc: null,
            previousObservedSourceLine: 7);

        Assert.Equal(7, withoutNext.PreviousObservedSourceLine);
        Assert.Null(withoutNext.NextObservedSourceLine);
    }

    [Fact]
    public void MissingCandleEvidence_RejectsLineWithoutPairedTimestamp()
    {
        Assert.Throws<ArgumentException>(() => new MissingCandleEvidence(
            Ts(11), H1, GapReference,
            previousObservedTimestampUtc: null,
            nextObservedTimestampUtc: Ts(12),
            previousObservedSourceLine: 7));
        Assert.Throws<ArgumentException>(() => new MissingCandleEvidence(
            Ts(11), H1, GapReference,
            previousObservedTimestampUtc: Ts(10),
            nextObservedTimestampUtc: null,
            nextObservedSourceLine: 9));
    }

    [Fact]
    public void MissingCandleEvidence_RejectsZeroOrNegativeLines()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MissingCandleEvidence(
            Ts(11), H1, GapReference,
            previousObservedTimestampUtc: Ts(10),
            nextObservedTimestampUtc: Ts(12),
            previousObservedSourceLine: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MissingCandleEvidence(
            Ts(11), H1, GapReference,
            previousObservedTimestampUtc: Ts(10),
            nextObservedTimestampUtc: Ts(12),
            nextObservedSourceLine: -3));
    }

    [Fact]
    public void TimeGapEvidence_LineIsPresentExactlyWhenPairedTimestampIsPresent()
    {
        var interior = new TimeGapEvidence(
            Ts(11), Ts(12), H1, missingCandleCount: 2, elapsedSeconds: 7200,
            previousObservedTimestampUtc: Ts(10),
            nextObservedTimestampUtc: Ts(13),
            previousObservedSourceLine: 4,
            nextObservedSourceLine: 20);

        Assert.Equal(4, interior.PreviousObservedSourceLine);
        Assert.Equal(20, interior.NextObservedSourceLine);

        // Boundary gap at the start of the dataset: no preceding observed row.
        var startBoundary = new TimeGapEvidence(
            Ts(10), Ts(11), H1, missingCandleCount: 2, elapsedSeconds: 7200,
            previousObservedTimestampUtc: null,
            nextObservedTimestampUtc: Ts(12),
            nextObservedSourceLine: 9);

        Assert.Null(startBoundary.PreviousObservedSourceLine);
        Assert.Equal(9, startBoundary.NextObservedSourceLine);

        // Boundary gap at the end of the dataset: no following observed row.
        var endBoundary = new TimeGapEvidence(
            Ts(22), Ts(23), H1, missingCandleCount: 2, elapsedSeconds: 7200,
            previousObservedTimestampUtc: Ts(21),
            nextObservedTimestampUtc: null,
            previousObservedSourceLine: 30);

        Assert.Equal(30, endBoundary.PreviousObservedSourceLine);
        Assert.Null(endBoundary.NextObservedSourceLine);
    }

    [Fact]
    public void TimeGapEvidence_RejectsLineWithoutPairedTimestamp()
    {
        Assert.Throws<ArgumentException>(() => new TimeGapEvidence(
            Ts(11), Ts(12), H1, 2, 7200,
            previousObservedTimestampUtc: null,
            nextObservedTimestampUtc: Ts(13),
            previousObservedSourceLine: 4));
        Assert.Throws<ArgumentException>(() => new TimeGapEvidence(
            Ts(11), Ts(12), H1, 2, 7200,
            previousObservedTimestampUtc: Ts(10),
            nextObservedTimestampUtc: null,
            nextObservedSourceLine: 20));
    }

    [Fact]
    public void TimeGapEvidence_RejectsZeroOrNegativeLines()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeGapEvidence(
            Ts(11), Ts(12), H1, 2, 7200,
            previousObservedTimestampUtc: Ts(10),
            nextObservedTimestampUtc: Ts(13),
            previousObservedSourceLine: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeGapEvidence(
            Ts(11), Ts(12), H1, 2, 7200,
            previousObservedTimestampUtc: Ts(10),
            nextObservedTimestampUtc: Ts(13),
            nextObservedSourceLine: -1));
    }

    [Fact]
    public void Evidence_LinesAboveInt32MaxValueArePreserved()
    {
        const long huge = (long)int.MaxValue + 42;

        var candle = new MissingCandleEvidence(
            Ts(11), H1, GapReference,
            previousObservedTimestampUtc: Ts(10),
            nextObservedTimestampUtc: Ts(12),
            previousObservedSourceLine: huge,
            nextObservedSourceLine: huge + 1);

        Assert.Equal(huge, candle.PreviousObservedSourceLine);
        Assert.Equal(huge + 1, candle.NextObservedSourceLine);

        var gap = new TimeGapEvidence(
            Ts(11), Ts(12), H1, 2, 7200,
            previousObservedTimestampUtc: Ts(10),
            nextObservedTimestampUtc: Ts(13),
            previousObservedSourceLine: huge,
            nextObservedSourceLine: huge + 1);

        Assert.Equal(huge, gap.PreviousObservedSourceLine);
        Assert.Equal(huge + 1, gap.NextObservedSourceLine);
    }
}