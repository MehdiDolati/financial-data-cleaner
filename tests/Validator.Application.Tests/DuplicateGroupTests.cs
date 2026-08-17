using Validator.Application.Validation;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;

namespace Validator.Application.Tests;

// A duplicate group counts once no matter how many rows participate, keeps every
// participating row traceable to its physical line, and names exactly the OHLCV
// fields that differ.
public sealed class DuplicateGroupTests
{
    private static DateTimeOffset Ts(int hour) => new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    private static DuplicateCandidateRow Row(
        long line,
        decimal close = 1.2m,
        decimal volume = 500m,
        string? original = null) =>
        new(line, 1.1m, 1.3m, 1.0m, close, volume, original);

    [Fact]
    public void ExactDuplicates_AreClassifiedExactWithNoDifferingFields()
    {
        var group = DuplicateGroupProcessor.Build(Ts(10), [Row(5), Row(9)]);

        Assert.Equal(DuplicateClassification.Exact, group.Header.Classification);
        Assert.Empty(group.Header.DifferingFields);
        Assert.Equal([5L, 9L], group.SourceLines);
    }

    [Fact]
    public void ConflictingDuplicates_NameEveryDifferingFieldInCanonicalOrder()
    {
        var group = DuplicateGroupProcessor.Build(
            Ts(10),
            [Row(5, close: 1.2m, volume: 500m), Row(6, close: 1.9m, volume: 700m)]);

        Assert.Equal(DuplicateClassification.Conflicting, group.Header.Classification);
        Assert.Equal(["Close", "Volume"], group.Header.DifferingFields);
    }

    [Fact]
    public void DifferingFields_ReportsAllFiveOhlcvFieldsWhenAllDiffer()
    {
        var differing = DuplicateGroupProcessor.DifferingFields(
        [
            new DuplicateCandidateRow(1, 1m, 2m, 0.5m, 1.5m, 10m),
            new DuplicateCandidateRow(2, 2m, 3m, 0.4m, 1.6m, 20m)
        ]);

        Assert.Equal(["Open", "High", "Low", "Close", "Volume"], differing);
    }

    [Fact]
    public void EveryParticipatingRow_IsStreamedAsItsOwnChildRecordInLineOrder()
    {
        var group = DuplicateGroupProcessor.Build(
            Ts(10),
            [Row(30, original: "2024.08.01 10:00"), Row(11), Row(20)]);

        var rows = group.Records.OfType<FindingEvidenceRecord.DuplicateRow>().ToArray();

        Assert.Equal([11L, 20L, 30L], rows.Select(record => record.Row.SourceLine));
        Assert.Equal("2024.08.01 10:00", rows.Single(r => r.Row.SourceLine == 30).Row.OriginalTimestampText);
        Assert.All(rows, record => Assert.Equal(group.Reference, record.Finding));
        Assert.Equal(rows.Length, rows.Select(record => record.ChildOrder).Distinct().Count());
    }

    [Fact]
    public void ArbitrarilyLargeGroup_ContributesExactlyOneToTheDuplicateCount()
    {
        var rows = Enumerable.Range(1, 500).Select(line => Row(line)).ToArray();

        var group = DuplicateGroupProcessor.Build(Ts(10), rows);
        var header = DuplicateGroupProcessor.HeaderFor(group);

        Assert.Equal(1, header.CountContribution);
        Assert.Equal(500, group.SourceLines.Count);
        Assert.Equal(500, header.Location.SourceLines.Count);
        Assert.Equal(500, group.Records.OfType<FindingEvidenceRecord.DuplicateRow>().Count());
    }

    [Fact]
    public void GroupReference_IsDerivedFromTheSharedTimestampAndLowestLine()
    {
        var group = DuplicateGroupProcessor.Build(Ts(10), [Row(42), Row(7)]);

        Assert.Equal(
            FindingReferenceFactory.DuplicateRecord(Ts(10), 7).Value,
            group.Reference.Value);
    }

    [Fact]
    public void Build_RejectsGroupsThatAreNotActuallyDuplicated()
    {
        Assert.Throws<ArgumentException>(() => DuplicateGroupProcessor.Build(Ts(10), [Row(1)]));
        Assert.Throws<ArgumentException>(() => DuplicateGroupProcessor.Build(Ts(10), [Row(1), Row(1)]));
        Assert.Throws<ArgumentNullException>(() => DuplicateGroupProcessor.Build(Ts(10), null!));
    }

    [Fact]
    public void HeaderFor_ExplainsTheConflictAndSuggestsAnAction()
    {
        var exact = DuplicateGroupProcessor.HeaderFor(
            DuplicateGroupProcessor.Build(Ts(10), [Row(1), Row(2)]));
        var conflicting = DuplicateGroupProcessor.HeaderFor(
            DuplicateGroupProcessor.Build(Ts(10), [Row(1), Row(2, close: 9m)]));

        Assert.Equal(FindingCategory.DuplicateRecord, exact.Category);
        Assert.Equal(EvidenceKind.DuplicateRecord, exact.EvidenceKind);
        Assert.Contains("identical", exact.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Close", conflicting.Explanation, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(conflicting.SuggestedAction));
        Assert.Equal(Ts(10), conflicting.Location.TimestampUtc);
    }
}
