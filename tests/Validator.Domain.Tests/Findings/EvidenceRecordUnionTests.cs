using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;

namespace Validator.Domain.Tests.Findings;

// The evidence union is the wire between the checks that detect a problem and
// the writers that explain it, so every member must expose its owning finding,
// its payload, and a deterministic child ordering key. These cases read each
// member through the union so a renamed or dropped accessor cannot pass
// unnoticed, and they pin the remaining constructor guards that keep an
// unusable record from being created at all.
public sealed class EvidenceRecordUnionTests
{
    private static readonly Timeframe H1 = Timeframe.Parse("H1");
    private static readonly FindingReference Owner =
        new("time-gap:20240801T1000000000000Z:20240801T1200000000000Z");
    private static readonly FindingReference Child =
        new("missing-candle:20240801T1100000000000Z");

    private static DateTimeOffset Ts(int hour) => new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    private static OhlcValues Values() => new(1.1m, 1.3m, 1.0m, 1.2m, 500m);

    [Fact]
    public void MissingCandleAndGapHeader_ExposeOwnerPayloadAndKind()
    {
        var missing = new FindingEvidenceRecord.MissingCandle(
            Child,
            new MissingCandleEvidence(Ts(11), H1, Owner, Ts(10), Ts(12)));

        Assert.Equal(Child, missing.Finding);
        Assert.Equal(Ts(11), missing.Evidence.ExpectedTimestampUtc);
        Assert.Equal("MissingCandle", missing.Kind);
        Assert.Equal(0, missing.ChildOrder);

        var gap = new FindingEvidenceRecord.TimeGapHeader(
            Owner,
            new TimeGapEvidence(Ts(11), Ts(11), H1, 1, 7200, Ts(10), Ts(12)));

        Assert.Equal(Owner, gap.Finding);
        Assert.Equal(1, gap.Evidence.MissingCandleCount);
        Assert.Equal("TimeGap", gap.Kind);
    }

    [Fact]
    public void GapMissingReference_CarriesBothEndsOfTheRelationship()
    {
        var edge = new FindingEvidenceRecord.TimeGapMissingReference(Owner, Child, 3);

        Assert.Equal(Owner, edge.Finding);
        Assert.Equal(Child, edge.TargetReference);
        Assert.Equal(3, edge.ChildOrder);
        Assert.Equal("TimeGapMissingReference", edge.Kind);
    }

    [Fact]
    public void DuplicateRecords_ExposeGroupDifferingFieldsAndEveryRow()
    {
        var header = new FindingEvidenceRecord.DuplicateHeader(
            Owner,
            new DuplicateRecordEvidence(Ts(10), DuplicateClassification.Conflicting, ["Close"]));

        Assert.Equal(Owner, header.Finding);
        Assert.Equal(DuplicateClassification.Conflicting, header.Evidence.Classification);
        Assert.Equal("DuplicateRecord", header.Kind);

        var field = new FindingEvidenceRecord.DuplicateDifferingField(Owner, "Close", 1);

        Assert.Equal(Owner, field.Finding);
        Assert.Equal("Close", field.Field);
        Assert.Equal("DuplicateDifferingField", field.Kind);

        var row = new FindingEvidenceRecord.DuplicateRow(
            Owner,
            new DuplicateRowEvidence(42, "2024.08.01 10:00", 1.1m, 1.3m, 1.0m, 1.2m, 500m),
            2);

        Assert.Equal(Owner, row.Finding);
        Assert.Equal(42, row.Row.SourceLine);
        Assert.Equal("DuplicateRow", row.Kind);
    }

    [Fact]
    public void InvalidOhlcRecords_ExposeObservedValuesAndEachViolatedCode()
    {
        var observed = new FindingEvidenceRecord.InvalidOhlcValues(Owner, Values());

        Assert.Equal(Owner, observed.Finding);
        Assert.Equal(1.0m, observed.Observed.Low);
        Assert.Equal(1.2m, observed.Observed.Close);
        Assert.Equal("InvalidOhlc", observed.Kind);

        var violation = new FindingEvidenceRecord.InvalidOhlcViolation(
            Owner,
            OhlcViolationCode.HIGH_BELOW_LOW,
            1);

        Assert.Equal(Owner, violation.Finding);
        Assert.Equal(OhlcViolationCode.HIGH_BELOW_LOW, violation.Code);
        Assert.Equal("InvalidOhlcViolation", violation.Kind);
    }

    [Fact]
    public void ClosedMarketRecord_ExposesSelectedCalendarAndClassifyingRule()
    {
        var closed = new FindingEvidenceRecord.ClosedMarket(
            Owner,
            new ClosedMarketRecordEvidence("forex", "Forex", "WeekendClosed"));

        Assert.Equal(Owner, closed.Finding);
        Assert.Equal("Forex", closed.Evidence.CalendarName);
        Assert.Equal("WeekendClosed", closed.Evidence.ClosedRule);
        Assert.Equal("ClosedMarketRecord", closed.Kind);
    }

    [Fact]
    public void MalformedRecords_ExposeRowEachFieldErrorAndEachSkippedCheck()
    {
        var header = new FindingEvidenceRecord.MalformedHeader(
            Owner,
            new MalformedRowEvidence(42, Ts(10), "2024.08.01 10:00", expectedSlotReserved: true));

        Assert.Equal(Owner, header.Finding);
        Assert.Equal(42, header.Evidence.SourceLine);
        Assert.True(header.Evidence.ExpectedSlotReserved);
        Assert.Equal("MalformedRow", header.Kind);

        var error = new FindingEvidenceRecord.MalformedFieldErrorRecord(
            Owner,
            new MalformedFieldError("close", "n/a", MalformedReasonCode.INVALID_DECIMAL, "Not a decimal."),
            1);

        Assert.Equal(Owner, error.Finding);
        Assert.Equal("close", error.Error.Field);
        Assert.Equal("MalformedFieldError", error.Kind);

        var skipped = new FindingEvidenceRecord.MalformedSkippedCheck(Owner, CheckName.InvalidOhlc, 2);

        Assert.Equal(Owner, skipped.Finding);
        Assert.Equal(CheckName.InvalidOhlc, skipped.Check);
        Assert.Equal("MalformedSkippedCheck", skipped.Kind);
    }

    [Fact]
    public void FindingReference_RendersItsOwnStableValue()
    {
        Assert.Equal(Owner.Value, Owner.ToString());
    }

    [Fact]
    public void FindingReference_OrdersAMissingCounterpartLast()
    {
        // Ordering feeds canonical report order, so a null counterpart must still
        // produce a defined comparison instead of throwing mid-sort.
        Assert.True(Owner.CompareTo(null) > 0);
    }

    [Fact]
    public void Header_RejectsAMissingReferenceAndAnUnestablishedCategory()
    {
        Assert.Throws<ArgumentNullException>(() => new DetailedFindingHeader(
            null!,
            FindingCategory.TimeGap,
            "Time gap",
            "A contiguous run of candles is absent.",
            1,
            new FindingLocation(null, Ts(11)),
            EvidenceKind.TimeGap,
            "Backfill the range."));

        // The severity values kept for older consumers sit above the six
        // established categories; a value below them must be refused too.
        Assert.False(DetailedFindingHeader.IsEstablishedCategory((FindingCategory)(-1)));
    }

    [Fact]
    public void Location_AcceptsKnownLinesWithAUtcTimestampAndRejectsALocalOne()
    {
        var located = new FindingLocation([7L, 9L], Ts(10), "2024.08.01 10:00");

        Assert.Equal([7L, 9L], located.SourceLines);
        Assert.Equal(Ts(10), located.TimestampUtc);
        Assert.Equal("2024.08.01 10:00", located.OriginalTimestampText);

        var expected = new FindingLocation(null, Ts(11));
        Assert.Empty(expected.SourceLines);

        Assert.Throws<ArgumentException>(() =>
            new FindingLocation([7L], new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.FromHours(2))));
    }

    [Fact]
    public void InvalidOhlc_RequiresObservedValuesAndAtLeastOneDistinctCode()
    {
        Assert.Throws<ArgumentNullException>(() => new InvalidOhlcEvidence(null!, [OhlcViolationCode.HIGH_BELOW_LOW]));

        // Omitting the codes would claim a violation nobody can act on.
        Assert.Throws<ArgumentException>(() => new InvalidOhlcEvidence(Values()));
        Assert.Throws<ArgumentException>(() => new InvalidOhlcEvidence(
            Values(),
            [OhlcViolationCode.HIGH_BELOW_LOW, OhlcViolationCode.HIGH_BELOW_LOW]));
    }

    [Fact]
    public void MalformedFieldError_KeepsAnAbsentOriginalValueAsEmptyText()
    {
        // A missing column has no offending text, but the error must still render.
        var error = new MalformedFieldError("close", null!, MalformedReasonCode.MISSING_COLUMN, "Column absent.");

        Assert.Equal(string.Empty, error.OriginalValue);
        Assert.Equal(MalformedReasonCode.MISSING_COLUMN, error.ReasonCode);
        Assert.Equal("Column absent.", error.Reason);
    }

    [Fact]
    public void Relationship_RejectsAnUnknownKindAndAMissingTarget()
    {
        Assert.Throws<ArgumentException>(() => new FindingRelationship("RelatedTo", Child));
        Assert.Throws<ArgumentNullException>(() =>
            new FindingRelationship(RelationshipKind.PartOfGap, null!));
    }

    [Fact]
    public void TimeGap_RejectsNonUtcBoundsOnBothEndsOfTheRun()
    {
        var local = new DateTimeOffset(2024, 8, 1, 11, 0, 0, TimeSpan.FromHours(2));

        Assert.Throws<ArgumentException>(() =>
            new TimeGapEvidence(local, Ts(11), H1, 1, 7200));
        Assert.Throws<ArgumentException>(() =>
            new TimeGapEvidence(Ts(11), local, H1, 1, 7200));
    }
}
