using System;
using System.IO;
using System.Linq;
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

// US5 (T056): verbose text labels both bracketing observed source lines for a
// missing candle and for its gap, labels an unavailable side `not applicable`,
// and still states that the absent record itself has no physical source line
// (FR-039, FR-040, per contracts/cli.md).
public sealed class VerboseReportWriterTests
{
    private const string NotApplicable = "not applicable";
    private static readonly Timeframe H1 = Timeframe.Parse("H1");
    private static readonly string Sha256 = new('b', 64);
    private static readonly FindingReference CandleRef = new("missing-candle:20240801T110000Z");
    private static readonly FindingReference GapRef = new("time-gap:20240801T110000Z:20240801T110000Z");

    private static DateTimeOffset Utc(int hour) => new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    private static ValidationContextSnapshot Context() => new(
        "H1",
        new CalendarContext("forex", "Forex"),
        TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+00:00"),
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

    private static async Task<string> RenderAbsenceAsync(
        long? previousLine,
        long? nextLine,
        DateTimeOffset? previousObserved,
        DateTimeOffset? nextObserved)
    {
        await using var owner = new FindingCatalog(
            () => new SpoolWriter(),
            path => new SpoolReader(path, path + ".complete"));

        await owner.AppendFindingAsync(new DetailedFindingHeader(
            CandleRef,
            FindingCategory.MissingCandle,
            "Missing candle",
            "An expected candle is absent from the dataset.",
            1,
            new FindingLocation(null, Utc(11)),
            EvidenceKind.MissingCandle,
            "Verify the source feed for the expected timestamp."));
        await owner.AppendEvidenceAsync(new FindingEvidenceRecord.MissingCandle(
            CandleRef,
            new MissingCandleEvidence(
                Utc(11), H1, GapRef, previousObserved, nextObserved, previousLine, nextLine)));

        await owner.AppendFindingAsync(new DetailedFindingHeader(
            GapRef,
            FindingCategory.TimeGap,
            "Time gap",
            "A contiguous run of expected candles is absent.",
            1,
            new FindingLocation(null, Utc(11)),
            EvidenceKind.TimeGap,
            "Investigate data discontinuities around the gap."));
        await owner.AppendEvidenceAsync(new FindingEvidenceRecord.TimeGapHeader(
            GapRef,
            new TimeGapEvidence(
                Utc(11), Utc(11), H1, 1, 3600, previousObserved, nextObserved, previousLine, nextLine)));
        await owner.AppendEvidenceAsync(new FindingEvidenceRecord.TimeGapMissingReference(GapRef, CandleRef, 0));

        var completion = await owner.CompleteAsync();
        var catalog = Assert.IsType<CompletedFindingCatalogResult.Succeeded>(completion).Catalog;

        var summary = new DetailedSummary(1, 0, 0, 0, 1, 0);
        var coverage = new ScanCoverage(3, 3, 0);
        var report = new DetailedValidationReport(
            new SourceIdentity("prices.csv", 2048, Sha256),
            Context(),
            coverage,
            AllCompleted(),
            summary,
            ReportReconciliation.Create(summary, coverage, catalog.Statistics),
            catalog);

        using var destination = new StringWriter();
        await new VerboseReportWriter().WriteAsync(report, destination);
        return destination.ToString();
    }

    // The evidence line belonging to one finding, located by the reference block
    // it follows, so an assertion does not depend on overall report layout.
    private static string EvidenceLineFor(string text, FindingReference reference, string containing)
    {
        var lines = text.Split('\n');
        var start = Array.FindIndex(lines, line => line.Contains($"reference={reference.Value}", StringComparison.Ordinal));
        Assert.True(start >= 0, $"The report does not mention {reference.Value}.");

        for (var index = start + 1; index < lines.Length; index++)
        {
            if (lines[index].Contains($"reference=", StringComparison.Ordinal) && !lines[index].StartsWith("    ", StringComparison.Ordinal))
            {
                break;
            }

            if (lines[index].Contains(containing, StringComparison.Ordinal))
            {
                return lines[index];
            }
        }

        Assert.Fail($"No line containing '{containing}' was found for {reference.Value}.");
        return string.Empty;
    }

    [Fact]
    public async Task WriteAsync_LabelsBothBracketingLinesForACandleAndItsGap()
    {
        var text = await RenderAbsenceAsync(7, 9, Utc(10), Utc(12));

        var candle = EvidenceLineFor(text, CandleRef, "previousObservedSourceLine");
        Assert.Contains("previousObservedSourceLine=7", candle, StringComparison.Ordinal);
        Assert.Contains("nextObservedSourceLine=9", candle, StringComparison.Ordinal);

        var gap = EvidenceLineFor(text, GapRef, "previousObservedSourceLine");
        Assert.Contains("previousObservedSourceLine=7", gap, StringComparison.Ordinal);
        Assert.Contains("nextObservedSourceLine=9", gap, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_LabelsTheUnavailableSideNotApplicable()
    {
        var startBoundary = await RenderAbsenceAsync(null, 9, null, Utc(12));
        foreach (var reference in new[] { CandleRef, GapRef })
        {
            var line = EvidenceLineFor(startBoundary, reference, "previousObservedSourceLine");
            Assert.Contains($"previousObservedSourceLine={NotApplicable}", line, StringComparison.Ordinal);
            Assert.Contains("nextObservedSourceLine=9", line, StringComparison.Ordinal);
        }

        var endBoundary = await RenderAbsenceAsync(7, null, Utc(10), null);
        foreach (var reference in new[] { CandleRef, GapRef })
        {
            var line = EvidenceLineFor(endBoundary, reference, "nextObservedSourceLine");
            Assert.Contains("previousObservedSourceLine=7", line, StringComparison.Ordinal);
            Assert.Contains($"nextObservedSourceLine={NotApplicable}", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task WriteAsync_StillStatesTheAbsentRecordHasNoPhysicalSourceLine()
    {
        var text = await RenderAbsenceAsync(7, 9, Utc(10), Utc(12));

        // Bracketing lines locate the absence; they never become the absent
        // record's own source line.
        var location = EvidenceLineFor(text, CandleRef, "location: sourceLines=");
        Assert.Contains($"sourceLines={NotApplicable}", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_KeepsBracketingLinesOnOneLinePerEvidenceRecord()
    {
        var text = await RenderAbsenceAsync(7, 9, Utc(10), Utc(12));

        // Source-derived values never introduce a new line, so exactly one
        // evidence line per absence carries the pair.
        var occurrences = text
            .Split('\n')
            .Count(line => line.Contains("previousObservedSourceLine", StringComparison.Ordinal));

        Assert.Equal(2, occurrences);
    }
}