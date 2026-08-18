using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Validation;
using Validator.Domain.Calendars;
using Validator.Domain.Candles;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;
using InMemorySpool = Validator.Application.Tests.Reporting.InMemorySpool;
using InMemorySpoolReader = Validator.Application.Tests.Reporting.InMemorySpoolReader;
using InMemorySpoolStore = Validator.Application.Tests.Reporting.InMemorySpoolStore;

namespace Validator.Application.Tests;

// What happens when the machinery underneath a run fails, and the shapes of
// input that only a malformed row or an out-of-order timestamp produces. A run
// that cannot prove its own findings must refuse rather than publish, so each
// refusal route is exercised through the orchestrator that owns it.
public sealed class ApplicationRefusalPathTests
{
    private readonly InMemorySpoolStore _store = new();

    private static readonly string Sha256 = new('a', 64);

    private static DateTimeOffset Ts(int hour) => new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    private static PriceCandle Candle(DateTimeOffset timestamp, long line) =>
        new(timestamp, 10m, 11m, 9m, 10m, 10m, line);

    private FindingCatalog RealCatalog() => new(
        () => new InMemorySpool(_store),
        path => new InMemorySpoolReader(_store.Spools[path]));

    private static DetailedValidationRequest Request(
        IPreparedCandleSource source,
        string? timeframe = "H1") =>
        new(
            "test.csv",
            source,
            new ValidationOptions { TimeframeOverride = timeframe },
            new AlwaysOpenCalendar(),
            new CsvInputOptions());

    // ------------------------------------------------ the catalog cannot finish

    [Fact]
    public async Task Execute_RefusesToPublishWhenTheCatalogCannotBeCompleted()
    {
        // A report is only trustworthy if its findings were all written down. If
        // completing the catalog fails, the run reports that failure instead of a
        // report whose finding set is silently short.
        var candles = new List<PriceCandle> { Candle(Ts(10), 2), Candle(Ts(11), 3) };
        var diagnostic = new FatalDiagnostic(
            "VALIDATION_INCOMPLETE",
            "The finding catalog could not be completed.",
            "Retry the run; if it persists, report the temporary storage failure.");
        var orchestrator = new DetailedValidationOrchestrator(
            () => new FailingCompletionSink(diagnostic));

        var outcome = await orchestrator.ExecuteAsync(
            Request(new FakePreparedSource(candles, Succeeded(candles))));

        var failure = Assert.IsType<DetailedValidationOutcome.Failed>(outcome);
        Assert.Same(diagnostic, failure.Diagnostic);
        Assert.Equal("VALIDATION_INCOMPLETE", failure.Diagnostic.Code);
    }

    [Fact]
    public async Task Execute_DisposesTheCatalogWhenAFailureEndsTheRunEarly()
    {
        // Temporary spools are real resources. A refused run still has to release
        // them, or a long-lived process would leak on every failure.
        var candles = new List<PriceCandle> { Candle(Ts(10), 2), Candle(Ts(11), 3) };
        var sink = new FailingCompletionSink(new FatalDiagnostic(
            "VALIDATION_INCOMPLETE",
            "The finding catalog could not be completed.",
            "Retry the run."));
        var orchestrator = new DetailedValidationOrchestrator(() => sink);

        await orchestrator.ExecuteAsync(
            Request(new FakePreparedSource(candles, Succeeded(candles))));

        Assert.True(sink.Disposed);
    }

    [Fact]
    public void AReconciliation_CannotBeBuiltOverCoverageWhoseRowsDoNotAddUp()
    {
        // Every row read must be accounted for as either accepted or malformed. The
        // reconciliation refuses to exist over an unbalanced count, so an
        // unreconciled coverage can never reach the orchestrator's gate. That is
        // why the gate's own coverage branch is unreachable in a real run.
        var categories = new[]
        {
            FindingCategory.MissingCandle,
            FindingCategory.DuplicateRecord,
            FindingCategory.InvalidOhlc,
            FindingCategory.ClosedMarketRecord,
            FindingCategory.TimeGap,
            FindingCategory.MalformedRow
        }.Select(category => new CategoryReconciliation(category, 0, 0, 0)).ToList();

        var error = Assert.Throws<ArgumentException>(
            () => _ = new ReportReconciliation(categories, new ScanCoverage(99, 2, 0)));


        Assert.Equal("coverage", error.ParamName);
    }



    [Fact]
    public async Task Execute_ReportsThePreparationFailureItWasHandedWithoutStartingAnyChecks()
    {

        // If the source could not be read, there is nothing to check. The run
        // passes the ingestion diagnostic through rather than inventing a report.
        var diagnostic = new FatalDiagnostic(
            "SOURCE_UNAVAILABLE",
            "The source file could not be opened.",
            "Confirm the path exists and is readable.");
        var source = new FakePreparedSource([], new PreparedCandleDataResult.Failed(diagnostic));
        var orchestrator = new DetailedValidationOrchestrator(RealCatalog);

        var outcome = await orchestrator.ExecuteAsync(Request(source));

        Assert.Same(diagnostic, Assert.IsType<DetailedValidationOutcome.Failed>(outcome).Diagnostic);
    }

    // ------------------------------------------------------------ malformed rows

    [Fact]
    public async Task Execute_DoesNotClaimACandleIsMissingWhenAMalformedRowOccupiesThatSlot()
    {
        // A row that failed to parse still occupies its slot. Reporting the slot as
        // a missing candle as well would double-count one defect and send a reader
        // looking for data that is present but unreadable.
        var candles = new List<PriceCandle> { Candle(Ts(10), 2), Candle(Ts(12), 4) };
        var malformed = new MalformedRow(3, "bad,row", "Unparsable price", Ts(11));
        var source = new MalformedAwareSource(candles, Succeeded(candles), [malformed]);
        var orchestrator = new DetailedValidationOrchestrator(RealCatalog);

        var outcome = await orchestrator.ExecuteAsync(Request(source));

        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;
        var cursors = new List<IDetailedFindingCursor>();
        await foreach (var cursor in catalog.ReadCanonicalAsync())
        {
            cursors.Add(cursor);
        }

        Assert.Equal(0, report.Summary.MissingCandles);
        Assert.Equal(1, report.Summary.MalformedRows);
        Assert.DoesNotContain(cursors, cursor => cursor.Header.Category == FindingCategory.MissingCandle);
        Assert.Contains(cursors, cursor => cursor.Header.Category == FindingCategory.MalformedRow);
    }

    [Fact]
    public async Task Execute_StillReportsAMalformedRowWhoseTimestampWasNeverRecovered()
    {
        // A row too broken to yield a timestamp cannot be placed on the timeline,
        // but the reader still needs to know the line exists and why it failed.
        var candles = new List<PriceCandle> { Candle(Ts(10), 2), Candle(Ts(11), 4) };
        var malformed = new MalformedRow(3, "totally,broken", "Unparsable timestamp");
        var source = new MalformedAwareSource(candles, Succeeded(candles), [malformed]);
        var orchestrator = new DetailedValidationOrchestrator(RealCatalog);

        var outcome = await orchestrator.ExecuteAsync(Request(source));

        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;
        var cursors = new List<IDetailedFindingCursor>();
        await foreach (var cursor in catalog.ReadCanonicalAsync())
        {
            cursors.Add(cursor);
        }

        var row = Assert.Single(cursors, cursor => cursor.Header.Category == FindingCategory.MalformedRow);
        Assert.Equal([3L], row.Header.Location.SourceLines);
        Assert.Null(row.Header.Location.TimestampUtc);
        Assert.Equal(1, report.Summary.MalformedRows);
    }

    // ------------------------------------------------------------ closed unions

    [Fact]
    public void CategoryIndex_HasNoRoomForACategoryThatDoesNotExist()
    {
        // The catalog's per-category counters are a fixed set of six. A value
        // outside the declared categories would silently corrupt the summary, so
        // it is refused rather than mapped to an arbitrary slot.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FindingReferenceFactory.PhysicalRecord((FindingCategory)99, 1));
    }


    // ----------------------------------------------------------------- helpers

    private static PreparedCandleDataResult Succeeded(IReadOnlyList<PriceCandle> candles) =>
        new PreparedCandleDataResult.Succeeded(
            new FakeReplayableData(candles),
            new SourceIdentity("test.csv", 1024, Sha256),
            new ResolvedCsvContext(
                ',',
                false,
                TimestampInterpretation.CreateCombined("yyyy-MM-dd HH:mm:ss", "timestamp", "+00:00"),
                null),
            new ScanCoverage(candles.Count + 1, candles.Count, 1));

    private sealed class FakeReplayableData : IReplayableCandleData
    {
        private readonly IReadOnlyList<PriceCandle> _candles;

        public FakeReplayableData(IReadOnlyList<PriceCandle> candles)
        {
            _candles = candles;
        }

        public IAsyncEnumerable<PriceCandle> ReplayAsync() => _candles.ToAsyncEnumerable();
    }

    private class FakePreparedSource : IPreparedCandleSource
    {
        private readonly IReadOnlyList<PriceCandle> _candles;
        private readonly PreparedCandleDataResult _result;

        public FakePreparedSource(IReadOnlyList<PriceCandle> candles, PreparedCandleDataResult result)
        {
            _candles = candles;
            _result = result;
        }

        public IAsyncEnumerable<PriceCandle> ReadAllAsync() => _candles.ToAsyncEnumerable();

        public ValueTask<PreparedCandleDataResult> PrepareAsync(
            CsvInputOptions options,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_result);
    }

    private sealed class MalformedAwareSource : FakePreparedSource, IMalformedRowSource
    {
        public MalformedAwareSource(
            IReadOnlyList<PriceCandle> candles,
            PreparedCandleDataResult result,
            IReadOnlyList<MalformedRow> malformedRows)
            : base(candles, result)
        {
            MalformedRows = malformedRows;
        }

        public IReadOnlyList<MalformedRow> MalformedRows { get; }
    }

    // A sink that accepts everything and then refuses to complete, standing in
    // for temporary storage that fails only at the end of a run.
    private sealed class FailingCompletionSink : IDetailedFindingSink
    {
        private readonly FatalDiagnostic _diagnostic;

        public FailingCompletionSink(FatalDiagnostic diagnostic)
        {
            _diagnostic = diagnostic;
        }

        public bool Disposed { get; private set; }

        public ValueTask AppendFindingAsync(
            DetailedFindingHeader finding,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask AppendLocationLineAsync(
            FindingReference finding,
            long sourceLine,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask AppendEvidenceAsync(
            FindingEvidenceRecord evidence,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask AppendRelationshipPairAsync(
            FindingRelationship forward,
            FindingRelationship reverse,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<CompletedFindingCatalogResult> CompleteAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<CompletedFindingCatalogResult>(
                new CompletedFindingCatalogResult.Failed(_diagnostic));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AlwaysOpenCalendar : IMarketCalendar
    {

        public MarketProfile Profile => MarketProfile.Crypto;

        public bool IsOpen(DateTimeOffset timestamp) => true;
    }
}
