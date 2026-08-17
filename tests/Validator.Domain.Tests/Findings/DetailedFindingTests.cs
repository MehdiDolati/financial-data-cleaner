using Validator.Domain.Candles;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;

namespace Validator.Domain.Tests.Findings;

public sealed class DetailedFindingTests
{
    private static readonly FindingReference Reference = new("duplicate-record:20240801T1000000000000Z:line-42");
    private static readonly FindingLocation Location = new([42L], new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.Zero));

    private const string ProvidedTitle = "Duplicate records";
    private const string ProvidedExplanation = "Rows share the same timestamp.";
    private const string ProvidedAction = "Investigate the source rows.";

    private static DetailedFindingHeader CreateHeader(
        FindingCategory category = FindingCategory.DuplicateRecord,
        long contribution = 1L,
        string? reference = null,
        string? title = null,
        string? explanation = null,
        string? action = null,
        FindingLocation? location = null,
        EvidenceKind? evidenceKind = null)
    {
        return new DetailedFindingHeader(
            new FindingReference(reference ?? "duplicate-record:20240801T1000000000000Z:line-42"),
            category,
            title ?? ProvidedTitle,
            explanation ?? ProvidedExplanation,
            contribution,
            location ?? Location,
            evidenceKind ?? EvidenceKind.DuplicateRecord,
            action ?? ProvidedAction);
    }

    [Fact]
    public void Constructor_RejectsNonEstablishedCategory()
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateHeader(category: FindingCategory.Informational));
        Assert.Contains("category", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsEmptyTitle(string? title)
    {
        Assert.Throws<ArgumentException>(() => CreateHeader(title: title));
    }

    [Fact]
    public void Constructor_RejectsNullTitle()
    {
        Assert.Throws<ArgumentException>(() =>
            new DetailedFindingHeader(Reference, FindingCategory.DuplicateRecord, null!, ProvidedExplanation, 1, Location, EvidenceKind.DuplicateRecord, ProvidedAction));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsEmptyExplanation(string? explanation)
    {
        Assert.Throws<ArgumentException>(() => CreateHeader(explanation: explanation));
    }

    [Fact]
    public void Constructor_RejectsNullExplanation()
    {
        Assert.Throws<ArgumentException>(() =>
            new DetailedFindingHeader(Reference, FindingCategory.DuplicateRecord, ProvidedTitle, null!, 1, Location, EvidenceKind.DuplicateRecord, ProvidedAction));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsEmptySuggestedAction(string? action)
    {
        Assert.Throws<ArgumentException>(() => CreateHeader(action: action));
    }

    [Fact]
    public void Constructor_RejectsNullSuggestedAction()
    {
        Assert.Throws<ArgumentException>(() =>
            new DetailedFindingHeader(Reference, FindingCategory.DuplicateRecord, ProvidedTitle, ProvidedExplanation, 1, Location, EvidenceKind.DuplicateRecord, null!));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Constructor_RejectsNonPositiveContribution(long contribution)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateHeader(contribution: contribution));
    }

    [Fact]
    public void Constructor_RejectsNullLocation()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DetailedFindingHeader(Reference, FindingCategory.DuplicateRecord, ProvidedTitle, ProvidedExplanation, 1, null!, EvidenceKind.DuplicateRecord, ProvidedAction));
    }

    [Fact]
    public void Constructor_RejectsEvidenceKindMismatchingCategory()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CreateHeader(category: FindingCategory.InvalidOhlc, evidenceKind: EvidenceKind.MissingCandle));
        Assert.Contains("evidence", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_AcceptsValidHeaderAndExposesFields()
    {
        var header = CreateHeader();

        Assert.Equal("duplicate-record:20240801T1000000000000Z:line-42", header.Reference.Value);
        Assert.Equal(FindingCategory.DuplicateRecord, header.Category);
        Assert.Equal("Duplicate records", header.Title);
        Assert.Equal("Rows share the same timestamp.", header.Explanation);
        Assert.Equal(1L, header.CountContribution);
        Assert.Equal(EvidenceKind.DuplicateRecord, header.EvidenceKind);
        Assert.Same(Location, header.Location);
        Assert.Equal("Investigate the source rows.", header.SuggestedAction);
    }

    [Fact]
    public void Reference_IsPartOfStructuralEquality()
    {
        var first = CreateHeader();
        var second = CreateHeader();
        var different = CreateHeader(reference: "duplicate-record:20240801T1000000000000Z:line-43");

        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void EvidenceKindOf_MapsEveryEstablishedCategory()
    {
        Assert.Equal(EvidenceKind.MissingCandle, DetailedFindingHeader.EvidenceKindOf(FindingCategory.MissingCandle));
        Assert.Equal(EvidenceKind.DuplicateRecord, DetailedFindingHeader.EvidenceKindOf(FindingCategory.DuplicateRecord));
        Assert.Equal(EvidenceKind.InvalidOhlc, DetailedFindingHeader.EvidenceKindOf(FindingCategory.InvalidOhlc));
        Assert.Equal(EvidenceKind.ClosedMarketRecord, DetailedFindingHeader.EvidenceKindOf(FindingCategory.ClosedMarketRecord));
        Assert.Equal(EvidenceKind.TimeGap, DetailedFindingHeader.EvidenceKindOf(FindingCategory.TimeGap));
        Assert.Equal(EvidenceKind.MalformedRow, DetailedFindingHeader.EvidenceKindOf(FindingCategory.MalformedRow));
    }

    [Fact]
    public void EvidenceKindOf_RejectsUnestablishedCategory()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetailedFindingHeader.EvidenceKindOf(FindingCategory.Critical));
    }

    [Fact]
    public void IsEstablishedCategory_RecognizesOnlyTheSixCategories()
    {
        Assert.True(DetailedFindingHeader.IsEstablishedCategory(FindingCategory.MissingCandle));
        Assert.True(DetailedFindingHeader.IsEstablishedCategory(FindingCategory.DuplicateRecord));
        Assert.True(DetailedFindingHeader.IsEstablishedCategory(FindingCategory.InvalidOhlc));
        Assert.True(DetailedFindingHeader.IsEstablishedCategory(FindingCategory.ClosedMarketRecord));
        Assert.True(DetailedFindingHeader.IsEstablishedCategory(FindingCategory.TimeGap));
        Assert.True(DetailedFindingHeader.IsEstablishedCategory(FindingCategory.MalformedRow));
        Assert.False(DetailedFindingHeader.IsEstablishedCategory(FindingCategory.Informational));
        Assert.False(DetailedFindingHeader.IsEstablishedCategory(FindingCategory.Minor));
        Assert.False(DetailedFindingHeader.IsEstablishedCategory(FindingCategory.Major));
        Assert.False(DetailedFindingHeader.IsEstablishedCategory(FindingCategory.Critical));
    }

    [Fact]
    public void FindingReference_RejectsEmptyValue()
    {
        Assert.Throws<ArgumentException>(() => new FindingReference(""));
        Assert.Throws<ArgumentException>(() => new FindingReference("   "));
    }

    [Fact]
    public void FindingReference_RejectsNonAsciiValue()
    {
        Assert.Throws<ArgumentException>(() => new FindingReference("référence-1"));
    }

    [Fact]
    public void FindingReference_AcceptsStableAsciiValue()
    {
        var reference = new FindingReference("time-gap:20240801T1000000000000Z:20240801T1200000000000Z");

        Assert.Equal("time-gap:20240801T1000000000000Z:20240801T1200000000000Z", reference.Value);
    }

    [Fact]
    public void FindingReference_IsOrdinalComparable()
    {
        var lower = new FindingReference("a:1");
        var higher = new FindingReference("b:1");

        Assert.True(lower.CompareTo(higher) < 0);
        Assert.True(higher.CompareTo(lower) > 0);
        Assert.Equal(0, lower.CompareTo(new FindingReference("a:1")));
    }

    [Fact]
    public void FindingLocation_RejectsNonUtcTimestamp()
    {
        var local = new DateTimeOffset(2024, 8, 1, 12, 0, 0, TimeSpan.FromHours(2));

        Assert.Throws<ArgumentException>(() => new FindingLocation([1L], local));
    }

    [Fact]
    public void FindingLocation_RejectsNonPositiveSourceLines()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FindingLocation([0L]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FindingLocation([-5L]));
    }

    [Fact]
    public void FindingLocation_AcceptsEmptyLinesForAbsentRecords()
    {
        var location = new FindingLocation(
            [],
            new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.Zero));

        Assert.Empty(location.SourceLines);
        Assert.NotNull(location.TimestampUtc);
        Assert.Null(location.OriginalTimestampText);
    }

    [Fact]
    public void FindingLocation_PreservesOriginalTimestampText()
    {
        var location = new FindingLocation(
            [7L],
            new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.Zero),
            "2024.08.01 10:00");

        Assert.Equal("2024.08.01 10:00", location.OriginalTimestampText);
    }

    [Fact]
    public void FindingRelationship_RejectsUnknownKind()
    {
        var target = new FindingReference("time-gap:20240801T1000000000000Z:20240801T1200000000000Z");

        Assert.Throws<ArgumentException>(() => new FindingRelationship("Unknown", target));
        Assert.Throws<ArgumentException>(() => new FindingRelationship("", target));
    }

    [Fact]
    public void FindingRelationship_AcceptsBothStableKinds()
    {
        var target = new FindingReference("time-gap:20240801T1000000000000Z:20240801T1200000000000Z");

        var forward = new FindingRelationship(RelationshipKind.PartOfGap, target);
        var reverse = new FindingRelationship(RelationshipKind.ContainsMissingCandle, target);

        Assert.Equal(RelationshipKind.PartOfGap, forward.Kind);
        Assert.Equal(RelationshipKind.ContainsMissingCandle, reverse.Kind);
        Assert.Equal(target, forward.TargetReference);
        Assert.Equal(target, reverse.TargetReference);
    }
}