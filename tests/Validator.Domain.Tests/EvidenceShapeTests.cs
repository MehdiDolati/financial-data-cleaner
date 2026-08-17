using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;

namespace Validator.Domain.Tests;

// Each category-specific evidence shape must expose exactly the named fields an
// analyst needs to act, and must refuse to exist without them.
public sealed class EvidenceShapeTests
{
    private static readonly Timeframe H1 = Timeframe.Parse("H1");
    private static readonly FindingReference Gap =
        new("time-gap:20240801T1000000000000Z:20240801T1200000000000Z");

    private static DateTimeOffset Ts(int hour) => new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MissingCandle_ExposesExpectedSlotTimeframeGapAndNeighbors()
    {
        var evidence = new MissingCandleEvidence(Ts(11), H1, Gap, Ts(10), Ts(12));

        Assert.Equal(Ts(11), evidence.ExpectedTimestampUtc);
        Assert.Equal(H1, evidence.ExpectedTimeframe);
        Assert.Equal(Gap, evidence.TimeGapReference);
        Assert.Equal(Ts(10), evidence.PreviousObservedTimestampUtc);
        Assert.Equal(Ts(12), evidence.NextObservedTimestampUtc);
    }

    [Fact]
    public void TimeGap_ExposesSpanCountAndElapsedSeconds()
    {
        var evidence = new TimeGapEvidence(Ts(11), Ts(11), H1, 1, 7200, Ts(10), Ts(12));

        Assert.Equal(Ts(11), evidence.FirstMissingTimestampUtc);
        Assert.Equal(Ts(11), evidence.LastMissingTimestampUtc);
        Assert.Equal(1, evidence.MissingCandleCount);
        Assert.Equal(7200, evidence.ElapsedSeconds);
    }

    [Fact]
    public void TimeGap_RejectsNonPositiveCountsAndElapsedTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimeGapEvidence(Ts(11), Ts(11), H1, 0, 7200));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimeGapEvidence(Ts(11), Ts(11), H1, 1, 0));
    }

    [Fact]
    public void DuplicateRow_KeepsEveryParticipatingRowTraceableToItsLine()
    {
        var row = new DuplicateRowEvidence(42, "2024.08.01 10:00", 1.1m, 1.3m, 1.0m, 1.2m, 500m);

        Assert.Equal(42, row.SourceLine);
        Assert.Equal("2024.08.01 10:00", row.OriginalTimestampText);
        Assert.Equal(1.1m, row.Open);
        Assert.Equal(1.3m, row.High);
        Assert.Equal(1.0m, row.Low);
        Assert.Equal(1.2m, row.Close);
        Assert.Equal(500m, row.Volume);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DuplicateRowEvidence(0, null, 1m, 1m, 1m, 1m, 1m));
    }

    [Fact]
    public void DuplicateRecord_ClassificationAgreesWithDifferingFields()
    {
        var exact = new DuplicateRecordEvidence(Ts(10), DuplicateClassification.Exact);
        Assert.Empty(exact.DifferingFields);

        var conflicting = new DuplicateRecordEvidence(
            Ts(10),
            DuplicateClassification.Conflicting,
            ["Close", "Volume"]);
        Assert.Equal(["Close", "Volume"], conflicting.DifferingFields);

        Assert.Throws<ArgumentException>(() =>
            new DuplicateRecordEvidence(Ts(10), DuplicateClassification.Conflicting));
        Assert.Throws<ArgumentException>(() =>
            new DuplicateRecordEvidence(Ts(10), DuplicateClassification.Exact, ["Close"]));
        Assert.Throws<ArgumentException>(() =>
            new DuplicateRecordEvidence(Ts(10), DuplicateClassification.Conflicting, ["Spread"]));
    }

    [Fact]
    public void InvalidOhlc_ListsEveryViolatedStableCodeWithObservedValues()
    {
        var evidence = new InvalidOhlcEvidence(
            new OhlcValues(1.2m, 1.1m, 1.3m, 1.25m, -5m),
            [OhlcViolationCode.HIGH_BELOW_OPEN, OhlcViolationCode.NEGATIVE_VOLUME]);

        Assert.Equal(1.2m, evidence.Observed.Open);
        Assert.Equal(-5m, evidence.Observed.Volume);
        Assert.Equal(
            [OhlcViolationCode.HIGH_BELOW_OPEN, OhlcViolationCode.NEGATIVE_VOLUME],
            evidence.Violations);

        Assert.Throws<ArgumentException>(() =>
            new InvalidOhlcEvidence(new OhlcValues(1m, 1m, 1m, 1m, 1m), []));
        Assert.Throws<ArgumentException>(() => new InvalidOhlcEvidence(
            new OhlcValues(1m, 1m, 1m, 1m, 1m),
            [OhlcViolationCode.HIGH_BELOW_LOW, OhlcViolationCode.HIGH_BELOW_LOW]));
    }

    [Fact]
    public void ClosedMarketRecord_NamesCalendarIdentityAndClassifyingRule()
    {
        var evidence = new ClosedMarketRecordEvidence(
            "forex",
            "Forex 24x5",
            "WeekendClosure",
            "UTC",
            new UtcBoundary(Ts(10), Ts(12)));

        Assert.Equal("forex", evidence.MarketProfile);
        Assert.Equal("Forex 24x5", evidence.CalendarName);
        Assert.Equal("UTC", evidence.CalendarTimeZone);
        Assert.Equal("WeekendClosure", evidence.ClosedRule);
        Assert.Equal(Ts(10), evidence.Boundary!.ClosedFromUtc);
        Assert.Equal(Ts(12), evidence.Boundary!.NextOpenUtc);

        Assert.Throws<ArgumentException>(() => new UtcBoundary(Ts(12), Ts(10)));
        Assert.Throws<ArgumentException>(() =>
            new ClosedMarketRecordEvidence(" ", "Forex 24x5", "WeekendClosure"));
        Assert.Throws<ArgumentException>(() =>
            new ClosedMarketRecordEvidence("forex", "Forex 24x5", " "));
    }

    [Fact]
    public void MalformedRow_KeepsLineAndPerFieldReasonCodes()
    {
        var row = new MalformedRowEvidence(9, null, "not-a-date", expectedSlotReserved: false);
        Assert.Equal(9, row.SourceLine);
        Assert.Null(row.ParsedTimestampUtc);
        Assert.Equal("not-a-date", row.OriginalTimestampText);
        Assert.False(row.ExpectedSlotReserved);

        var error = new MalformedFieldError("Close", "abc", MalformedReasonCode.INVALID_DECIMAL, "Not a decimal.");
        Assert.Equal("Close", error.Field);
        Assert.Equal("abc", error.OriginalValue);
        Assert.Equal(MalformedReasonCode.INVALID_DECIMAL, error.ReasonCode);
        Assert.Equal("Not a decimal.", error.Reason);

        Assert.Throws<ArgumentException>(() =>
            new MalformedFieldError(" ", "abc", MalformedReasonCode.INVALID_VALUE, "reason"));
        Assert.Throws<ArgumentException>(() =>
            new MalformedFieldError("Close", "abc", MalformedReasonCode.INVALID_VALUE, " "));
    }

    [Theory]
    [InlineData(FindingCategory.MissingCandle, EvidenceKind.MissingCandle)]
    [InlineData(FindingCategory.DuplicateRecord, EvidenceKind.DuplicateRecord)]
    [InlineData(FindingCategory.InvalidOhlc, EvidenceKind.InvalidOhlc)]
    [InlineData(FindingCategory.ClosedMarketRecord, EvidenceKind.ClosedMarketRecord)]
    [InlineData(FindingCategory.TimeGap, EvidenceKind.TimeGap)]
    [InlineData(FindingCategory.MalformedRow, EvidenceKind.MalformedRow)]
    public void EveryEstablishedCategory_HasOneCorrespondingEvidenceKind(
        FindingCategory category,
        EvidenceKind expected) =>
        Assert.Equal(expected, DetailedFindingHeader.EvidenceKindOf(category));
}
