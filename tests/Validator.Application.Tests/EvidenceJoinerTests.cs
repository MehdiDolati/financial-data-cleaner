using Validator.Application.Reporting;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;

namespace Validator.Application.Tests;

// Rendering joins normalized evidence records back to their finding. A finding
// whose required evidence is absent, or whose records belong elsewhere, is a
// defect rather than a partially rendered report, and relationship expansion is
// deterministic.
public sealed class EvidenceJoinerTests
{
    private static readonly Timeframe H1 = Timeframe.Parse("H1");
    private static readonly FindingReference Candle =
        new("missing-candle:20240801T1100000000000Z");
    private static readonly FindingReference Gap =
        new("time-gap:20240801T1100000000000Z:20240801T1100000000000Z");

    private static DateTimeOffset Ts(int hour) => new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    private static DetailedFindingHeader CandleHeader() => new(
        Candle,
        FindingCategory.MissingCandle,
        "Missing candle",
        "No candle for the expected slot.",
        1,
        new FindingLocation(Array.Empty<long>(), Ts(11)),
        EvidenceKind.MissingCandle,
        "Backfill the candle.");

    private static DetailedFindingHeader GapHeader() => new(
        Gap,
        FindingCategory.TimeGap,
        "Time gap",
        "Candles are absent across the interval.",
        1,
        new FindingLocation(Array.Empty<long>(), Ts(11)),
        EvidenceKind.TimeGap,
        "Backfill the interval.");

    [Fact]
    public void Join_AttachesTheCategoryHeaderRecordToItsFinding()
    {
        var evidence = new MissingCandleEvidence(Ts(11), H1, Gap, Ts(10), Ts(12));

        var joined = EvidenceJoiner.Join(
            CandleHeader(),
            [new FindingEvidenceRecord.MissingCandle(Candle, evidence)]);

        Assert.Equal(Candle, joined.Finding);
        Assert.Equal(EvidenceKind.MissingCandle, joined.Kind);
        Assert.Equal(evidence, Assert.IsType<FindingEvidenceRecord.MissingCandle>(joined.Header).Evidence);
        Assert.Empty(joined.Children);
        Assert.Empty(joined.Relationships);
    }

    [Fact]
    public void Join_StreamsChildRecordsInAppendedChildOrder()
    {
        var gapEvidence = new TimeGapEvidence(Ts(11), Ts(11), H1, 1, 7200, Ts(10), Ts(12));
        var second = new FindingReference("missing-candle:20240801T1200000000000Z");

        var joined = EvidenceJoiner.Join(
            GapHeader(),
            [
                new FindingEvidenceRecord.TimeGapMissingReference(Gap, second, 2),
                new FindingEvidenceRecord.TimeGapHeader(Gap, gapEvidence),
                new FindingEvidenceRecord.TimeGapMissingReference(Gap, Candle, 1)
            ]);

        Assert.IsType<FindingEvidenceRecord.TimeGapHeader>(joined.Header);
        Assert.Equal(
            [Candle.Value, second.Value],
            joined.ChildrenOf<FindingEvidenceRecord.TimeGapMissingReference>()
                .Select(record => record.TargetReference.Value));
    }

    [Fact]
    public void Join_RejectsAFindingWhoseRequiredEvidenceRecordIsMissing()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            EvidenceJoiner.Join(CandleHeader(), Array.Empty<FindingEvidenceRecord>()));

        Assert.Contains(Candle.Value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Join_RejectsEvidenceOwnedByAnotherFinding()
    {
        var foreign = new FindingEvidenceRecord.MissingCandle(
            new FindingReference("missing-candle:20240801T1200000000000Z"),
            new MissingCandleEvidence(Ts(12), H1, Gap));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EvidenceJoiner.Join(CandleHeader(), [foreign]));

        Assert.Contains("cannot be joined", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Join_RejectsARecordOfTheWrongCategoryAsTheHeader()
    {
        var wrongKind = new FindingEvidenceRecord.TimeGapHeader(
            Candle,
            new TimeGapEvidence(Ts(11), Ts(11), H1, 1, 7200));

        Assert.Throws<InvalidOperationException>(() => EvidenceJoiner.Join(CandleHeader(), [wrongKind]));
    }

    [Fact]
    public void ExpandRelationships_IsDeterministicAndCollapsesRepeatedEdges()
    {
        var first = new FindingReference("missing-candle:20240801T1100000000000Z");
        var second = new FindingReference("missing-candle:20240801T1200000000000Z");

        var expanded = EvidenceJoiner.ExpandRelationships(
        [
            new FindingRelationship(RelationshipKind.PartOfGap, Gap),
            new FindingRelationship(RelationshipKind.ContainsMissingCandle, second),
            new FindingRelationship(RelationshipKind.ContainsMissingCandle, first),
            new FindingRelationship(RelationshipKind.ContainsMissingCandle, first)
        ]);

        Assert.Equal(
            [
                (RelationshipKind.ContainsMissingCandle, first.Value),
                (RelationshipKind.ContainsMissingCandle, second.Value),
                (RelationshipKind.PartOfGap, Gap.Value)
            ],
            expanded.Select(relationship => (relationship.Kind, relationship.TargetReference.Value)));
    }

    [Fact]
    public void ExpandRelationships_RejectsUnknownRelationshipKinds() =>
        Assert.Throws<ArgumentException>(() => new FindingRelationship("Related", Gap));

    [Fact]
    public void OwnerAndChildOrder_AreAvailableForEveryEvidenceRecordShape()
    {
        FindingEvidenceRecord[] records =
        [
            new FindingEvidenceRecord.MissingCandle(Candle, new MissingCandleEvidence(Ts(11), H1, Gap)),
            new FindingEvidenceRecord.TimeGapHeader(Candle, new TimeGapEvidence(Ts(11), Ts(11), H1, 1, 3600)),
            new FindingEvidenceRecord.TimeGapMissingReference(Candle, Gap, 1),
            new FindingEvidenceRecord.DuplicateHeader(Candle, new DuplicateRecordEvidence(Ts(11), DuplicateClassification.Exact)),
            new FindingEvidenceRecord.DuplicateDifferingField(Candle, "Close", 2),
            new FindingEvidenceRecord.DuplicateRow(Candle, new DuplicateRowEvidence(1, null, 1m, 1m, 1m, 1m, 1m), 3),
            new FindingEvidenceRecord.InvalidOhlcValues(Candle, new OhlcValues(1m, 1m, 1m, 1m, 1m)),
            new FindingEvidenceRecord.InvalidOhlcViolation(Candle, OhlcViolationCode.HIGH_BELOW_LOW, 4),
            new FindingEvidenceRecord.ClosedMarket(Candle, new ClosedMarketRecordEvidence("forex", "Forex", "Weekend")),
            new FindingEvidenceRecord.MalformedHeader(Candle, new MalformedRowEvidence(1)),
            new FindingEvidenceRecord.MalformedFieldErrorRecord(Candle, new MalformedFieldError("Close", "x", MalformedReasonCode.INVALID_DECIMAL, "bad"), 5),
            new FindingEvidenceRecord.MalformedSkippedCheck(Candle, CheckName.InvalidOhlc, 6)
        ];

        Assert.All(records, record => Assert.Equal(Candle, EvidenceJoiner.OwnerOf(record)));
        Assert.All(records, record => Assert.True(EvidenceJoiner.ChildOrderOf(record) >= 0));
        Assert.All(records, record => Assert.False(string.IsNullOrWhiteSpace(record.Kind)));
    }
}
