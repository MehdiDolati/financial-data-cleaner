using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Tests.Reporting;
using Validator.Application.Validation;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;

namespace Validator.Application.Tests;

// The remaining refusals of the application layer, exercised one at a time:
// a reference is only built for a category the report publishes, an offset is
// only accepted in its canonical form, evidence is only joined to the finding
// that owns it, and a catalog refuses work once it is completed or disposed.
// Each of these paths is the difference between a report that states a fact and
// one that invents it, so each is proven rather than assumed.
public sealed class ApplicationGuardCoverageTests
{
    private static DateTimeOffset Utc(int hour = 10) => new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Offset(int hour = 10) =>
        new(2024, 8, 1, hour, 0, 0, TimeSpan.FromHours(2));

    private static readonly FindingReference GapReference = new("time-gap:a:b");

    private readonly InMemorySpoolStore _store = new();

    private FindingCatalog NewCatalog() => new(
        () => new InMemorySpool(_store),
        path => new InMemorySpoolReader(_store.Spools[path]));

    // A record shape the union does not publish. Nothing in the application can
    // produce one, so every switch over the union must refuse it rather than
    // silently attribute it to some finding.
    private sealed record UnpublishedEvidence : FindingEvidenceRecord
    {
        public override string Kind => "Unpublished";
    }

    private static MissingCandleEvidence MissingEvidence() => new(
        Utc(11),
        Timeframe.Parse("H1"),
        GapReference,
        null,
        null);

    private static DetailedFindingHeader HeaderFor(FindingCategory category)
    {
        var hasLine = category is not FindingCategory.MissingCandle and not FindingCategory.TimeGap;
        return new DetailedFindingHeader(
            FindingReferenceFactory.PhysicalRecord(category, 7),
            category,
            "Finding",
            "A finding was detected.",
            1,
            new FindingLocation(hasLine ? [7L] : Array.Empty<long>(), Utc()),
            DetailedFindingHeader.EvidenceKindOf(category),
            "Review the source rows.");
    }

    // ---------------------------------------------------------------- references

    [Theory]
    [InlineData(FindingCategory.MissingCandle, "missing-candle:line-5")]
    [InlineData(FindingCategory.DuplicateRecord, "duplicate-record:line-5")]
    [InlineData(FindingCategory.InvalidOhlc, "invalid-ohlc:line-5")]
    [InlineData(FindingCategory.ClosedMarketRecord, "closed-market-record:line-5")]
    [InlineData(FindingCategory.TimeGap, "time-gap:line-5")]
    [InlineData(FindingCategory.MalformedRow, "malformed-row:line-5")]
    public void PhysicalRecordReference_NamesEveryEstablishedCategory(FindingCategory category, string expected)
    {
        // The segment is how a reader identifies what a reference points at, so
        // every published category has exactly one spelled-out name.
        Assert.Equal(expected, FindingReferenceFactory.PhysicalRecord(category, 5).Value);
    }

    [Fact]
    public void PhysicalRecordReference_RefusesACategoryTheReportDoesNotPublish()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FindingReferenceFactory.PhysicalRecord((FindingCategory)999, 5));
    }

    // ------------------------------------------------------ timestamp contract

    [Fact]
    public void TimestampInterpretation_RequiresBothHalvesOfASeparatedTimestamp()
    {
        Assert.Throws<ArgumentException>(() => TimestampInterpretation.CreateSeparate(" ", "HH:mm", "+02:00"));
        Assert.Throws<ArgumentException>(() => TimestampInterpretation.CreateSeparate("yyyy.MM.dd", " ", "+02:00"));
    }

    [Fact]
    public void TimestampInterpretation_RequiresBothHalvesOfACombinedTimestamp()
    {
        Assert.Throws<ArgumentException>(() => TimestampInterpretation.CreateCombined(" ", "timestamp", "+02:00"));
        Assert.Throws<ArgumentException>(() => TimestampInterpretation.CreateCombined("yyyy-MM-dd", " ", "+02:00"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("+2:00")]
    [InlineData("x02:00")]
    [InlineData("+02-00")]
    [InlineData("+ab:cd")]
    [InlineData("+15:00")]
    [InlineData("+02:60")]
    [InlineData("+14:30")]
    public void TimestampInterpretation_RejectsAnOffsetThatIsNotCanonical(string? sourceOffset)
    {
        // The offset is what turns a source instant into a UTC one. A value that
        // is not in the published fixed form could shift every timestamp in the
        // report, so it is refused instead of being interpreted.
        var error = Assert.Throws<ArgumentException>(
            () => TimestampInterpretation.CreateCombined("yyyy-MM-dd HH:mm:ss", "timestamp", sourceOffset!));

        Assert.Equal("sourceOffset", error.ParamName);
    }

    [Theory]
    [InlineData("+14:00")]
    [InlineData("-14:00")]
    [InlineData("+00:00")]
    [InlineData("-05:30")]
    public void TimestampInterpretation_AcceptsEveryCanonicalOffsetWithinRange(string sourceOffset)
    {
        var interpretation = TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", sourceOffset);

        Assert.Equal(sourceOffset, interpretation.SourceOffset);
    }

    // -------------------------------------------------------- missing candles

    [Fact]
    public void MissingCandleGeneration_RequiresATimeframeAndAnOwningGap()
    {
        var timeframe = Timeframe.Parse("H1");

        Assert.Throws<ArgumentNullException>(
            () => MissingCandleProcessor.Generate(Utc(10), Utc(13), null!, GapReference));
        Assert.Throws<ArgumentNullException>(
            () => MissingCandleProcessor.Generate(Utc(10), Utc(13), timeframe, null!));
    }

    [Fact]
    public void MissingCandleGeneration_RejectsObservedTimestampsThatAreNotUtc()
    {
        // An expected slot is computed from the neighbours. A local-time
        // neighbour would place every generated slot at the wrong instant.
        var timeframe = Timeframe.Parse("H1");

        Assert.Throws<ArgumentException>(
            () => MissingCandleProcessor.Generate(Offset(10), Utc(13), timeframe, GapReference));
        Assert.Throws<ArgumentException>(
            () => MissingCandleProcessor.Generate(Utc(10), Offset(13), timeframe, GapReference));
    }

    [Fact]
    public void MissingCandleGeneration_RequiresTheNextObservationToFollowThePrevious()
    {
        var timeframe = Timeframe.Parse("H1");

        var error = Assert.Throws<ArgumentException>(
            () => MissingCandleProcessor.Generate(Utc(13), Utc(10), timeframe, GapReference));

        Assert.Equal("nextObservedUtc", error.ParamName);
    }

    // -------------------------------------------------------- duplicate groups

    [Fact]
    public void DuplicateGroupProcessor_RefusesToAnswerWithoutRowsOrAGroup()
    {
        Assert.Throws<ArgumentNullException>(() => DuplicateGroupProcessor.DifferingFields(null!));
        Assert.Throws<ArgumentNullException>(() => DuplicateGroupProcessor.HeaderFor(null!));
    }

    // ------------------------------------------------------- reconciliation

    [Fact]
    public void CategoryCounters_RefusesToCountForAnUnpublishedCategory()
    {
        var counters = new CategoryCounters();

        Assert.Throws<ArgumentOutOfRangeException>(() => counters.Add((FindingCategory)999, 1));
    }

    [Fact]
    public void CategoryCounters_RefusesAContributionThatIsNotPositive()
    {
        // Every finding contributes at least one count; a zero or negative
        // contribution would let a catalog disagree with its own summary.
        var counters = new CategoryCounters();

        Assert.Throws<ArgumentOutOfRangeException>(() => counters.Add(FindingCategory.MissingCandle, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.Add(FindingCategory.MissingCandle, -1));
    }

    [Fact]
    public void ReportReconciliation_RequiresTheFactsItReconciles()
    {
        var coverage = new ScanCoverage(10, 10, 0);
        var statistics = ZeroStatistics();
        var summary = new DetailedSummary(0, 0, 0, 0, 0, 0);

        Assert.Equal(
            "summary",
            Assert.Throws<ArgumentNullException>(
                () => ReportReconciliation.Create(null!, coverage, statistics)).ParamName);
        Assert.Equal(
            "coverage",
            Assert.Throws<ArgumentNullException>(
                () => ReportReconciliation.Create(summary, null!, statistics)).ParamName);
        Assert.Equal(
            "catalog",
            Assert.Throws<ArgumentNullException>(
                () => ReportReconciliation.Create(summary, coverage, null!)).ParamName);
    }

    [Fact]
    public void ReportReconciliation_RequiresCategoriesAndCoverage()
    {
        var coverage = new ScanCoverage(10, 10, 0);

        Assert.Equal(
            "categories",
            Assert.Throws<ArgumentNullException>(() => new ReportReconciliation(null!, coverage)).ParamName);
        Assert.Equal(
            "coverage",
            Assert.Throws<ArgumentNullException>(() => new ReportReconciliation(CleanCategories(), null!)).ParamName);
    }

    // --------------------------------------------------------- evidence joiner

    [Fact]
    public void JoinedEvidence_RequiresEveryPartItClaimsToCarry()
    {
        var reference = new FindingReference("missing-candle:20240801T1100000000000Z");
        var header = new FindingEvidenceRecord.MissingCandle(reference, MissingEvidence());
        var children = Array.Empty<FindingEvidenceRecord>();
        var relationships = Array.Empty<FindingRelationship>();

        Assert.Throws<ArgumentNullException>(
            () => new JoinedEvidence(null!, EvidenceKind.MissingCandle, header, children, relationships));
        Assert.Throws<ArgumentNullException>(
            () => new JoinedEvidence(reference, EvidenceKind.MissingCandle, null!, children, relationships));
        Assert.Throws<ArgumentNullException>(
            () => new JoinedEvidence(reference, EvidenceKind.MissingCandle, header, null!, relationships));
        Assert.Throws<ArgumentNullException>(
            () => new JoinedEvidence(reference, EvidenceKind.MissingCandle, header, children, null!));
    }

    [Fact]
    public void EvidenceJoiner_RequiresAHeaderAndRecordsToJoin()
    {
        var header = HeaderFor(FindingCategory.MissingCandle);

        Assert.Equal(
            "header",
            Assert.Throws<ArgumentNullException>(
                () => EvidenceJoiner.Join(null!, Array.Empty<FindingEvidenceRecord>())).ParamName);
        Assert.Equal(
            "records",
            Assert.Throws<ArgumentNullException>(() => EvidenceJoiner.Join(header, null!)).ParamName);
    }

    [Theory]
    [InlineData(FindingCategory.MissingCandle)]
    [InlineData(FindingCategory.DuplicateRecord)]
    [InlineData(FindingCategory.InvalidOhlc)]
    [InlineData(FindingCategory.ClosedMarketRecord)]
    [InlineData(FindingCategory.TimeGap)]
    [InlineData(FindingCategory.MalformedRow)]
    public void EvidenceJoiner_RefusesToRenderACategoryWithoutItsOwnHeaderRecord(FindingCategory category)
    {
        // Each category is proven by one specific evidence shape. A child record
        // alone describes part of a finding, so joining it without its header
        // would publish a finding whose central fact was never established.
        var header = HeaderFor(category);
        var child = new FindingEvidenceRecord.DuplicateDifferingField(header.Reference, "Open", 1);

        var error = Assert.Throws<InvalidOperationException>(() => EvidenceJoiner.Join(header, [child]));

        Assert.Contains(header.EvidenceKind.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceJoiner_RefusesToAttributeAnUnpublishedRecordToAnyFinding()
    {
        Assert.Throws<ArgumentNullException>(() => EvidenceJoiner.OwnerOf(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => EvidenceJoiner.OwnerOf(new UnpublishedEvidence()));
        Assert.Throws<ArgumentNullException>(() => EvidenceJoiner.ChildOrderOf(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => EvidenceJoiner.ChildOrderOf(new UnpublishedEvidence()));
    }

    [Fact]
    public void EvidenceJoiner_RequiresRelationshipsToExpand()
    {
        Assert.Throws<ArgumentNullException>(() => EvidenceJoiner.ExpandRelationships(null!));
    }

    [Fact]
    public async Task EvidenceJoiner_RequiresACursorToJoinFrom()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await EvidenceJoiner.JoinAsync(null!));
    }

    [Fact]
    public async Task EvidenceJoiner_JoinsACursorsEvidenceAndCollapsesRepeatedEdges()
    {
        // Reading a finding back must produce the same joined view every time:
        // the header first, children in child order, and one edge per distinct
        // relationship regardless of how many times it was recorded.
        var header = HeaderFor(FindingCategory.MissingCandle);
        var target = new FindingReference("time-gap:a:b");
        var cursor = new StubCursor(
            header,
            [
                new FindingEvidenceRecord.MissingCandle(header.Reference, MissingEvidence()),
                new FindingEvidenceRecord.TimeGapMissingReference(header.Reference, target, 2),
                new FindingEvidenceRecord.DuplicateDifferingField(header.Reference, "Open", 1)
            ],
            [
                new FindingRelationship(RelationshipKind.PartOfGap, target),
                new FindingRelationship(RelationshipKind.PartOfGap, target)
            ]);

        var joined = await EvidenceJoiner.JoinAsync(cursor);

        Assert.Equal(header.Reference, joined.Finding);
        Assert.Equal(EvidenceKind.MissingCandle, joined.Kind);
        Assert.IsType<FindingEvidenceRecord.MissingCandle>(joined.Header);
        Assert.Equal(
            new[] { "DuplicateDifferingField", "TimeGapMissingReference" },
            joined.Children.Select(record => record.Kind));
        var relationship = Assert.Single(joined.Relationships);
        Assert.Equal(RelationshipKind.PartOfGap, relationship.Kind);
        Assert.Equal(target, relationship.TargetReference);
        Assert.Single(joined.ChildrenOf<FindingEvidenceRecord.TimeGapMissingReference>());
    }

    // --------------------------------------------------------- finding catalog

    [Fact]
    public void FindingCatalog_RequiresBothSpoolFactories()
    {
        Assert.Equal(
            "spoolWriterFactory",
            Assert.Throws<ArgumentNullException>(
                () => new FindingCatalog(null!, path => new InMemorySpoolReader([]))).ParamName);
        Assert.Equal(
            "spoolReaderFactory",
            Assert.Throws<ArgumentNullException>(
                () => new FindingCatalog(() => new InMemorySpool(_store), null!)).ParamName);
    }

    [Fact]
    public async Task FindingCatalog_RefusesChildRecordsWithoutTheFindingTheyBelongTo()
    {
        // A location line, evidence record, or edge only means something as part
        // of a finding. Accepting one for an unknown reference would leave the
        // report with detail that belongs to nothing.
        await using var catalog = NewCatalog();
        var unknown = new FindingReference("malformed-row:line-99");

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await catalog.AppendFindingAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await catalog.AppendLocationLineAsync(null!, 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await catalog.AppendLocationLineAsync(unknown, 0));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await catalog.AppendLocationLineAsync(unknown, 5));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await catalog.AppendEvidenceAsync(null!));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await catalog.AppendEvidenceAsync(
                new FindingEvidenceRecord.MissingCandle(unknown, MissingEvidence())));
    }

    [Fact]
    public async Task FindingCatalog_RefusesAnUnpublishedEvidenceShape()
    {
        await using var catalog = NewCatalog();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await catalog.AppendEvidenceAsync(new UnpublishedEvidence()));
    }

    [Fact]
    public async Task FindingCatalog_RefusesARelationshipThatDoesNotPointBothWays()
    {
        // An edge recorded in one direction only would let a reader follow a gap
        // to its missing candles but not back, so both directions are required.
        await using var catalog = NewCatalog();
        var forward = new FindingRelationship(RelationshipKind.PartOfGap, GapReference);
        var reverse = new FindingRelationship(
            RelationshipKind.ContainsMissingCandle,
            new FindingReference("missing-candle:20240801T1100000000000Z"));

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await catalog.AppendRelationshipPairAsync(null!, reverse));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await catalog.AppendRelationshipPairAsync(forward, null!));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await catalog.AppendRelationshipPairAsync(reverse, forward));
    }

    [Fact]
    public async Task FindingCatalog_RefusesToBeReadBeforeItIsCompleted()
    {
        await using var catalog = NewCatalog();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in catalog.ReadCanonicalAsync())
            {
            }
        });
    }

    [Fact]
    public async Task FindingCatalog_RefusesMoreWorkOnceDisposedAndDisposesOnlyOnce()
    {
        var catalog = NewCatalog();
        var header = HeaderFor(FindingCategory.MalformedRow);

        await catalog.DisposeAsync();
        await catalog.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await catalog.AppendFindingAsync(header));
    }

    [Fact]
    public async Task FindingCatalog_ReplaysTheTimeframeItStoredForAMissingCandle()
    {
        // Evidence is written as text and read back later, so the timeframe a
        // missing candle was expected on must survive the round trip exactly.
        await using var catalog = NewCatalog();
        var header = HeaderFor(FindingCategory.MissingCandle);
        await catalog.AppendFindingAsync(header);
        await catalog.AppendEvidenceAsync(
            new FindingEvidenceRecord.MissingCandle(header.Reference, MissingEvidence()));

        var completion = await catalog.CompleteAsync();
        var completed = Assert.IsType<CompletedFindingCatalogResult.Succeeded>(completion).Catalog;

        await foreach (var cursor in completed.ReadCanonicalAsync())
        {
            var records = new List<FindingEvidenceRecord>();
            await foreach (var record in cursor.ReadEvidenceAsync())
            {
                records.Add(record);
            }

            var evidence = Assert.IsType<FindingEvidenceRecord.MissingCandle>(Assert.Single(records)).Evidence;
            Assert.Equal("H1", evidence.ExpectedTimeframe.ToString());

            Assert.Equal(Utc(11), evidence.ExpectedTimestampUtc);
        }
    }

    [Fact]
    public async Task FindingCatalog_RefusesMoreFindingsOnceCompleted()
    {
        await using var catalog = NewCatalog();
        await catalog.CompleteAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await catalog.AppendFindingAsync(HeaderFor(FindingCategory.MalformedRow)));
    }

    private static FindingCatalogStatistics ZeroStatistics()
    {
        var zero = new CategoryStatistics(0, 0);
        return new FindingCatalogStatistics(zero, zero, zero, zero, zero, zero);
    }

    private static List<CategoryReconciliation> CleanCategories() =>
    [
        new(FindingCategory.MissingCandle, 0, 0, 0),
        new(FindingCategory.DuplicateRecord, 0, 0, 0),
        new(FindingCategory.InvalidOhlc, 0, 0, 0),
        new(FindingCategory.ClosedMarketRecord, 0, 0, 0),
        new(FindingCategory.TimeGap, 0, 0, 0),
        new(FindingCategory.MalformedRow, 0, 0, 0)
    ];

    private sealed class StubCursor : IDetailedFindingCursor
    {
        private readonly IReadOnlyList<FindingEvidenceRecord> _evidence;
        private readonly IReadOnlyList<FindingRelationship> _relationships;

        public StubCursor(
            DetailedFindingHeader header,
            IReadOnlyList<FindingEvidenceRecord> evidence,
            IReadOnlyList<FindingRelationship> relationships)
        {
            Header = header;
            _evidence = evidence;
            _relationships = relationships;
        }

        public DetailedFindingHeader Header { get; }

        public async IAsyncEnumerable<long> ReadSourceLinesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<FindingRelationship> ReadRelationshipsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var relationship in _relationships)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return relationship;
            }

            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<FindingEvidenceRecord> ReadEvidenceAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var record in _evidence)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return record;
            }

            await Task.CompletedTask;
        }
    }
}
