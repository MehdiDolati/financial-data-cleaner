using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;
using Validator.Infrastructure.Findings;
using Validator.Infrastructure.Reporting;
using Xunit;

namespace Validator.Infrastructure.Tests.Reporting;

// A successful v2 report answers what is wrong, where it is, why it is wrong,
// and what to do, and it reconciles its own counts. Findings are emitted in one
// canonical order with UTC timestamps and no host-specific values.
public sealed class DetailedReportV2WriterTests
{
    private static readonly Timeframe H1 = Timeframe.Parse("H1");
    private static readonly string Sha256 = new('a', 64);
    private static readonly FindingReference MalformedRef = new("malformed-row:5");
    private static readonly FindingReference CandleRef = new("missing-candle:20240801T110000Z");
    private static readonly FindingReference GapRef = new("time-gap:20240801T110000Z:20240801T110000Z");

    private static DateTimeOffset Utc(int hour) => new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    // Findings are located by their stable reference, so an assertion about one
    // finding does not depend on its position in the canonical sequence.
    private static JsonElement FindingByReference(JsonDocument document, FindingReference reference) =>
        document.RootElement
            .GetProperty("findings")
            .EnumerateArray()
            .Single(finding => finding.GetProperty("reference").GetString() == reference.Value);

    private static FindingCatalog CreateCatalog() => new(
        () => new SpoolWriter(),
        path => new SpoolReader(path, path + ".complete"));

    private static async Task<ICompletedFindingCatalog> PopulateAsync(FindingCatalog catalog)
    {
        await catalog.AppendFindingAsync(new DetailedFindingHeader(
            MalformedRef,
            FindingCategory.MalformedRow,
            "Malformed row",
            "The row could not be interpreted as an OHLCV record.",
            1,
            new FindingLocation(new long[] { 5 }, null, "2024.08.01 12:00"),
            EvidenceKind.MalformedRow,
            "Fix the row or remove it from the source."));
        await catalog.AppendLocationLineAsync(MalformedRef, 5);
        await catalog.AppendEvidenceAsync(new FindingEvidenceRecord.MalformedHeader(
            MalformedRef,
            new MalformedRowEvidence(5, null, "2024.08.01 12:00")));
        await catalog.AppendEvidenceAsync(new FindingEvidenceRecord.MalformedFieldErrorRecord(
            MalformedRef,
            new MalformedFieldError("High", "abc", MalformedReasonCode.INVALID_VALUE, "The value is not a number."),
            0));
        await catalog.AppendEvidenceAsync(new FindingEvidenceRecord.MalformedSkippedCheck(
            MalformedRef,
            CheckName.InvalidOhlc,
            0));

        await catalog.AppendFindingAsync(new DetailedFindingHeader(
            CandleRef,
            FindingCategory.MissingCandle,
            "Missing candle",
            "An expected candle is absent from the source.",
            1,
            new FindingLocation(null, Utc(11)),
            EvidenceKind.MissingCandle,
            "Backfill the expected candle from the upstream feed."));
        await catalog.AppendEvidenceAsync(new FindingEvidenceRecord.MissingCandle(
            CandleRef,
            new MissingCandleEvidence(Utc(11), H1, GapRef, Utc(10), Utc(12))));

        await catalog.AppendFindingAsync(new DetailedFindingHeader(
            GapRef,
            FindingCategory.TimeGap,
            "Time gap",
            "A contiguous run of expected candles is absent.",
            1,
            new FindingLocation(null, Utc(11)),
            EvidenceKind.TimeGap,
            "Investigate data discontinuities around the gap."));
        await catalog.AppendEvidenceAsync(new FindingEvidenceRecord.TimeGapHeader(
            GapRef,
            new TimeGapEvidence(Utc(11), Utc(11), H1, 1, 3600, Utc(10), Utc(12))));
        await catalog.AppendEvidenceAsync(new FindingEvidenceRecord.TimeGapMissingReference(GapRef, CandleRef, 0));
        await catalog.AppendRelationshipPairAsync(
            new FindingRelationship(RelationshipKind.PartOfGap, GapRef),
            new FindingRelationship(RelationshipKind.ContainsMissingCandle, CandleRef));

        var result = await catalog.CompleteAsync();
        return Assert.IsType<CompletedFindingCatalogResult.Succeeded>(result).Catalog;
    }

    private static ValidationContextSnapshot Context() => new(
        "H1",
        new CalendarContext("forex", "Forex"),
        TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
        "comma",
        false,
        new DateRange(Utc(10), Utc(12)));

    private static CheckExecution[] AllCompleted() =>
    [
        new(CheckName.MissingCandles, CheckStatus.Completed),
        new(CheckName.DuplicateRecords, CheckStatus.Completed),
        new(CheckName.InvalidOhlc, CheckStatus.Completed),
        new(CheckName.ClosedMarketRecords, CheckStatus.Completed),
        new(CheckName.TimeGaps, CheckStatus.Completed),
        new(CheckName.MalformedRows, CheckStatus.Completed)
    ];

    private static async Task<(string Json, ICompletedFindingCatalog Catalog, FindingCatalog Owner)> RenderPopulatedAsync()
    {
        var owner = CreateCatalog();
        var catalog = await PopulateAsync(owner);
        var summary = new DetailedSummary(1, 0, 0, 0, 1, 1);
        var coverage = new ScanCoverage(4, 3, 1);
        var report = new DetailedValidationReport(
            new SourceIdentity("prices.csv", 4096, Sha256),
            Context(),
            coverage,
            AllCompleted(),
            summary,
            ReportReconciliation.Create(summary, coverage, catalog.Statistics),
            catalog);

        using var destination = new StringWriter();
        await new DetailedReportV2Writer().WriteAsync(report, destination);
        return (destination.ToString(), catalog, owner);
    }

    [Fact]
    public async Task WriteAsync_CleanReport_DeclaresCleanStatusAndCompleteEmptyFindingSet()
    {
        await using var owner = CreateCatalog();
        var completed = Assert.IsType<CompletedFindingCatalogResult.Succeeded>(await owner.CompleteAsync()).Catalog;
        var summary = new DetailedSummary(0, 0, 0, 0, 0, 0);
        var coverage = new ScanCoverage(3, 3, 0);
        var report = new DetailedValidationReport(
            new SourceIdentity("clean.csv", 512, Sha256),
            Context(),
            coverage,
            AllCompleted(),
            summary,
            ReportReconciliation.Create(summary, coverage, completed.Statistics),
            completed);

        using var destination = new StringWriter();
        await new DetailedReportV2Writer().WriteAsync(report, destination);

        using var document = JsonDocument.Parse(destination.ToString());
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("contractVersion").GetInt32());
        Assert.Equal("Clean", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("findingSetComplete").GetBoolean());
        Assert.Empty(root.GetProperty("findings").EnumerateArray());
        Assert.True(root.GetProperty("reconciliation").GetProperty("coverageReconciled").GetBoolean());
    }

    [Fact]
    public async Task WriteAsync_PopulatedReport_WritesSourceContextAndCoverage()
    {
        var (json, _, owner) = await RenderPopulatedAsync();
        await using var _owner = owner;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("FindingsDetected", root.GetProperty("status").GetString());

        var source = root.GetProperty("source");
        Assert.Equal("prices.csv", source.GetProperty("fileName").GetString());
        Assert.Equal(4096, source.GetProperty("byteSize").GetInt64());
        Assert.Equal(Sha256, source.GetProperty("sha256").GetString());

        var context = root.GetProperty("context");
        Assert.Equal("H1", context.GetProperty("timeframe").GetString());
        Assert.Equal("forex", context.GetProperty("calendar").GetProperty("profile").GetString());
        Assert.Equal("SeparateDateTime", context.GetProperty("timestamp").GetProperty("mode").GetString());
        Assert.Equal("yyyy.MM.dd", context.GetProperty("timestamp").GetProperty("dateFormat").GetString());
        Assert.Equal("+02:00", context.GetProperty("timestamp").GetProperty("sourceOffset").GetString());
        Assert.Equal("comma", context.GetProperty("delimiter").GetString());
        Assert.False(context.GetProperty("hasHeader").GetBoolean());
        Assert.Equal("2024-08-01T10:00:00Z", context.GetProperty("dateRange").GetProperty("from").GetString());
        Assert.Equal("2024-08-01T12:00:00Z", context.GetProperty("dateRange").GetProperty("to").GetString());

        var coverage = root.GetProperty("coverage");
        Assert.Equal(4, coverage.GetProperty("physicalRowsExamined").GetInt64());
        Assert.Equal(3, coverage.GetProperty("acceptedRows").GetInt64());
        Assert.Equal(1, coverage.GetProperty("malformedRows").GetInt64());
    }

    [Fact]
    public async Task WriteAsync_ListsSixChecksAndReconcilesEveryCategory()
    {
        var (json, _, owner) = await RenderPopulatedAsync();
        await using var _owner = owner;

        using var document = JsonDocument.Parse(json);
        var checks = document.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Equal(
            new[]
            {
                "MissingCandles",
                "DuplicateRecords",
                "InvalidOhlc",
                "ClosedMarketRecords",
                "TimeGaps",
                "MalformedRows"
            },
            checks.Select(check => check.GetProperty("check").GetString()).ToArray());
        Assert.All(checks, check => Assert.Equal("Completed", check.GetProperty("status").GetString()));
        Assert.All(checks, check => Assert.False(check.TryGetProperty("reason", out _)));

        var categories = document.RootElement
            .GetProperty("reconciliation")
            .GetProperty("categories")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(
            new[]
            {
                "MissingCandle",
                "DuplicateRecord",
                "InvalidOhlc",
                "ClosedMarketRecord",
                "TimeGap",
                "MalformedRow"
            },
            categories.Select(category => category.GetProperty("category").GetString()).ToArray());
        Assert.All(categories, category => Assert.Equal(
            category.GetProperty("summaryCount").GetInt64(),
            category.GetProperty("contributionSum").GetInt64()));

        var summary = document.RootElement.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("missingCandles").GetInt64());
        Assert.Equal(1, summary.GetProperty("timeGaps").GetInt64());
        Assert.Equal(1, summary.GetProperty("malformedRows").GetInt64());
        Assert.Equal(0, summary.GetProperty("duplicateRecords").GetInt64());
    }

    // Findings follow the established canonical order: category rank first, so
    // a missing candle precedes its gap, and malformed rows come last.
    [Fact]
    public async Task WriteAsync_EmitsFindingsInCanonicalOrderWithCompleteNarrative()
    {
        var (json, _, owner) = await RenderPopulatedAsync();
        await using var _owner = owner;

        using var document = JsonDocument.Parse(json);
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(
            new[] { CandleRef.Value, GapRef.Value, MalformedRef.Value },
            findings.Select(finding => finding.GetProperty("reference").GetString()).ToArray());

        Assert.All(findings, finding =>
        {
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("title").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("explanation").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("suggestedAction").GetString()));
            Assert.True(finding.GetProperty("countContribution").GetInt64() > 0);
            Assert.Equal(JsonValueKind.Array, finding.GetProperty("location").GetProperty("sourceLines").ValueKind);
        });
    }

    [Fact]
    public async Task WriteAsync_MalformedFinding_CarriesSourceLineFieldErrorsAndSkippedChecks()
    {
        var (json, _, owner) = await RenderPopulatedAsync();
        await using var _owner = owner;

        using var document = JsonDocument.Parse(json);
        var malformed = FindingByReference(document, MalformedRef);
        Assert.Equal(new long[] { 5 }, malformed
            .GetProperty("location")
            .GetProperty("sourceLines")
            .EnumerateArray()
            .Select(line => line.GetInt64())
            .ToArray());
        Assert.Equal("2024.08.01 12:00", malformed.GetProperty("location").GetProperty("originalTimestampText").GetString());

        var evidence = malformed.GetProperty("evidence");
        Assert.Equal("MalformedRow", evidence.GetProperty("kind").GetString());
        Assert.False(evidence.GetProperty("expectedSlotReserved").GetBoolean());
        var fieldError = evidence.GetProperty("fieldErrors").EnumerateArray().Single();
        Assert.Equal("High", fieldError.GetProperty("field").GetString());
        Assert.Equal("abc", fieldError.GetProperty("originalValue").GetString());
        Assert.Equal("INVALID_VALUE", fieldError.GetProperty("reasonCode").GetString());
        Assert.Equal("InvalidOhlc", evidence.GetProperty("checksNotApplied").EnumerateArray().Single().GetString());
    }

    [Fact]
    public async Task WriteAsync_MissingCandleAndGap_CrossReferenceEachOtherInBothDirections()
    {
        var (json, _, owner) = await RenderPopulatedAsync();
        await using var _owner = owner;

        using var document = JsonDocument.Parse(json);
        var candle = FindingByReference(document, CandleRef);
        var gap = FindingByReference(document, GapRef);

        Assert.Empty(candle.GetProperty("location").GetProperty("sourceLines").EnumerateArray());
        var candleEvidence = candle.GetProperty("evidence");
        Assert.Equal("MissingCandle", candleEvidence.GetProperty("kind").GetString());
        Assert.Equal("2024-08-01T11:00:00Z", candleEvidence.GetProperty("expectedTimestampUtc").GetString());
        Assert.Equal("H1", candleEvidence.GetProperty("expectedTimeframe").GetString());
        Assert.Equal(GapRef.Value, candleEvidence.GetProperty("timeGapReference").GetString());
        Assert.Equal("2024-08-01T10:00:00Z", candleEvidence.GetProperty("previousObservedTimestampUtc").GetString());
        var candleRelationship = candle.GetProperty("relationships").EnumerateArray().Single();
        Assert.Equal("PartOfGap", candleRelationship.GetProperty("kind").GetString());
        Assert.Equal(GapRef.Value, candleRelationship.GetProperty("targetReference").GetString());

        var gapEvidence = gap.GetProperty("evidence");
        Assert.Equal("TimeGap", gapEvidence.GetProperty("kind").GetString());
        Assert.Equal(1, gapEvidence.GetProperty("missingCandleCount").GetInt64());
        Assert.Equal(3600, gapEvidence.GetProperty("elapsedSeconds").GetInt64());
        Assert.Equal(
            CandleRef.Value,
            gapEvidence.GetProperty("missingCandleReferences").EnumerateArray().Single().GetString());
        var gapRelationship = gap.GetProperty("relationships").EnumerateArray().Single();
        Assert.Equal("ContainsMissingCandle", gapRelationship.GetProperty("kind").GetString());
        Assert.Equal(CandleRef.Value, gapRelationship.GetProperty("targetReference").GetString());
    }

    [Fact]
    public async Task WriteAsync_NeverExposesRetiredOrAmbiguousTotals()
    {
        var (json, _, owner) = await RenderPopulatedAsync();
        await using var _owner = owner;

        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.DoesNotContain("totalErrors", names);
        Assert.DoesNotContain("uniqueProblems", names);
        Assert.DoesNotContain("isClean", names);
        Assert.DoesNotContain("errors", names);
        Assert.Equal(
            new[]
            {
                "contractVersion",
                "status",
                "findingSetComplete",
                "source",
                "context",
                "coverage",
                "checks",
                "summary",
                "reconciliation",
                "findings"
            },
            names);
    }

    [Fact]
    public async Task WriteAsync_IsByteIdenticalAcrossRepeatedRunsOverTheSameCatalog()
    {
        var (first, catalog, owner) = await RenderPopulatedAsync();
        await using var _owner = owner;

        var summary = new DetailedSummary(1, 0, 0, 0, 1, 1);
        var coverage = new ScanCoverage(4, 3, 1);
        var report = new DetailedValidationReport(
            new SourceIdentity("prices.csv", 4096, Sha256),
            Context(),
            coverage,
            AllCompleted(),
            summary,
            ReportReconciliation.Create(summary, coverage, catalog.Statistics),
            catalog);

        using var destination = new StringWriter();
        await new DetailedReportV2Writer().WriteAsync(report, destination);

        Assert.Equal(first, destination.ToString());
    }
}
