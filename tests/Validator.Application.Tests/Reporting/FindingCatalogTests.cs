using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;

namespace Validator.Application.Tests.Reporting;

public sealed class FindingCatalogTests
{
    private readonly InMemorySpoolStore _store = new();

    private FindingCatalog CreateCatalog() => new(
        () => new InMemorySpool(_store),
        path => new InMemorySpoolReader(_store.Spools[path]));

    private static readonly Timeframe H1 = Timeframe.Parse("H1");

    private static DateTimeOffset Ts(int day, int hour = 10) =>
        new(2024, 8, day, hour, 0, 0, TimeSpan.Zero);

    private static FindingReference GapReference =>
        new("time-gap:20240801T1000000000000Z:20240801T1200000000000Z");

    private static FindingReference CandleReference =>
        new("missing-candle:20240801T1000000000000Z");

    private static DetailedFindingHeader Header(FindingReference reference, long contribution = 1) =>
        new(
            reference,
            FindingCategory.MissingCandle,
            "Missing candle",
            "An expected candle is absent.",
            contribution,
            new FindingLocation(null, Ts(1, 10)),
            EvidenceKind.MissingCandle,
            "Verify the source feed.");

    [Fact]
    public async Task CompleteThenRead_ReplaysFindingsInCanonicalReferenceOrder()
    {
        var catalog = CreateCatalog();
        var later = new FindingReference("missing-candle:20240802T1000000000000Z");
        var earlier = new FindingReference("missing-candle:20240801T1000000000000Z");

        await catalog.AppendFindingAsync(Header(later));
        await catalog.AppendFindingAsync(Header(earlier));

        var result = await catalog.CompleteAsync();
        var completed = Assert.IsType<CompletedFindingCatalogResult.Succeeded>(result);

        var references = new List<string>();
        await foreach (var cursor in completed.Catalog.ReadCanonicalAsync())
        {
            references.Add(cursor.Header.Reference.Value);
        }

        Assert.Equal(new[] { earlier.Value, later.Value }, references);
    }

    [Fact]
    public async Task LocationLines_StreamPerFindingInAppendOrder()
    {
        var catalog = CreateCatalog();
        await catalog.AppendFindingAsync(Header(CandleReference));
        await catalog.AppendLocationLineAsync(CandleReference, 7);
        await catalog.AppendLocationLineAsync(CandleReference, 9);

        var result = await catalog.CompleteAsync();
        var completed = Assert.IsType<CompletedFindingCatalogResult.Succeeded>(result);

        await foreach (var cursor in completed.Catalog.ReadCanonicalAsync())
        {
            var lines = new List<long>();
            await foreach (var line in cursor.ReadSourceLinesAsync())
            {
                lines.Add(line);
            }

            Assert.Equal(new[] { 7L, 9L }, lines);
        }
    }

    [Fact]
    public async Task MissingLocationLines_YieldNothingInsteadOfInventedLines()
    {
        var catalog = CreateCatalog();
        await catalog.AppendFindingAsync(Header(CandleReference));

        var result = await catalog.CompleteAsync();
        var completed = Assert.IsType<CompletedFindingCatalogResult.Succeeded>(result);

        await foreach (var cursor in completed.Catalog.ReadCanonicalAsync())
        {
            var lines = new List<long>();
            await foreach (var line in cursor.ReadSourceLinesAsync())
            {
                lines.Add(line);
            }

            Assert.Empty(lines);
        }
    }

    [Fact]
    public async Task EvidenceRecords_RoundTripWithChildOrderAndKind()
    {
        var catalog = CreateCatalog();
        await catalog.AppendFindingAsync(Header(CandleReference));
        await catalog.AppendFindingAsync(Header(GapReference));
        var missing = new MissingCandleEvidence(Ts(1, 10), H1, GapReference);
        var gap = new FindingReference("time-gap:20240801T1000000000000Z:20240801T1200000000000Z");

        await catalog.AppendEvidenceAsync(new FindingEvidenceRecord.MissingCandle(CandleReference, missing));
        await catalog.AppendEvidenceAsync(new FindingEvidenceRecord.TimeGapHeader(
            gap,
            new TimeGapEvidence(Ts(1, 10), Ts(1, 12), H1, 2, 7200, null, null)));

        var result = await catalog.CompleteAsync();
        var completed = Assert.IsType<CompletedFindingCatalogResult.Succeeded>(result);

        await foreach (var cursor in completed.Catalog.ReadCanonicalAsync())
        {
            var records = new List<FindingEvidenceRecord>();
            await foreach (var record in cursor.ReadEvidenceAsync())
            {
                records.Add(record);
            }

            if (cursor.Header.Reference.Value == CandleReference.Value)
            {
                var record = Assert.IsType<FindingEvidenceRecord.MissingCandle>(Assert.Single(records));
                Assert.Equal(Ts(1, 10), record.Evidence.ExpectedTimestampUtc);
                Assert.Equal(GapReference, record.Evidence.TimeGapReference);
            }
            else
            {
                Assert.IsType<FindingEvidenceRecord.TimeGapHeader>(Assert.Single(records));
                Assert.Equal(gap, cursor.Header.Reference);
            }
        }
    }

    [Fact]
    public async Task RelationshipPair_PersistsBothEdgesWithCorrectOwners()
    {
        var catalog = CreateCatalog();
        await catalog.AppendFindingAsync(Header(CandleReference));
        await catalog.AppendFindingAsync(Header(GapReference));

        await catalog.AppendRelationshipPairAsync(
            new FindingRelationship(RelationshipKind.PartOfGap, GapReference),
            new FindingRelationship(RelationshipKind.ContainsMissingCandle, CandleReference));

        var result = await catalog.CompleteAsync();
        var completed = Assert.IsType<CompletedFindingCatalogResult.Succeeded>(result);

        var relationshipsByOwner = new Dictionary<string, List<string>>();
        await foreach (var cursor in completed.Catalog.ReadCanonicalAsync())
        {
            var relationships = new List<string>();
            await foreach (var relationship in cursor.ReadRelationshipsAsync())
            {
                relationships.Add($"{relationship.Kind}:{relationship.TargetReference.Value}");
            }

            relationshipsByOwner[cursor.Header.Reference.Value] = relationships;
        }

        Assert.Equal(
            new[] { $"{RelationshipKind.PartOfGap}:{GapReference.Value}" },
            relationshipsByOwner[CandleReference.Value]);
        Assert.Equal(
            new[] { $"{RelationshipKind.ContainsMissingCandle}:{CandleReference.Value}" },
            relationshipsByOwner[GapReference.Value]);
    }

    [Fact]
    public async Task DuplicateReference_IsRejected()
    {
        var catalog = CreateCatalog();
        await catalog.AppendFindingAsync(Header(CandleReference));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.AppendFindingAsync(Header(CandleReference)).AsTask());
    }

    [Fact]
    public async Task RelationshipPair_RequiresInverseKinds()
    {
        var catalog = CreateCatalog();
        await catalog.AppendFindingAsync(Header(CandleReference));
        await catalog.AppendFindingAsync(Header(GapReference));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.AppendRelationshipPairAsync(
                new FindingRelationship(RelationshipKind.PartOfGap, GapReference),
                new FindingRelationship(RelationshipKind.PartOfGap, CandleReference)).AsTask());
    }

    [Fact]
    public async Task MissingRelationshipTarget_FailsCompletion()
    {
        var catalog = CreateCatalog();
        await catalog.AppendFindingAsync(Header(CandleReference));

        await catalog.AppendRelationshipPairAsync(
            new FindingRelationship(RelationshipKind.PartOfGap, GapReference),
            new FindingRelationship(RelationshipKind.ContainsMissingCandle, CandleReference));

        var result = await catalog.CompleteAsync();
        var failed = Assert.IsType<CompletedFindingCatalogResult.Failed>(result);

        Assert.Equal("VALIDATION_INCOMPLETE", failed.Diagnostic.Code);
    }

    [Fact]
    public async Task Statistics_AccumulateEntriesAndContributionSums()
    {
        var catalog = CreateCatalog();
        await catalog.AppendFindingAsync(Header(CandleReference, contribution: 1));
        await catalog.AppendFindingAsync(Header(GapReference, contribution: 1));
        var duplicate = new FindingReference("duplicate-record:20240801T1000000000000Z:line-1");
        await catalog.AppendFindingAsync(new DetailedFindingHeader(
            duplicate,
            FindingCategory.DuplicateRecord,
            "Duplicate records",
            "Records share a timestamp.",
            3,
            new FindingLocation(new[] { 1L, 2L, 3L, 4L }, Ts(1, 10)),
            EvidenceKind.DuplicateRecord,
            "Review the duplicate rows."));

        var result = await catalog.CompleteAsync();
        Assert.IsType<CompletedFindingCatalogResult.Succeeded>(result);

        var statistics = catalog.Statistics;
        Assert.Equal(2, statistics.MissingCandles.EntryCount);
        Assert.Equal(2, statistics.MissingCandles.ContributionSum);
        Assert.Equal(1, statistics.DuplicateRecords.EntryCount);
        Assert.Equal(3, statistics.DuplicateRecords.ContributionSum);
        Assert.Equal(0, statistics.InvalidOhlc.ContributionSum);
    }

    [Fact]
    public async Task CompleteThenRead_IsReplayableAndIdempotent()
    {
        var catalog = CreateCatalog();
        await catalog.AppendFindingAsync(Header(CandleReference));
        await catalog.AppendLocationLineAsync(CandleReference, 7);

        var first = await catalog.CompleteAsync();
        var second = await catalog.CompleteAsync();
        Assert.IsType<CompletedFindingCatalogResult.Succeeded>(first);
        Assert.IsType<CompletedFindingCatalogResult.Succeeded>(second);

        var references = new List<string>();
        await foreach (var cursor in ((CompletedFindingCatalogResult.Succeeded)first).Catalog.ReadCanonicalAsync())
        {
            references.Add(cursor.Header.Reference.Value);
        }

        var replay = new List<string>();
        await foreach (var cursor in ((CompletedFindingCatalogResult.Succeeded)second).Catalog.ReadCanonicalAsync())
        {
            replay.Add(cursor.Header.Reference.Value);
        }

        Assert.Equal(references, replay);
    }

    [Fact]
    public async Task DisposeAsync_DeletesAllTemporarySpools()
    {
        var catalog = CreateCatalog();
        await catalog.AppendFindingAsync(Header(CandleReference));
        await catalog.AppendFindingAsync(Header(GapReference));
        await catalog.AppendLocationLineAsync(CandleReference, 7);
        await catalog.AppendRelationshipPairAsync(
            new FindingRelationship(RelationshipKind.PartOfGap, GapReference),
            new FindingRelationship(RelationshipKind.ContainsMissingCandle, CandleReference));

        var result = await catalog.CompleteAsync();
        Assert.IsType<CompletedFindingCatalogResult.Succeeded>(result);

        var spoolCount = _store.Spools.Count;
        Assert.True(spoolCount > 0);

        await catalog.DisposeAsync();
        Assert.Empty(_store.Spools);
    }

    [Fact]
    public async Task AppendingToCompletedCatalog_IsRejected()
    {
        var catalog = CreateCatalog();
        await catalog.AppendFindingAsync(Header(CandleReference));
        await catalog.CompleteAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.AppendFindingAsync(Header(GapReference)).AsTask());
    }

    [Fact]
    public async Task MultiReferenceChildren_ReadBackFromCanonicalSpools()
    {
        var catalog = CreateCatalog();
        var later = new FindingReference("missing-candle:20240802T1000000000000Z");
        var earlier = new FindingReference("missing-candle:20240801T1000000000000Z");
        await catalog.AppendFindingAsync(Header(earlier));
        await catalog.AppendFindingAsync(Header(later));
        await catalog.AppendLocationLineAsync(later, 7);
        await catalog.AppendLocationLineAsync(earlier, 3);
        await catalog.AppendLocationLineAsync(later, 9);
        await catalog.AppendLocationLineAsync(earlier, 5);

        var result = await catalog.CompleteAsync();
        var completed = Assert.IsType<CompletedFindingCatalogResult.Succeeded>(result);

        var linesByReference = new Dictionary<string, List<long>>();
        await foreach (var cursor in completed.Catalog.ReadCanonicalAsync())
        {
            var lines = new List<long>();
            await foreach (var line in cursor.ReadSourceLinesAsync())
            {
                lines.Add(line);
            }

            linesByReference.Add(cursor.Header.Reference.Value, lines);
        }

        Assert.Equal(new[] { 3L, 5L }, linesByReference[earlier.Value]);
        Assert.Equal(new[] { 7L, 9L }, linesByReference[later.Value]);
    }

    [Fact]
    public async Task EmptyCatalog_CompletesWithCleanStatistics()
    {
        var catalog = CreateCatalog();
        var result = await catalog.CompleteAsync();
        var completed = Assert.IsType<CompletedFindingCatalogResult.Succeeded>(result);

        var references = new List<string>();
        await foreach (var cursor in completed.Catalog.ReadCanonicalAsync())
        {
            references.Add(cursor.Header.Reference.Value);
        }

        Assert.Empty(references);
        var statistics = completed.Catalog.Statistics;
        Assert.Equal(0, statistics.MissingCandles.ContributionSum
            + statistics.DuplicateRecords.ContributionSum
            + statistics.InvalidOhlc.ContributionSum
            + statistics.ClosedMarketRecords.ContributionSum
            + statistics.TimeGaps.ContributionSum
            + statistics.MalformedRows.ContributionSum);
    }


}