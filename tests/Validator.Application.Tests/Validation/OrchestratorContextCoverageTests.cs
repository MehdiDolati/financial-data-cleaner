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
using Xunit;
using InMemorySpool = Validator.Application.Tests.Reporting.InMemorySpool;
using InMemorySpoolReader = Validator.Application.Tests.Reporting.InMemorySpoolReader;
using InMemorySpoolStore = Validator.Application.Tests.Reporting.InMemorySpoolStore;

namespace Validator.Application.Tests.Validation;

// What a report states about the run itself, and the check outcomes that only
// appear for particular shapes of source data: the delimiter that was actually
// resolved, an exactly duplicated row, every OHLC invariant, a gap that runs to
// the end of the data, and a reference that repeats. Each is a fact a reader
// relies on, so each is produced by a run rather than asserted in isolation.
public sealed class OrchestratorContextCoverageTests
{
    private readonly InMemorySpoolStore _store = new();

    private static readonly string Sha256 = new('a', 64);

    private DetailedValidationOrchestrator CreateOrchestrator() => new(
        () => new FindingCatalog(
            () => new InMemorySpool(_store),
            path => new InMemorySpoolReader(_store.Spools[path])));

    private static DateTimeOffset Ts(int hour) => new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    private static PriceCandle Candle(
        DateTimeOffset timestamp,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long line,
        decimal volume = 10m) =>
        new(timestamp, open, high, low, close, volume, line);

    private static DetailedValidationRequest Request(
        IPreparedCandleSource source,
        IMarketCalendar calendar) =>
        new(
            "test.csv",
            source,
            new ValidationOptions { TimeframeOverride = "H1" },
            calendar,
            new CsvInputOptions());

    private static PreparedCandleDataResult Succeeded(
        IReadOnlyList<PriceCandle> candles,
        char delimiter = ',',
        bool hasHeader = false,
        DateRange? dateRange = null) =>

        new PreparedCandleDataResult.Succeeded(
            new FakeReplayableData(candles),
            new SourceIdentity("test.csv", 1024, Sha256),
            new ResolvedCsvContext(
                delimiter,
                hasHeader,
                TimestampInterpretation.CreateCombined("yyyy-MM-dd HH:mm:ss", "timestamp", "+02:00"),
                dateRange),
            new ScanCoverage(candles.Count, candles.Count, 0));

    [Theory]
    [InlineData(',', "comma")]
    [InlineData(';', "semicolon")]
    [InlineData('\t', "tab")]
    public async Task Execute_ReportsTheDelimiterThatWasActuallyResolved(char delimiter, string expected)
    {
        // A reader reproducing the run needs the delimiter the parse used, so the
        // report names it rather than assuming the most common one.
        var candles = new List<PriceCandle>
        {
            Candle(Ts(10), 10m, 11m, 9m, 10m, 2),
            Candle(Ts(11), 11m, 12m, 10m, 11m, 3)
        };
        var source = new FakePreparedSource(candles, Succeeded(candles, delimiter));

        var outcome = await CreateOrchestrator().ExecuteAsync(Request(source, new AlwaysOpenCalendar()));

        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;
        Assert.Equal(expected, report.Context.Delimiter);
    }

    [Fact]
    public async Task Execute_RefusesToNameADelimiterItCannotDescribe()
    {
        // A delimiter outside the supported set has no published name. Inventing
        // one would misdescribe how the source was read.
        var candles = new List<PriceCandle> { Candle(Ts(10), 10m, 11m, 9m, 10m, 2) };
        var source = new FakePreparedSource(candles, Succeeded(candles, '|'));

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await CreateOrchestrator().ExecuteAsync(Request(source, new AlwaysOpenCalendar())));
    }

    [Fact]
    public async Task Execute_CarriesTheHeaderAndDateRangeTheSourceEstablished()
    {
        var candles = new List<PriceCandle>
        {
            Candle(Ts(10), 10m, 11m, 9m, 10m, 2),
            Candle(Ts(11), 11m, 12m, 10m, 11m, 3)
        };
        var range = new DateRange(Ts(10), Ts(11));
        var source = new FakePreparedSource(
            candles,
            Succeeded(candles, ',', hasHeader: true, dateRange: range));

        var outcome = await CreateOrchestrator().ExecuteAsync(Request(source, new AlwaysOpenCalendar()));

        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;
        Assert.True(report.Context.HasHeader);
        Assert.Equal(range, report.Context.DateRange);

    }

    [Fact]
    public async Task Execute_ClassifiesAnExactlyRepeatedRowAsExactWithNoDifferingFields()
    {
        // Two identical rows are a transcription artefact, not a conflict. Saying
        // fields differ when none do would send a reader looking for a decision
        // they do not have to make.
        var candles = new List<PriceCandle>
        {
            Candle(Ts(10), 10m, 11m, 9m, 10m, 2),
            Candle(Ts(10), 10m, 11m, 9m, 10m, 3)
        };
        var source = new FakePreparedSource(candles, Succeeded(candles));

        var outcome = await CreateOrchestrator().ExecuteAsync(Request(source, new AlwaysOpenCalendar()));

        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;
        var cursor = Assert.Single(await ReadCursorsAsync(catalog));
        Assert.Equal(FindingCategory.DuplicateRecord, cursor.Header.Category);
        Assert.Equal("Exact duplicate record", cursor.Header.Title);
        Assert.Equal(1, cursor.Header.CountContribution);

        var evidence = await ReadEvidenceAsync(cursor);
        var header = Assert.IsType<FindingEvidenceRecord.DuplicateHeader>(evidence[0]).Evidence;
        Assert.Equal(DuplicateClassification.Exact, header.Classification);
        Assert.Empty(header.DifferingFields);
        Assert.DoesNotContain(evidence, record => record is FindingEvidenceRecord.DuplicateDifferingField);
    }

    [Fact]
    public async Task Execute_NamesEveryOhlcInvariantASingleRowViolates()
    {
        // Each violation is a separate reason the row cannot be trusted, and a
        // reader repairing the data needs all of them, not just the first.
        var candles = new List<PriceCandle>
        {
            Candle(Ts(10), 0m, -1m, 5m, 0m, 2, volume: -3m),
            Candle(Ts(11), 10m, 11m, 9m, 10m, 3)
        };
        var source = new FakePreparedSource(candles, Succeeded(candles));

        var outcome = await CreateOrchestrator().ExecuteAsync(Request(source, new AlwaysOpenCalendar()));

        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;
        var cursor = Assert.Single(
            await ReadCursorsAsync(catalog),
            candidate => candidate.Header.Category == FindingCategory.InvalidOhlc);

        var codes = (await ReadEvidenceAsync(cursor))
            .OfType<FindingEvidenceRecord.InvalidOhlcViolation>()
            .Select(record => record.Code)
            .ToArray();

        Assert.Equal(
            new[]
            {
                OhlcViolationCode.HIGH_BELOW_OPEN,
                OhlcViolationCode.HIGH_BELOW_CLOSE,
                OhlcViolationCode.HIGH_BELOW_LOW,
                OhlcViolationCode.LOW_ABOVE_OPEN,
                OhlcViolationCode.LOW_ABOVE_CLOSE,
                OhlcViolationCode.NON_POSITIVE_OPEN,
                OhlcViolationCode.NON_POSITIVE_HIGH,
                OhlcViolationCode.NON_POSITIVE_CLOSE,
                OhlcViolationCode.NEGATIVE_VOLUME
            },
            codes);
    }

    [Fact]
    public async Task Execute_KeepsBothFindingsWhenTwoWouldShareOneReference()
    {
        // Two closed-market rows on the same physical line would derive the same
        // reference. Collapsing them would drop a finding the summary counts, so
        // the second is distinguished instead.
        var candles = new List<PriceCandle>
        {
            Candle(Ts(10), 10m, 11m, 9m, 10m, 5),
            Candle(Ts(11), 11m, 12m, 10m, 11m, 5)
        };
        var source = new FakePreparedSource(candles, Succeeded(candles));

        var outcome = await CreateOrchestrator().ExecuteAsync(Request(source, new NeverOpenCalendar()));

        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;
        var references = (await ReadCursorsAsync(catalog))
            .Where(cursor => cursor.Header.Category == FindingCategory.ClosedMarketRecord)
            .Select(cursor => cursor.Header.Reference.Value)
            .ToArray();

        Assert.Equal(
            new[] { "closed-market-record:line-5", "closed-market-record:line-5:2" },
            references);
        Assert.Equal(2, report.Summary.ClosedMarketRecords);
    }

    [Fact]
    public async Task Execute_DoesNotCountAClosedSlotAsAMissingCandle()
    {
        // A slot the market was closed for is not a missing candle: nothing was
        // expected there. The gap therefore spans only the open slot, while the
        // row that was present during the closure is reported on its own terms,
        // and the gap still cites the observations either side of the closure.

        var candles = new List<PriceCandle>
        {
            Candle(Ts(9), 10m, 11m, 9m, 10m, 2),
            Candle(Ts(10), 10m, 11m, 9m, 10m, 3),
            Candle(Ts(12), 11m, 12m, 10m, 11m, 4)
        };
        var source = new FakePreparedSource(candles, Succeeded(candles));

        var outcome = await CreateOrchestrator().ExecuteAsync(Request(source, new ClosedAtHourCalendar(10)));

        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;
        var cursors = await ReadCursorsAsync(catalog);
        var gap = Assert.Single(cursors, cursor => cursor.Header.Category == FindingCategory.TimeGap);

        var gapEvidence = Assert.IsType<FindingEvidenceRecord.TimeGapHeader>(
            Assert.Single(await ReadEvidenceAsync(gap), record => record is FindingEvidenceRecord.TimeGapHeader))
            .Evidence;

        Assert.Equal(Ts(9), gapEvidence.PreviousObservedTimestampUtc);
        Assert.Equal(Ts(12), gapEvidence.NextObservedTimestampUtc);
        Assert.Equal(1, gapEvidence.MissingCandleCount);
        Assert.Equal(1, report.Summary.MissingCandles);
        Assert.Equal(1, report.Summary.ClosedMarketRecords);

        // The closed slot itself is never published as an expected-but-absent
        // candle, so no missing-candle finding cites that timestamp.
        var missing = cursors.Where(cursor => cursor.Header.Category == FindingCategory.MissingCandle);
        Assert.All(missing, cursor => Assert.Equal(Ts(11), cursor.Header.Location.TimestampUtc));


    }

    private static async Task<List<IDetailedFindingCursor>> ReadCursorsAsync(ICompletedFindingCatalog catalog)
    {
        var cursors = new List<IDetailedFindingCursor>();
        await foreach (var cursor in catalog.ReadCanonicalAsync())
        {
            cursors.Add(cursor);
        }

        return cursors;
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

    private sealed class NeverOpenCalendar : IMarketCalendar
    {
        public MarketProfile Profile => MarketProfile.Equities;

        public bool IsOpen(DateTimeOffset timestamp) => false;
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
