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
using Xunit;
using InMemorySpool = Validator.Application.Tests.Reporting.InMemorySpool;
using InMemorySpoolReader = Validator.Application.Tests.Reporting.InMemorySpoolReader;
using InMemorySpoolStore = Validator.Application.Tests.Reporting.InMemorySpoolStore;

namespace Validator.Application.Tests.Validation;

public sealed class DetailedValidationOrchestratorTests
{
    private readonly InMemorySpoolStore _store = new();

    private static readonly string Sha256 = new('a', 64);

    private DetailedValidationOrchestrator CreateOrchestrator() => new(
        () => new FindingCatalog(
            () => new InMemorySpool(_store),
            path => new InMemorySpoolReader(_store.Spools[path])));

    private static DateTimeOffset Ts(int day, int hour) =>
        new(2024, 8, day, hour, 0, 0, TimeSpan.Zero);

    private static PriceCandle Candle(DateTimeOffset timestamp, decimal open, decimal high, decimal low, decimal close, long line) =>
        new(timestamp, open, high, low, close, 10m, line);

    private static DetailedValidationRequest CreateRequest(
        IPreparedCandleSource source,
        IMarketCalendar calendar,
        ValidationOptions? options = null) =>
        new(
            "test.csv",
            source,
            options ?? new ValidationOptions { TimeframeOverride = "H1" },
            calendar,
            new CsvInputOptions());

    private static ScanCoverage Coverage(int accepted, int malformed) =>
        new(accepted + malformed, accepted, malformed);

    private static PreparedCandleDataResult SucceededResult(
        IReadOnlyList<PriceCandle> candles,
        IReadOnlyList<MalformedRow> malformed,
        ScanCoverage coverage) =>
        new PreparedCandleDataResult.Succeeded(
            new FakeReplayableData(candles),
            new SourceIdentity("test.csv", 1024, Sha256),
            new ResolvedCsvContext(
                ',',
                false,
                TimestampInterpretation.CreateCombined("yyyy-MM-dd HH:mm:ss", "timestamp", "+02:00"),
                null),
            coverage);

    [Fact]
    public async Task Execute_AllSixEstablishedChecks_ProduceTypedFindingsWithTraceability()
    {
        var candles = new List<PriceCandle>
        {
            Candle(Ts(1, 9), 10m, 11m, 9m, 10m, 2),
            Candle(Ts(1, 9), 11m, 12m, 10m, 11m, 3),
            Candle(Ts(1, 10), 5m, 6m, 4m, 5m, 4),
            Candle(Ts(1, 11), 100m, 1m, 0m, 50m, 5),
            Candle(Ts(1, 12), 20m, 21m, 19m, 20m, 6),
            Candle(Ts(1, 14), 30m, 31m, 29m, 30m, 7)
        };
        var malformed = new List<MalformedRow>
        {
            new(8, "garbage,row", "Unparsable timestamp")
        };
        var source = new FakePreparedSource(
            candles,
            malformed,
            SucceededResult(candles, malformed, Coverage(6, 1)));
        var orchestrator = CreateOrchestrator();

        var outcome = await orchestrator.ExecuteAsync(
            CreateRequest(source, new ClosedAtHourCalendar(10)));

        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;

        Assert.Equal(ReportStatus.FindingsDetected, report.Status);
        Assert.True(report.FindingSetComplete);
        Assert.Equal(2, report.ContractVersion);
        Assert.Equal("test.csv", report.Source.FileName);
        Assert.Equal("H1", report.Context.Timeframe);
        Assert.Equal("equities", report.Context.Calendar.Profile);
        Assert.Equal("comma", report.Context.Delimiter);
        Assert.False(report.Context.HasHeader);

        Assert.Equal(new DetailedSummary(1, 1, 1, 1, 1, 1), report.Summary);

        var expectedChecks = new[]
        {
            CheckName.MissingCandles,
            CheckName.DuplicateRecords,
            CheckName.InvalidOhlc,
            CheckName.ClosedMarketRecords,
            CheckName.TimeGaps,
            CheckName.MalformedRows
        };
        Assert.Equal(expectedChecks, report.Checks.Select(check => check.Check));
        Assert.All(report.Checks, check => Assert.Equal(CheckStatus.Completed, check.Status));

        foreach (var category in report.Reconciliation.Categories)
        {
            Assert.Equal(report.Summary.For(category.Category), category.SummaryCount);
            Assert.Equal(report.Summary.For(category.Category), category.ContributionSum);
        }

        var cursors = await ReadAllCursorsAsync(catalog);
        Assert.Equal(6, cursors.Count);

        var closed = Assert.Single(cursors, cursor => cursor.Header.Reference.Value == "closed-market-record:line-4");
        Assert.Equal(FindingCategory.ClosedMarketRecord, closed.Header.Category);
        Assert.Equal(new[] { 4L }, await ReadLocationLinesAsync(closed));
        var closedEvidence = await ReadEvidenceAsync(closed);
        var closedRecord = Assert.IsType<FindingEvidenceRecord.ClosedMarket>(Assert.Single(closedEvidence)).Evidence;
        Assert.Equal("equities", closedRecord.MarketProfile);
        Assert.Equal("Equities", closedRecord.CalendarName);
        Assert.Equal("RecurringClosedRule", closedRecord.ClosedRule);

        var duplicate = Assert.Single(cursors, cursor => cursor.Header.Reference.Value == "duplicate-record:20240801T0900000000000Z:line-2");
        Assert.Equal(FindingCategory.DuplicateRecord, duplicate.Header.Category);
        Assert.Equal(1, duplicate.Header.CountContribution);
        Assert.Equal(new[] { 2L, 3L }, await ReadLocationLinesAsync(duplicate));
        var duplicateEvidence = await ReadEvidenceAsync(duplicate);
        var duplicateHeader = Assert.IsType<FindingEvidenceRecord.DuplicateHeader>(duplicateEvidence[0]).Evidence;
        Assert.Equal(DuplicateClassification.Conflicting, duplicateHeader.Classification);
        Assert.Equal(new[] { "Close", "High", "Low", "Open" }, duplicateHeader.DifferingFields);
        Assert.Equal(2, duplicateEvidence.Count(record => record is FindingEvidenceRecord.DuplicateRow));
        Assert.Equal(4, duplicateEvidence.Count(record => record is FindingEvidenceRecord.DuplicateDifferingField));

        var invalid = Assert.Single(cursors, cursor => cursor.Header.Reference.Value == "invalid-ohlc:line-5");
        Assert.Equal(FindingCategory.InvalidOhlc, invalid.Header.Category);
        Assert.Equal(new[] { 5L }, await ReadLocationLinesAsync(invalid));
        var invalidEvidence = await ReadEvidenceAsync(invalid);
        var observed = Assert.IsType<FindingEvidenceRecord.InvalidOhlcValues>(invalidEvidence[0]).Observed;
        Assert.Equal((100m, 1m, 0m, 50m, 10m), (observed.Open, observed.High, observed.Low, observed.Close, observed.Volume));
        var violationCodes = invalidEvidence
            .OfType<FindingEvidenceRecord.InvalidOhlcViolation>()
            .Select(record => record.Code)
            .ToArray();
        Assert.Equal(
            new[] { OhlcViolationCode.HIGH_BELOW_OPEN, OhlcViolationCode.HIGH_BELOW_CLOSE, OhlcViolationCode.NON_POSITIVE_LOW },
            violationCodes);

        var gapReference = new FindingReference("time-gap:20240801T1300000000000Z:20240801T1300000000000Z");
        var missing = Assert.Single(cursors, cursor => cursor.Header.Reference.Value == "missing-candle:20240801T1300000000000Z");
        Assert.Equal(FindingCategory.MissingCandle, missing.Header.Category);
        Assert.Empty(await ReadLocationLinesAsync(missing));
        Assert.Equal(Ts(1, 13), missing.Header.Location.TimestampUtc);
        var missingEvidence = Assert.IsType<FindingEvidenceRecord.MissingCandle>(
            Assert.Single(await ReadEvidenceAsync(missing))).Evidence;
        Assert.Equal(Ts(1, 13), missingEvidence.ExpectedTimestampUtc);
        Assert.Equal(gapReference, missingEvidence.TimeGapReference);
        Assert.Equal(Ts(1, 12), missingEvidence.PreviousObservedTimestampUtc);
        Assert.Equal(Ts(1, 14), missingEvidence.NextObservedTimestampUtc);
        var missingRelationships = await ReadRelationshipsAsync(missing);
        var partOfGap = Assert.Single(missingRelationships, relationship => relationship.Kind == RelationshipKind.PartOfGap);
        Assert.Equal(gapReference, partOfGap.TargetReference);

        var gap = Assert.Single(cursors, cursor => cursor.Header.Reference.Value == gapReference.Value);
        Assert.Equal(FindingCategory.TimeGap, gap.Header.Category);
        var gapEvidence = Assert.IsType<FindingEvidenceRecord.TimeGapHeader>(
            Assert.Single(await ReadEvidenceAsync(gap), record => record is FindingEvidenceRecord.TimeGapHeader)).Evidence;
        Assert.Equal(Ts(1, 13), gapEvidence.FirstMissingTimestampUtc);
        Assert.Equal(Ts(1, 13), gapEvidence.LastMissingTimestampUtc);
        Assert.Equal(1, gapEvidence.MissingCandleCount);
        Assert.Equal(3600, gapEvidence.ElapsedSeconds);
        Assert.Equal(Ts(1, 12), gapEvidence.PreviousObservedTimestampUtc);
        Assert.Equal(Ts(1, 14), gapEvidence.NextObservedTimestampUtc);
        var gapChild = Assert.Single(
            await ReadEvidenceAsync(gap),
            record => record is FindingEvidenceRecord.TimeGapMissingReference);
        Assert.Equal(
            new FindingReference("missing-candle:20240801T1300000000000Z"),
            Assert.IsType<FindingEvidenceRecord.TimeGapMissingReference>(gapChild).TargetReference);
        var gapRelationships = await ReadRelationshipsAsync(gap);
        var contains = Assert.Single(gapRelationships, relationship => relationship.Kind == RelationshipKind.ContainsMissingCandle);
        Assert.Equal(new FindingReference("missing-candle:20240801T1300000000000Z"), contains.TargetReference);

        var malformedFinding = Assert.Single(cursors, cursor => cursor.Header.Reference.Value == "malformed-row:line-8");
        Assert.Equal(FindingCategory.MalformedRow, malformedFinding.Header.Category);
        Assert.Equal(new[] { 8L }, await ReadLocationLinesAsync(malformedFinding));
        var malformedEvidence = await ReadEvidenceAsync(malformedFinding);
        var malformedHeader = Assert.IsType<FindingEvidenceRecord.MalformedHeader>(malformedEvidence[0]).Evidence;
        Assert.Equal(8, malformedHeader.SourceLine);
        Assert.Null(malformedHeader.ParsedTimestampUtc);
        Assert.False(malformedHeader.ExpectedSlotReserved);
        Assert.Single(malformedEvidence, record => record is FindingEvidenceRecord.MalformedFieldErrorRecord);
        Assert.Equal(3, malformedEvidence.Count(record => record is FindingEvidenceRecord.MalformedSkippedCheck));
    }

    [Fact]
    public async Task Execute_CanonicalOrder_MatchesReferenceOrderAcrossCategories()
    {
        var candles = new List<PriceCandle>
        {
            Candle(Ts(1, 10), 10m, 11m, 9m, 10m, 3),
            Candle(Ts(1, 10), 11m, 12m, 10m, 11m, 4),
            Candle(Ts(1, 11), 100m, 1m, 0m, 50m, 5)
        };
        var malformed = new List<MalformedRow>
        {
            new(6, "garbage,row", "Unparsable timestamp")
        };
        var source = new FakePreparedSource(candles, malformed, SucceededResult(candles, malformed, Coverage(3, 1)));
        var orchestrator = CreateOrchestrator();

        var outcome = await orchestrator.ExecuteAsync(CreateRequest(source, new AlwaysOpenCalendar()));
        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;

        var references = new List<string>();
        await foreach (var cursor in catalog.ReadCanonicalAsync())
        {
            references.Add(cursor.Header.Reference.Value);
        }

        Assert.Equal(
            new[]
            {
                "duplicate-record:20240801T1000000000000Z:line-3",
                "invalid-ohlc:line-5",
                "malformed-row:line-6"
            },
            references);
    }

    [Fact]
    public async Task Execute_SequenceChecksAreNotApplicable_WhenFewerThanTwoOpenTimestamps()
    {
        var candles = new List<PriceCandle>
        {
            Candle(Ts(1, 9), 5m, 6m, 4m, 5m, 2),
            Candle(Ts(1, 10), 6m, 7m, 5m, 6m, 3)
        };
        var source = new FakePreparedSource(candles, [], SucceededResult(candles, [], Coverage(2, 0)));
        var orchestrator = CreateOrchestrator();

        var outcome = await orchestrator.ExecuteAsync(
            CreateRequest(source, new ClosedAtHourCalendar(10)));

        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;

        Assert.Equal(CheckStatus.NotApplicable, report.Checks[0].Status);
        Assert.Equal(CheckStatus.NotApplicable, report.Checks[4].Status);
        Assert.NotNull(report.Checks[0].Reason);
        Assert.NotNull(report.Checks[4].Reason);
        Assert.Equal(CheckStatus.Completed, report.Checks[1].Status);
        Assert.Equal(CheckStatus.Completed, report.Checks[2].Status);
        Assert.Equal(CheckStatus.Completed, report.Checks[3].Status);
        Assert.Equal(CheckStatus.Completed, report.Checks[5].Status);

        Assert.Equal(new DetailedSummary(0, 0, 0, 1, 0, 0), report.Summary);
        Assert.Equal(ReportStatus.FindingsDetected, report.Status);
    }

    [Fact]
    public async Task Execute_SingleMissingCandle_ProducesGapWithOneCandle()
    {
        var candles = new List<PriceCandle>
        {
            Candle(Ts(1, 10), 10m, 11m, 9m, 10m, 2),
            Candle(Ts(1, 12), 20m, 21m, 19m, 20m, 3)
        };
        var source = new FakePreparedSource(candles, [], SucceededResult(candles, [], Coverage(2, 0)));
        var orchestrator = CreateOrchestrator();

        var outcome = await orchestrator.ExecuteAsync(CreateRequest(source, new AlwaysOpenCalendar()));
        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;

        var cursors = await ReadAllCursorsAsync(catalog);
        Assert.Equal(2, cursors.Count);
        var gap = Assert.Single(cursors, cursor => cursor.Header.Category == FindingCategory.TimeGap);
        var missing = Assert.Single(cursors, cursor => cursor.Header.Category == FindingCategory.MissingCandle);

        Assert.Equal(new DetailedSummary(1, 0, 0, 0, 1, 0), report.Summary);
        Assert.Equal("time-gap:20240801T1100000000000Z:20240801T1100000000000Z", gap.Header.Reference.Value);
        Assert.Equal("missing-candle:20240801T1100000000000Z", missing.Header.Reference.Value);

        var gapEvidence = Assert.IsType<FindingEvidenceRecord.TimeGapHeader>(
            Assert.Single(await ReadEvidenceAsync(gap), record => record is FindingEvidenceRecord.TimeGapHeader)).Evidence;
        Assert.Equal(Ts(1, 10), gapEvidence.PreviousObservedTimestampUtc);
        Assert.Equal(Ts(1, 12), gapEvidence.NextObservedTimestampUtc);

        var missingEvidence = Assert.IsType<FindingEvidenceRecord.MissingCandle>(
            Assert.Single(await ReadEvidenceAsync(missing))).Evidence;
        Assert.Equal(Ts(1, 10), missingEvidence.PreviousObservedTimestampUtc);
        Assert.Equal(Ts(1, 12), missingEvidence.NextObservedTimestampUtc);
        Assert.Equal(gap.Header.Reference, missingEvidence.TimeGapReference);
    }

    [Fact]
    public async Task Execute_MultiCandleGap_CountsEveryMissingCandleAndElapsedSeconds()
    {
        var candles = new List<PriceCandle>
        {
            Candle(Ts(1, 9), 5m, 6m, 4m, 5m, 2),
            Candle(Ts(1, 13), 20m, 21m, 19m, 20m, 3)
        };
        var source = new FakePreparedSource(candles, [], SucceededResult(candles, [], Coverage(2, 0)));
        var orchestrator = CreateOrchestrator();

        var outcome = await orchestrator.ExecuteAsync(CreateRequest(source, new AlwaysOpenCalendar()));
        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;

        var cursors = await ReadAllCursorsAsync(catalog);
        Assert.Equal(3, cursors.Count(cursor => cursor.Header.Category == FindingCategory.MissingCandle));
        var gap = Assert.Single(cursors, cursor => cursor.Header.Category == FindingCategory.TimeGap);

        Assert.Equal(new DetailedSummary(3, 0, 0, 0, 1, 0), report.Summary);
        Assert.Equal("time-gap:20240801T1000000000000Z:20240801T1200000000000Z", gap.Header.Reference.Value);

        var gapEvidence = Assert.IsType<FindingEvidenceRecord.TimeGapHeader>(
            Assert.Single(await ReadEvidenceAsync(gap), record => record is FindingEvidenceRecord.TimeGapHeader)).Evidence;
        Assert.Equal(Ts(1, 10), gapEvidence.FirstMissingTimestampUtc);
        Assert.Equal(Ts(1, 12), gapEvidence.LastMissingTimestampUtc);
        Assert.Equal(3, gapEvidence.MissingCandleCount);
        Assert.Equal(3 * 3600, gapEvidence.ElapsedSeconds);

        var childReferences = (await ReadEvidenceAsync(gap))
            .OfType<FindingEvidenceRecord.TimeGapMissingReference>()
            .Select(record => record.TargetReference.Value)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "missing-candle:20240801T1000000000000Z",
                "missing-candle:20240801T1100000000000Z",
                "missing-candle:20240801T1200000000000Z"
            },
            childReferences);

        var relationships = await ReadRelationshipsAsync(gap);
        Assert.Equal(3, relationships.Count);
        Assert.All(relationships, relationship => Assert.Equal(RelationshipKind.ContainsMissingCandle, relationship.Kind));
    }

    [Fact]
    public async Task Execute_DuplicateGroup_ContributesRowsMinusOneAndStreamsEveryRow()
    {
        var candles = new List<PriceCandle>
        {
            Candle(Ts(1, 10), 10m, 11m, 9m, 10m, 2),
            Candle(Ts(1, 10), 10m, 11m, 9m, 12m, 3),
            Candle(Ts(1, 10), 10m, 11m, 9m, 13m, 4)
        };
        var source = new FakePreparedSource(candles, [], SucceededResult(candles, [], Coverage(3, 0)));
        var orchestrator = CreateOrchestrator();

        var outcome = await orchestrator.ExecuteAsync(CreateRequest(source, new AlwaysOpenCalendar()));
        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;

        var cursors = await ReadAllCursorsAsync(catalog);
        var duplicate = Assert.Single(cursors, cursor => cursor.Header.Category == FindingCategory.DuplicateRecord);
        Assert.Equal(2, duplicate.Header.CountContribution);
        Assert.Equal(new[] { 2L, 3L, 4L }, await ReadLocationLinesAsync(duplicate));

        var evidence = await ReadEvidenceAsync(duplicate);
        var header = Assert.IsType<FindingEvidenceRecord.DuplicateHeader>(evidence[0]).Evidence;
        Assert.Equal(DuplicateClassification.Conflicting, header.Classification);
        Assert.Equal(new[] { "Close" }, header.DifferingFields);
        var rows = evidence.OfType<FindingEvidenceRecord.DuplicateRow>().ToArray();
        Assert.Equal(new[] { 2L, 3L, 4L }, rows.Select(record => record.Row.SourceLine));
        Assert.Equal(new[] { 10m, 12m, 13m }, rows.Select(record => record.Row.Close));
    }

    [Fact]
    public async Task Execute_CleanDataset_ProducesCleanReportWithCompletedChecks()
    {
        var candles = new List<PriceCandle>
        {
            Candle(Ts(1, 10), 10m, 11m, 9m, 10m, 2),
            Candle(Ts(1, 11), 20m, 21m, 19m, 20m, 3)
        };
        var source = new FakePreparedSource(candles, [], SucceededResult(candles, [], Coverage(2, 0)));
        var orchestrator = CreateOrchestrator();

        var outcome = await orchestrator.ExecuteAsync(CreateRequest(source, new AlwaysOpenCalendar()));
        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;

        Assert.Equal(ReportStatus.Clean, report.Status);
        Assert.True(report.Summary.IsClean);
        Assert.All(report.Checks, check => Assert.Equal(CheckStatus.Completed, check.Status));
        Assert.Empty(await ReadAllCursorsAsync(catalog));
    }

    [Fact]
    public async Task Execute_FailedSourcePreparation_ReturnsFatalWithoutCreatingCatalog()
    {
        var diagnostic = new FatalDiagnostic(
            "INVALID_CSV",
            "The file is not a valid CSV.",
            "Fix the file or supply a different one.");
        var source = new FailingPreparedSource(diagnostic);
        var catalogCreated = false;
        var orchestrator = new DetailedValidationOrchestrator(() =>
        {
            catalogCreated = true;
            return new FindingCatalog(
                () => new InMemorySpool(_store),
                path => new InMemorySpoolReader(_store.Spools[path]));
        });

        var outcome = await orchestrator.ExecuteAsync(CreateRequest(source, new AlwaysOpenCalendar()));

        var failed = Assert.IsType<DetailedValidationOutcome.Failed>(outcome);
        Assert.Equal("INVALID_CSV", failed.Diagnostic.Code);
        Assert.Equal(FailureStage.Ingestion, failed.Diagnostic.Stage);
        Assert.All(failed.Diagnostic.Checks, check => Assert.Equal(CheckStatus.NotCompleted, check.Status));
        Assert.False(catalogCreated);
    }

    [Fact]
    public async Task Execute_EmptyDataset_ReturnsAmbiguousTimeframeFatal()
    {
        var source = new FakePreparedSource([], [], SucceededResult([], [], Coverage(0, 0)));
        var catalogCreated = false;
        var orchestrator = new DetailedValidationOrchestrator(() =>
        {
            catalogCreated = true;
            return new FindingCatalog(
                () => new InMemorySpool(_store),
                path => new InMemorySpoolReader(_store.Spools[path]));
        });

        var outcome = await orchestrator.ExecuteAsync(
            CreateRequest(source, new AlwaysOpenCalendar(), new ValidationOptions()));

        var failed = Assert.IsType<DetailedValidationOutcome.Failed>(outcome);
        Assert.Equal("AMBIGUOUS_TIMEFRAME", failed.Diagnostic.Code);
        Assert.Equal(FailureStage.TimeframeResolution, failed.Diagnostic.Stage);
        Assert.False(catalogCreated);
    }

    [Fact]
    public async Task Execute_InvalidTimeframeOverride_ReturnsInvalidArgumentFatal()
    {
        var candles = new List<PriceCandle> { Candle(Ts(1, 10), 10m, 11m, 9m, 10m, 2) };
        var source = new FakePreparedSource(candles, [], SucceededResult(candles, [], Coverage(1, 0)));
        var orchestrator = CreateOrchestrator();

        var outcome = await orchestrator.ExecuteAsync(
            CreateRequest(source, new AlwaysOpenCalendar(), new ValidationOptions { TimeframeOverride = "X99" }));

        var failed = Assert.IsType<DetailedValidationOutcome.Failed>(outcome);
        Assert.Equal("INVALID_ARGUMENT", failed.Diagnostic.Code);
        Assert.Equal(FailureStage.ArgumentValidation, failed.Diagnostic.Stage);
    }

    private static async Task<List<IDetailedFindingCursor>> ReadAllCursorsAsync(ICompletedFindingCatalog catalog)
    {
        var cursors = new List<IDetailedFindingCursor>();
        await foreach (var cursor in catalog.ReadCanonicalAsync())
        {
            cursors.Add(cursor);
        }

        return cursors;
    }

    private static async Task<List<long>> ReadLocationLinesAsync(IDetailedFindingCursor cursor)
    {
        var lines = new List<long>();
        await foreach (var line in cursor.ReadSourceLinesAsync())
        {
            lines.Add(line);
        }

        return lines;
    }

    private static async Task<List<FindingRelationship>> ReadRelationshipsAsync(IDetailedFindingCursor cursor)
    {
        var relationships = new List<FindingRelationship>();
        await foreach (var relationship in cursor.ReadRelationshipsAsync())
        {
            relationships.Add(relationship);
        }

        return relationships;
    }

    private static async Task<List<FindingEvidenceRecord>> ReadEvidenceAsync(IDetailedFindingCursor cursor)
    {
        var evidence = new List<FindingEvidenceRecord>();
        await foreach (var record in cursor.ReadEvidenceAsync())
        {
            evidence.Add(record);
        }

        return evidence;
    }

    private sealed class FakeReplayableData : IReplayableCandleData
    {
        private readonly IReadOnlyList<PriceCandle> _candles;

        public FakeReplayableData(IReadOnlyList<PriceCandle> candles)
        {
            _candles = candles;
        }

        public IAsyncEnumerable<PriceCandle> ReplayAsync() => _candles.ToAsyncEnumerable();
    }

    private sealed class FakePreparedSource : IPreparedCandleSource, IMalformedRowSource
    {
        private readonly IReadOnlyList<PriceCandle> _candles;
        private readonly PreparedCandleDataResult _result;

        public FakePreparedSource(
            IReadOnlyList<PriceCandle> candles,
            IReadOnlyList<MalformedRow> malformed,
            PreparedCandleDataResult result)
        {
            _candles = candles;
            MalformedRows = malformed;
            _result = result;
        }

        public IReadOnlyList<MalformedRow> MalformedRows { get; }

        public IAsyncEnumerable<PriceCandle> ReadAllAsync() => _candles.ToAsyncEnumerable();

        public ValueTask<PreparedCandleDataResult> PrepareAsync(
            CsvInputOptions options,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_result);
    }

    private sealed class FailingPreparedSource : IPreparedCandleSource
    {
        private readonly FatalDiagnostic _diagnostic;

        public FailingPreparedSource(FatalDiagnostic diagnostic)
        {
            _diagnostic = diagnostic;
        }

        public IAsyncEnumerable<PriceCandle> ReadAllAsync() => Array.Empty<PriceCandle>().ToAsyncEnumerable();

        public ValueTask<PreparedCandleDataResult> PrepareAsync(
            CsvInputOptions options,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PreparedCandleDataResult>(new PreparedCandleDataResult.Failed(_diagnostic));
    }

    private sealed class AlwaysOpenCalendar : IMarketCalendar
    {
        public MarketProfile Profile => MarketProfile.Crypto;

        public bool IsOpen(DateTimeOffset timestamp) => true;
    }

    private sealed class ClosedAtHourCalendar : IMarketCalendar
    {
        private readonly int _closedHour;

        public ClosedAtHourCalendar(int closedHour)
        {
            _closedHour = closedHour;
        }

        public MarketProfile Profile => MarketProfile.Equities;

        public bool IsOpen(DateTimeOffset timestamp) => timestamp.Hour != _closedHour;
    }
}
