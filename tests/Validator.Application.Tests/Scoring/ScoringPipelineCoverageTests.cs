using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Application.Validation;
using Validator.Domain.Calendars;
using Validator.Domain.Candles;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Xunit;
using InMemorySpool = Validator.Application.Tests.Reporting.InMemorySpool;
using InMemorySpoolReader = Validator.Application.Tests.Reporting.InMemorySpoolReader;
using InMemorySpoolStore = Validator.Application.Tests.Reporting.InMemorySpoolStore;

namespace Validator.Application.Tests.Scoring;

// Scoring reaches a report only through a real run, so these tests drive the
// orchestrator itself rather than the builder in isolation. They prove the things
// that decide whether a published score can be trusted: a score is attached only
// when it was asked for, it is derived from the populations the run itself
// established, and an impossible defect rate fails the run instead of being
// clamped into a publishable number.
public sealed class ScoringPipelineCoverageTests
{
    private readonly InMemorySpoolStore _store = new();

    private static readonly string Sha256 = new('a', 64);

    private static DateTimeOffset Utc(int hour) => new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    private static PriceCandle Candle(DateTimeOffset timestamp, long line, decimal volume = 10m) =>
        new(timestamp, 10m, 11m, 9m, 10m, volume, line);

    private FindingCatalog RealCatalog() => new(
        () => new InMemorySpool(_store),
        path => new InMemorySpoolReader(_store.Spools[path]));

    private static DetailedValidationRequest Request(
        IPreparedCandleSource source,
        ScoreRequest? score) =>
        new(
            "test.csv",
            source,
            new ValidationOptions { TimeframeOverride = "H1", Score = score },
            new AlwaysOpenCalendar(),
            new CsvInputOptions());

    // ------------------------------------------------------- scoring opt-in

    [Fact]
    public async Task Execute_WithoutAScoreRequest_PublishesAReportCarryingNoScore()
    {
        // Scoring is opt-in. A run that was not asked to score must behave exactly
        // as before and must not attach a score a caller never requested.
        var candles = new List<PriceCandle> { Candle(Utc(10), 2), Candle(Utc(11), 3) };
        var source = new FakePreparedSource(candles, Succeeded(candles, accepted: 2, examined: 2));

        var outcome = await new DetailedValidationOrchestrator(RealCatalog)
            .ExecuteAsync(Request(source, score: null));

        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;

        Assert.Null(report.Score);
    }

    [Fact]
    public async Task Execute_WithAScoreRequest_ScoresEveryMetricOverTheRunsOwnPopulations()
    {
        // A flawless two-candle run scores 100.00 on all six metrics. The
        // populations are the ones the run itself established, so the score is a
        // pure derivation of the report rather than a second measurement.
        var candles = new List<PriceCandle> { Candle(Utc(10), 2), Candle(Utc(11), 3) };
        var source = new FakePreparedSource(candles, Succeeded(candles, accepted: 2, examined: 2));

        var outcome = await new DetailedValidationOrchestrator(RealCatalog)
            .ExecuteAsync(Request(source, ScoreRequest.Default()));

        var report = Assert.IsType<DetailedValidationOutcome.Succeeded>(outcome).Report;
        await using var catalog = report.Findings;

        var score = Assert.IsType<DatasetScoreReport>(report.Score);
        Assert.Equal(6, score.Metrics.Count);
        Assert.All(score.Metrics, metric => Assert.Equal(MetricScoreState.Scored, metric.State));
        Assert.Equal("100.00", score.Dataset.Average!.Value.Format());
        Assert.Equal(ScoreWeightingSource.Default, score.Weighting.Source);

        // The scale travels with the report, so a reader never has to assume
        // which direction the number runs in or how many decimals it carries.
        Assert.Equal(0, score.Scale.Minimum);
        Assert.Equal(100, score.Scale.Maximum);
        Assert.True(score.Scale.HigherIsBetter);

        // The expected-candle population comes from the sequence walk that
        // reported the missing candles, so the two can never disagree.
        var missing = score.Metrics.Single(metric => metric.Category == FindingCategory.MissingCandle);
        Assert.Equal(MetricPopulationKind.ExpectedCandles, missing.PopulationKind);
        Assert.Equal(2, missing.Population);
    }

    // --------------------------------------------------- impossible defect rate

    [Fact]
    public async Task Execute_WhenAMetricCountExceedsItsPopulation_FailsInsteadOfClampingTheScore()
    {
        // Four rows share one timestamp, so three duplicates are reported, while
        // the coverage accounts for only two accepted rows. A defect rate above 1
        // is an internal inconsistency: the run must fail as a reconciliation
        // failure rather than clamp the metric to zero and publish a score.
        var candles = new List<PriceCandle>
        {
            Candle(Utc(10), 2),
            Candle(Utc(10), 3, volume: 11m),
            Candle(Utc(10), 4, volume: 12m),
            Candle(Utc(10), 5, volume: 13m)
        };
        var source = new FakePreparedSource(candles, Succeeded(candles, accepted: 2, examined: 2));

        var outcome = await new DetailedValidationOrchestrator(RealCatalog)
            .ExecuteAsync(Request(source, ScoreRequest.Default()));

        var diagnostic = Assert.IsType<DetailedValidationOutcome.Failed>(outcome).Diagnostic;
        Assert.Equal("REPORT_RECONCILIATION_FAILED", diagnostic.Code);
        Assert.Contains("exceeds its population", diagnostic.Guidance, StringComparison.Ordinal);

        // The checks are carried on the diagnostic so a reader can still see how
        // far the run got before it was refused.
        Assert.Equal(6, diagnostic.Checks.Count);
    }

    // ----------------------------------------------------------------- helpers

    private static PreparedCandleDataResult Succeeded(
        IReadOnlyList<PriceCandle> candles,
        long accepted,
        long examined) =>
        new PreparedCandleDataResult.Succeeded(
            new FakeReplayableData(candles),
            new SourceIdentity("test.csv", 1024, Sha256),
            new ResolvedCsvContext(
                ',',
                false,
                TimestampInterpretation.CreateCombined("yyyy-MM-dd HH:mm:ss", "timestamp", "+00:00"),
                null),
            new ScanCoverage(examined, accepted, examined - accepted));

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
