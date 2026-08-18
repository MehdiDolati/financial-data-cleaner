using System;
using System.Collections.Generic;
using System.Linq;
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

// The last few statements that only run for particular shapes of input: a
// context whose timeframe code is malformed in each possible way, a finding
// read back with no evidence or relationships of its own, and an orchestrator
// handed no request at all. These are small paths, but each one decides whether
// a report is published or refused, so none is left unproven.
public sealed class ApplicationResidualCoverageTests
{
    private static DateTimeOffset Utc(int hour = 10) => new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    private readonly InMemorySpoolStore _store = new();

    private FindingCatalog NewCatalog() => new(
        () => new InMemorySpool(_store),
        path => new InMemorySpoolReader(_store.Spools[path]));

    private static TimestampInterpretation Timestamp() =>
        TimestampInterpretation.CreateCombined("yyyy-MM-dd HH:mm:ss", "timestamp", "+00:00");

    private static CalendarContext Calendar() => new("crypto", "Crypto");

    // ------------------------------------------------------- context snapshot

    [Theory]
    [InlineData("")]
    [InlineData("H")]
    [InlineData("X1")]
    [InlineData("HH")]
    [InlineData("H0")]
    [InlineData("H-1")]
    [InlineData("H+1")]
    [InlineData("H 1")]
    [InlineData("H1.5")]
    public void ContextSnapshot_RejectsATimeframeThatIsNotACanonicalCode(string timeframe)
    {
        // The timeframe decides which candles are expected, so a code that cannot
        // be read back exactly would make every missing-candle claim unverifiable.
        var error = Assert.Throws<ArgumentException>(
            () => new ValidationContextSnapshot(timeframe, Calendar(), Timestamp(), "comma", false, null));

        Assert.Equal("timeframe", error.ParamName);
    }

    [Theory]
    [InlineData("M1")]
    [InlineData("H4")]
    [InlineData("D1")]
    [InlineData("M15")]
    public void ContextSnapshot_AcceptsEveryCanonicalTimeframeCode(string timeframe)
    {
        var snapshot = new ValidationContextSnapshot(timeframe, Calendar(), Timestamp(), "comma", false, null);

        Assert.Equal(timeframe, snapshot.Timeframe);
    }

    [Fact]
    public void ContextSnapshot_RequiresTheCalendarAndTimestampItDescribes()
    {
        Assert.Equal(
            "calendar",
            Assert.Throws<ArgumentNullException>(
                () => new ValidationContextSnapshot("H1", null!, Timestamp(), "comma", false, null)).ParamName);
        Assert.Equal(
            "timestamp",
            Assert.Throws<ArgumentNullException>(
                () => new ValidationContextSnapshot("H1", Calendar(), null!, "comma", false, null)).ParamName);
    }

    [Theory]
    [InlineData("pipe")]
    [InlineData("COMMA")]
    [InlineData("")]
    public void ContextSnapshot_RejectsADelimiterItCannotName(string delimiter)
    {
        var error = Assert.Throws<ArgumentException>(
            () => new ValidationContextSnapshot("H1", Calendar(), Timestamp(), delimiter, false, null));

        Assert.Equal("delimiter", error.ParamName);
    }

    [Theory]
    [InlineData("comma")]
    [InlineData("semicolon")]
    [InlineData("tab")]
    public void ContextSnapshot_AcceptsEveryDelimiterItCanName(string delimiter)
    {
        var snapshot = new ValidationContextSnapshot("H1", Calendar(), Timestamp(), delimiter, false, null);

        Assert.Equal(delimiter, snapshot.Delimiter);
    }

    // ------------------------------------------------------------ orchestrator

    [Fact]
    public async Task Orchestrator_RequiresACatalogFactoryAndARequest()
    {
        Assert.Throws<ArgumentNullException>(() => new DetailedValidationOrchestrator(null!));

        var orchestrator = new DetailedValidationOrchestrator(NewCatalog);
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await orchestrator.ExecuteAsync(null!));
    }

    // ------------------------------------------------------------- catalog reads

    [Fact]
    public async Task CompletedCatalog_ReadsBackNoStreamedDetailWhenNoneWasAppended()
    {
        // Streamed detail is only what was actually appended. A finding whose
        // evidence, edges, and location lines were never recorded reads back as
        // empty sequences rather than failing, and the header still states the
        // line it cites, so the report never implies detail it does not hold.

        await using var catalog = NewCatalog();
        var header = new DetailedFindingHeader(
            FindingReferenceFactory.PhysicalRecord(FindingCategory.MalformedRow, 4),
            FindingCategory.MalformedRow,
            "Malformed row",
            "The row could not be parsed.",
            1,
            new FindingLocation([4L], null),
            EvidenceKind.MalformedRow,
            "Repair the row in the source file.");

        await catalog.AppendFindingAsync(header);
        var completion = await catalog.CompleteAsync();
        var completed = Assert.IsType<CompletedFindingCatalogResult.Succeeded>(completion).Catalog;

        await foreach (var cursor in completed.ReadCanonicalAsync())
        {
            var evidence = new List<FindingEvidenceRecord>();
            await foreach (var record in cursor.ReadEvidenceAsync())
            {
                evidence.Add(record);
            }

            var relationships = new List<FindingRelationship>();
            await foreach (var relationship in cursor.ReadRelationshipsAsync())
            {
                relationships.Add(relationship);
            }

            var lines = new List<long>();
            await foreach (var line in cursor.ReadSourceLinesAsync())
            {
                lines.Add(line);
            }

            Assert.Empty(evidence);
            Assert.Empty(relationships);
            Assert.Empty(lines);

            // The header keeps the citation it was created with, independently of
            // whether any line was spooled for streaming.
            Assert.Equal([4L], cursor.Header.Location.SourceLines);
        }
    }


    [Fact]
    public async Task CompletedCatalog_KeepsFindingsInCanonicalOrderAcrossCategories()
    {
        // Canonical order is what makes two runs comparable, so the sequence is
        // the documented category order rather than the order things arrived in.
        await using var catalog = NewCatalog();
        var gap = FindingReferenceFactory.TimeGap(Utc(11), Utc(11));
        var missing = FindingReferenceFactory.MissingCandle(Utc(11));

        await catalog.AppendFindingAsync(new DetailedFindingHeader(
            gap,
            FindingCategory.TimeGap,
            "Time gap",
            "A contiguous run of expected candles is absent.",
            1,
            new FindingLocation(Array.Empty<long>(), Utc(11)),
            EvidenceKind.TimeGap,
            "Investigate data discontinuities around the gap."));
        await catalog.AppendEvidenceAsync(new FindingEvidenceRecord.TimeGapHeader(
            gap,
            new TimeGapEvidence(Utc(10), Utc(12), Timeframe.Parse("H1"), 1, 3600)));

        await catalog.AppendFindingAsync(new DetailedFindingHeader(
            missing,
            FindingCategory.MissingCandle,
            "Missing candle",
            "An expected candle is absent from the dataset.",
            1,
            new FindingLocation(Array.Empty<long>(), Utc(11)),
            EvidenceKind.MissingCandle,
            "Verify the source feed for the expected timestamp."));
        await catalog.AppendEvidenceAsync(new FindingEvidenceRecord.MissingCandle(
            missing,
            new MissingCandleEvidence(Utc(11), Timeframe.Parse("H1"), gap, Utc(10), Utc(12))));
        await catalog.AppendRelationshipPairAsync(
            new FindingRelationship(RelationshipKind.PartOfGap, gap),
            new FindingRelationship(RelationshipKind.ContainsMissingCandle, missing));

        var completion = await catalog.CompleteAsync();
        var completed = Assert.IsType<CompletedFindingCatalogResult.Succeeded>(completion).Catalog;

        var categories = new List<FindingCategory>();
        var joined = new List<JoinedEvidence>();
        await foreach (var cursor in completed.ReadCanonicalAsync())
        {
            categories.Add(cursor.Header.Category);
            joined.Add(await EvidenceJoiner.JoinAsync(cursor));
        }

        Assert.Equal(
            new[] { FindingCategory.MissingCandle, FindingCategory.TimeGap },
            categories);

        // Both directions of the gap relationship survive the round trip, so a
        // reader can navigate from the gap to its candle and back again.
        var missingEdges = joined[0].Relationships.Select(edge => edge.Kind).ToArray();
        var gapEdges = joined[1].Relationships.Select(edge => edge.Kind).ToArray();
        Assert.Equal([RelationshipKind.PartOfGap], missingEdges);
        Assert.Equal([RelationshipKind.ContainsMissingCandle], gapEdges);
    }
}
