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
using InMemorySpool = Validator.Application.Tests.Reporting.InMemorySpool;
using InMemorySpoolReader = Validator.Application.Tests.Reporting.InMemorySpoolReader;
using InMemorySpoolStore = Validator.Application.Tests.Reporting.InMemorySpoolStore;


namespace Validator.Application.Tests;

// The last reachable statements in the Application layer: the facts a request
// and a context are asked to restate, the hash format a source identity
// refuses, the one duplicate field that had not yet been seen to differ, and
// the refusal to guess a timeframe from irregular data.
public sealed class ApplicationFinalGapTests
{
    private readonly InMemorySpoolStore _store = new();

    private static readonly string Sha256 = new('a', 64);

    private static DateTimeOffset Utc(int hour, int minute = 0) =>
        new(2024, 8, 1, hour, minute, 0, TimeSpan.Zero);

    private DetailedValidationOrchestrator CreateOrchestrator() => new(
        () => new FindingCatalog(
            () => new InMemorySpool(_store),
            path => new InMemorySpoolReader(_store.Spools[path])));

    private static PriceCandle Candle(
        DateTimeOffset timestamp,
        long line,
        decimal volume = 10m) =>
        new(timestamp, 10m, 11m, 9m, 10m, volume, line);

    // ------------------------------------------------------ restated request facts

    [Fact]
    public void Request_RestatesTheSourceItWasGiven()
    {
        // A report names the source it describes, so the label the caller supplied
        // must survive on the request rather than being reconstructed later.
        var source = new FakePreparedSource([], null!);
        var options = new ValidationOptions { TimeframeOverride = "H1" };
        var calendar = new AlwaysOpenCalendar();
        var csv = new CsvInputOptions();

        var request = new DetailedValidationRequest("prices.csv", source, options, calendar, csv);

        Assert.Equal("prices.csv", request.SourceLabel);
        Assert.Same(source, request.CandleSource);
        Assert.Same(options, request.Options);
        Assert.Same(calendar, request.MarketCalendar);
        Assert.Same(csv, request.CsvOptions);
    }

    [Fact]
    public void ContextSnapshot_RestatesTheTimestampInterpretationItWasGiven()
    {
        // How timestamps were read is the fact that makes every other timestamp in
        // the report interpretable, so the context hands back exactly what it held.
        var timestamp = TimestampInterpretation.CreateCombined(
            "yyyy-MM-dd HH:mm:ss",
            "timestamp",
            "+02:00");

        var snapshot = new ValidationContextSnapshot(
            "H1",
            new CalendarContext("crypto", "Crypto"),
            timestamp,
            "comma",
            true,
            null);

        Assert.Same(timestamp, snapshot.Timestamp);
        Assert.True(snapshot.HasHeader);
        Assert.Null(snapshot.DateRange);
    }

    [Fact]
    public void ContextSnapshot_RejectsAMissingTimeframeRatherThanFailingLater()
    {
        var error = Assert.Throws<ArgumentException>(
            () => new ValidationContextSnapshot(
                null!,
                new CalendarContext("crypto", "Crypto"),
                TimestampInterpretation.CreateCombined("yyyy-MM-dd HH:mm:ss", "timestamp", "+00:00"),
                "comma",
                false,
                null));

        Assert.Equal("timeframe", error.ParamName);
    }

    // --------------------------------------------------------- source identity

    [Theory]
    [InlineData('A')]
    [InlineData('F')]
    [InlineData('g')]
    [InlineData('z')]
    [InlineData('/')]
    [InlineData(':')]
    [InlineData('`')]
    public void SourceIdentity_RejectsAHashThatIsNotLowerCaseHexadecimal(char intruder)
    {
        // The hash is how a reader confirms they hold the same bytes the run read.
        // A hash in an unexpected alphabet cannot be compared reliably, so it is
        // refused rather than stored and later mismatched.
        var hash = new string('a', 63) + intruder;

        var error = Assert.Throws<ArgumentException>(() => new SourceIdentity("prices.csv", 1024, hash));

        Assert.Equal("sha256", error.ParamName);
    }

    [Fact]
    public void SourceIdentity_AcceptsEveryLowerCaseHexDigit()
    {
        var hash = string.Concat(Enumerable.Repeat("0123456789abcdef", 4));

        var identity = new SourceIdentity("prices.csv", 2048, hash);

        Assert.Equal(hash, identity.Sha256);
        Assert.Equal(2048, identity.ByteSize);
        Assert.Equal("prices.csv", identity.FileName);
    }

    // ------------------------------------------------------------- duplicates

    [Fact]
    public async Task Execute_NamesVolumeAsTheDifferingFieldWhenOnlyVolumeDiffers()
    {
        // Volume alone differing is the classic partial-bar artefact. The reader
        // has to choose which row to keep, so the field that differs is named.
        var candles = new List<PriceCandle>
        {
            Candle(Utc(10), 2, volume: 10m),
            Candle(Utc(10), 3, volume: 99m)
        };
        var source = new FakePreparedSource(candles, Succeeded(candles));

        var outcome = await CreateOrchestrator().ExecuteAsync(
            new DetailedValidationRequest(
                "test.csv",
                source,
                new ValidationOptions { TimeframeOverride = "H1" },
                new AlwaysOpenCalendar(),
                new CsvInputOptions()));

        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;

        var cursors = new List<IDetailedFindingCursor>();
        await foreach (var cursor in catalog.ReadCanonicalAsync())
        {
            cursors.Add(cursor);
        }

        var duplicate = Assert.Single(
            cursors,
            cursor => cursor.Header.Category == FindingCategory.DuplicateRecord);

        var evidence = new List<FindingEvidenceRecord>();
        await foreach (var record in duplicate.ReadEvidenceAsync())
        {
            evidence.Add(record);
        }

        var header = Assert.IsType<FindingEvidenceRecord.DuplicateHeader>(evidence[0]).Evidence;
        Assert.Equal(DuplicateClassification.Conflicting, header.Classification);
        Assert.Equal(["Volume"], header.DifferingFields);
    }

    // -------------------------------------------------------------- timeframe

    [Fact]
    public async Task Execute_RefusesToReportWhenNoTimeframeCanBeInferred()
    {
        // Every missing-candle claim depends on knowing how far apart candles are
        // meant to be. Irregular spacing makes that unknowable, so the run is
        // refused with a diagnostic naming the reason rather than reporting gaps
        // measured against a spacing that was guessed.
        var candles = new List<PriceCandle>
        {
            Candle(Utc(10, 0), 2),
            Candle(Utc(10, 7), 3),
            Candle(Utc(10, 23), 4),
            Candle(Utc(11, 2), 5)
        };
        var source = new FakePreparedSource(candles, Succeeded(candles));

        var outcome = await CreateOrchestrator().ExecuteAsync(
            new DetailedValidationRequest(
                "test.csv",
                source,
                new ValidationOptions(),
                new AlwaysOpenCalendar(),
                new CsvInputOptions()));

        var diagnostic = Assert.IsType<DetailedValidationOutcome.Failed>(outcome).Diagnostic;
        Assert.Equal("AMBIGUOUS_TIMEFRAME", diagnostic.Code);
        Assert.Equal(FailureClass.Configuration, diagnostic.FailureClass);
        Assert.Equal(FailureStage.TimeframeResolution, diagnostic.Stage);
        Assert.Contains("unique timeframe", diagnostic.Reason, StringComparison.OrdinalIgnoreCase);

        // A refused run states no findings, so every check reads as not completed
        // and nothing about the data can be mistaken for a verified result.
        Assert.False(diagnostic.FindingSetComplete);
        Assert.All(diagnostic.Checks, check => Assert.Equal(CheckStatus.NotCompleted, check.Status));
    }


    [Fact]
    public async Task Execute_RefusesToReportWhenTheTimeframeOverrideIsNotACode()
    {
        // An override that cannot be parsed is a caller mistake, not a data fault.
        // It is reported as an invalid argument so the caller fixes the command
        // rather than reading a report built on a timeframe nobody chose.
        var candles = new List<PriceCandle> { Candle(Utc(10), 2), Candle(Utc(11), 3) };
        var source = new FakePreparedSource(candles, Succeeded(candles));

        var outcome = await CreateOrchestrator().ExecuteAsync(
            new DetailedValidationRequest(
                "test.csv",
                source,
                new ValidationOptions { TimeframeOverride = "Q7" },
                new AlwaysOpenCalendar(),
                new CsvInputOptions()));

        var diagnostic = Assert.IsType<DetailedValidationOutcome.Failed>(outcome).Diagnostic;
        Assert.Equal("INVALID_ARGUMENT", diagnostic.Code);
        Assert.Equal(FailureClass.Configuration, diagnostic.FailureClass);
        Assert.Equal(FailureStage.ArgumentValidation, diagnostic.Stage);
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
            new ScanCoverage(candles.Count, candles.Count, 0));

    private sealed class FakeReplayableData : IReplayableCandleData
    {
        private readonly IReadOnlyList<PriceCandle> _candles;

        public FakeReplayableData(IReadOnlyList<PriceCandle> candles)
        {
            _candles = candles;
        }

        public IAsyncEnumerable<PriceCandle> ReplayAsync() => _candles.ToAsyncEnumerable();
    }

    private sealed class FakePreparedSource : IPreparedCandleSource
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

    private sealed class AlwaysOpenCalendar : IMarketCalendar
    {
        public MarketProfile Profile => MarketProfile.Crypto;

        public bool IsOpen(DateTimeOffset timestamp) => true;
    }
}
