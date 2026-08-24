using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Benchmark;
using Validator.Application.Comparison;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Candles;
using Validator.Domain.Comparison;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;
using Validator.Domain.Timeframes;
using Xunit;

namespace Validator.Application.Tests.Comparison
{
    // Exercises guard arms, null paths, and boundary branches in the comparison
    // layer to reach 100% line and branch coverage over reachable code.
    public sealed class ComparisonGuardTests
    {
        private static readonly string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

        // --- CandidateIdentity null guards ---

        [Fact]
        public void CandidateIdentity_NullSource_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CandidateIdentity(null!, CreateContext(), "AUDUSD"));
        }

        [Fact]
        public void CandidateIdentity_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CandidateIdentity(new SourceIdentity("test.csv", 1, Sha256), null!, "AUDUSD"));
        }

        [Fact]
        public void CandidateIdentity_InvalidInstrument_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new CandidateIdentity(new SourceIdentity("test.csv", 1, Sha256), CreateContext(), ""));
            Assert.Throws<ArgumentException>(() =>
                new CandidateIdentity(new SourceIdentity("test.csv", 1, Sha256), CreateContext(), "AUD/USD"));
        }

        // --- ToleranceResolver.ParseOverrides (exercises ParseOhlcvField internally) ---

        [Fact]
        public void ParseOverrides_RejectsAnUnknownFieldName()
        {
            var json = "{\"InvalidField\": {\"absolute\": 0.001}}";
            Assert.Throws<ArgumentException>(() => ToleranceResolver.ParseOverrides(json));
        }

        [Fact]
        public void ParseOverrides_ParsesAllValidFieldNames()
        {
            // Exercises every arm of ParseOhlcvField (open, high, low, close, volume)
            var json = "{" +
                '\"' + "Open" + '\"' + ": {\"absolute\": 0.001}, " +
                '\"' + "High" + '\"' + ": {\"absolute\": 0.002}, " +
                '\"' + "Low" + '\"' + ": {\"absolute\": 0.003}, " +
                '\"' + "Close" + '\"' + ": {\"absolute\": 0.004}, " +
                '\"' + "Volume" + '\"' + ": {\"relative\": 0.05}" +
                "}";

            var result = ToleranceResolver.ParseOverrides(json);
            Assert.Equal(5, result.Count);
        }

        [Fact]
        public void ParseOverrides_RejectsInvalidRelativeTolerance()
        {
            var json = "{\"Open\": {\"relative\": \"not-a-number\"}}";
            Assert.Throws<ArgumentException>(() => ToleranceResolver.ParseOverrides(json));
        }

        [Fact]
        public void ParseOverrides_RejectsInvalidAbsoluteTolerance()
        {
            var json = "{\"Open\": {\"absolute\": \"not-a-number\"}}";
            Assert.Throws<ArgumentException>(() => ToleranceResolver.ParseOverrides(json));
        }

        // --- CompareDatasetsUseCase.Compare (exercises isDifferent paths and TryGetValue) ---

        [Fact]
        public void Compare_WithMatchingTimestampsAndDifferentValues_ReportsDiscrepancies()
        {
            var ts = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var benchmarkCandles = new[]
            {
                new PriceCandle(ts, 1.0m, 1.1m, 0.9m, 1.05m, 1000, 1)
            };
            var candidateCandles = new[]
            {
                new PriceCandle(ts, 1.0m, 1.1m, 0.9m, 1.05m, 2000, 2) // volume differs
            };

            var benchmark = CreateBenchmarkWithCandles(benchmarkCandles);
            var candidate = CreateCandidate();
            var useCase = new CompareDatasetsUseCase();

            // Should succeed and report the volume discrepancy
            var report = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, candidate);
            Assert.NotNull(report);
            // Volume difference should be a material discrepancy since default volume tolerance is 5% relative
            // and 2000 vs 1000 is 100% difference, well above 5%
            Assert.NotEmpty(report.MaterialDiscrepancies);
        }

        [Fact]
        public void Compare_WithSmallDifferenceWithinAbsoluteTolerance_AcceptsByAbsolute()
        {
            var ts = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            // Use integer-valued candle so maxPrecision=0, InferFractionalStep returns 1m,
            // and the default absolute tolerance of 0.0001 is used.
            var benchmarkCandles = new[]
            {
                new PriceCandle(ts, 100m, 101m, 99m, 100.5m, 1000, 1)
            };
            // Candidate differs by 0.00005 in Open — within the 0.0001 absolute tolerance
            var candidateCandles = new[]
            {
                new PriceCandle(ts, 100.00005m, 101m, 99m, 100.5m, 1000, 2)
            };

            var benchmark = CreateBenchmarkWithCandles(benchmarkCandles);
            var candidate = CreateCandidate();
            var useCase = new CompareDatasetsUseCase();

            var report = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, candidate);
            Assert.NotNull(report);
            Assert.Empty(report.MaterialDiscrepancies);
        }

        [Fact]
        public void Compare_WithMissingAndExtraTimestamps_ReportsCoverage()
        {
            var ts1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var ts2 = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
            var ts3 = new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero);

            // Benchmark has ts1, ts2, ts3; candidate has ts1, ts3 (missing ts2)
            var benchmarkCandles = new[]
            {
                new PriceCandle(ts1, 1.0m, 1.1m, 0.9m, 1.05m, 1000, 1),
                new PriceCandle(ts2, 1.0m, 1.1m, 0.9m, 1.05m, 1000, 2),
                new PriceCandle(ts3, 1.0m, 1.1m, 0.9m, 1.05m, 1000, 3)
            };
            var candidateCandles = new[]
            {
                new PriceCandle(ts1, 1.0m, 1.1m, 0.9m, 1.05m, 1000, 4),
                new PriceCandle(ts3, 1.0m, 1.1m, 0.9m, 1.05m, 1000, 5)
            };

            var benchmark = CreateBenchmarkWithCandles(benchmarkCandles);
            var candidate = CreateCandidate();
            var useCase = new CompareDatasetsUseCase();

            var report = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, candidate);
            Assert.Single(report.MissingFromCandidateTimestamps);
            Assert.Empty(report.ExtraInCandidateTimestamps);
        }

        // --- ComparisonReport null-coalescing branches ---

        [Fact]
        public void ComparisonReport_NullTimestamps_DefaultsToEmpty()
        {
            var config = ToleranceResolver.Resolve(null, "test");
            var report = new ComparisonReport(
                CreateBenchmarkWithCandles([]),
                CreateCandidate(),
                config,
                new ComparisonCoverage(0, 0, 0, 0, 0, null, null),
                [],
                [],
                missingFromCandidateTimestamps: null!,
                extraInCandidateTimestamps: null!,
                candidateScore: null,
                agreementScore: BenchmarkAgreementScore.Unavailable("no data"));

            Assert.Empty(report.MissingFromCandidateTimestamps);
            Assert.Empty(report.ExtraInCandidateTimestamps);
        }

        // --- Helpers ---

        private static ValidationContextSnapshot CreateContext(string timeframe = "M1") =>
            new(timeframe, new CalendarContext("forex", "Forex"),
                TimestampInterpretation.CreateSeparate("yyyy-MM-dd", "HH:mm:ss", "+00:00"),
                "comma", hasHeader: true, dateRange: null);

        private static TimestampInterpretation CreateTimestamp() =>
            TimestampInterpretation.CreateSeparate("yyyy-MM-dd", "HH:mm:ss", "+00:00");

        private static BenchmarkSnapshot CreateBenchmark(
            string timeframe = "M1",
            string instrument = "AUDUSD")
        {
            return CreateBenchmarkWithCandles([], timeframe, instrument);
        }

        private static BenchmarkSnapshot CreateBenchmarkWithCandles(
            IReadOnlyList<PriceCandle> candles,
            string timeframe = "M1",
            string instrument = "AUDUSD")
        {
            var allExcluded = MetricPopulationMap.CanonicalOrder
                .Select(c => new ExcludedMetric(c, MetricScoreState.NotScored, "test"))
                .ToList();

            return new BenchmarkSnapshot(
                name: "test-benchmark",
                establishedAtUtc: DateTimeOffset.UtcNow,
                source: new SourceIdentity("test.csv", 100, Sha256),
                context: CreateContext(timeframe),
                coverage: new ScanCoverage(0, 0, 0),
                checks: [],
                metrics: [],
                dataset: DatasetScore.Unavailable("test", [], allExcluded),
                weighting: new ScoreWeighting(
                    ScoreWeightingSource.Default,
                    MetricPopulationMap.CanonicalOrder
                        .Select(c => new MetricWeight(c, 1m))
                        .ToList()),
                instrument: instrument);
        }

        private static CandidateIdentity CreateCandidate(
            string timeframe = "M1",
            string instrument = "AUDUSD")
        {
            return new CandidateIdentity(
                source: new SourceIdentity("test.csv", 100, Sha256),
                context: CreateContext(timeframe),
                instrument: instrument);
        }
    }
}
