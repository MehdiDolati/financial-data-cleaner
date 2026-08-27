using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Benchmark;
using Validator.Application.Comparison;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Candles;
using Validator.Domain.Comparison;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;
using Xunit;

namespace Validator.Application.Tests.Comparison
{
    public class CompareDatasetsUseCaseTests
    {
        [Fact]
        public void Compare_IdenticalData_NoDiscrepanciesPerfectScore()
        {
            var benchmark = CreateBenchmark("test");
            var candles = CreateCandleSet();
            var identity = CreateCandidateIdentity();

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(benchmark, candles, candles, identity);

            Assert.Empty(report.MaterialDiscrepancies);
            Assert.Equal(100m, report.AgreementScore.Score!.Value.Rounded);
            Assert.Equal(5, report.Coverage.MatchedCount);
            Assert.Equal(0, report.Coverage.MissingFromCandidateCount);
            Assert.Equal(0, report.Coverage.ExtraInCandidateCount);
        }

        [Fact]
        public void Compare_MaterialPriceDifference_Detected()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Modify one price to be a material difference
            candidateCandles[2] = candidateCandles[2] with { Open = 0.63720m + 0.00050m }; // +0.00050 exceeds tolerance

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(
                benchmark, benchmarkCandles, candidateCandles, CreateCandidateIdentity());

            Assert.Single(report.MaterialDiscrepancies);
            Assert.Equal(OhlcvField.Open, report.MaterialDiscrepancies[0].Field);
            Assert.IsType<ToleranceDecision.MaterialDifference>(report.MaterialDiscrepancies[0].ToleranceDecision);
        }

        [Fact]
        public void Compare_ToleratedBrokerDifference_Accepted()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Modify one price by a small broker-level difference (within tolerance)
            candidateCandles[0] = candidateCandles[0] with { Open = 0.63421m + 0.00005m }; // +0.00005 within 0.00010 absolute

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(
                benchmark, benchmarkCandles, candidateCandles, CreateCandidateIdentity());

            Assert.Empty(report.MaterialDiscrepancies);
            // The tolerated summary should show the accepted difference
            var openSummary = report.ToleratedSummary.First(s => s.Field == OhlcvField.Open);
            Assert.Equal(5, openSummary.TotalCompared);
            Assert.Equal(1, openSummary.AcceptedCount);
        }

        [Fact]
        public void Compare_IdenticalValues_AreNotCountedAsToleratedDifferences()
        {
            var benchmark = CreateBenchmark("test");
            var candles = CreateCandleSet();

            var report = new CompareDatasetsUseCase(new FixedClock())
                .Compare(benchmark, candles, candles, CreateCandidateIdentity());

            Assert.All(report.ToleratedSummary, summary => Assert.Equal(0, summary.AcceptedCount));
        }

        [Fact]
        public void Compare_MissingCandle_ReportedInCoverage()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Remove one candle from candidate
            candidateCandles.RemoveAt(2);

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(
                benchmark, benchmarkCandles, candidateCandles, CreateCandidateIdentity());

            Assert.Empty(report.MaterialDiscrepancies);
            Assert.Equal(1, report.Coverage.MissingFromCandidateCount);
            Assert.Equal(4, report.Coverage.MatchedCount);
            Assert.Equal(5, report.Coverage.BenchmarkRecordCount);
            Assert.Equal(4, report.Coverage.CandidateRecordCount);
        }

        [Fact]
        public void Compare_ExtraCandle_ReportedInCoverage()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Add one extra candle to candidate
            candidateCandles.Add(new PriceCandle(
                new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
                0.64380m, 0.64500m, 0.64320m, 0.64450m, 115000m, sourceLine: 6));

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(
                benchmark, benchmarkCandles, candidateCandles, CreateCandidateIdentity());

            Assert.Empty(report.MaterialDiscrepancies);
            Assert.Equal(1, report.Coverage.ExtraInCandidateCount);
            Assert.Equal(5, report.Coverage.MatchedCount);
            Assert.Equal(6, report.Coverage.CandidateRecordCount);
            var extra = Assert.Single(report.ExtraInCandidateRecords);
            Assert.Equal(6, extra.CandidateSourceLine);
        }

        [Fact]
        public void Compare_NoOverlap_UnavailableScore()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();

            // Candidate with completely different timestamps
            var candidateCandles = new List<PriceCandle>
            {
                new(new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero),
                    1.10000m, 1.10500m, 1.09500m, 1.10200m, 100000m),
                new(new DateTimeOffset(2020, 1, 3, 0, 0, 0, TimeSpan.Zero),
                    1.10200m, 1.10800m, 1.10000m, 1.10600m, 95000m),
            };

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(
                benchmark, benchmarkCandles, candidateCandles, CreateCandidateIdentity());

            Assert.Empty(report.MaterialDiscrepancies);
            Assert.Equal(0, report.Coverage.MatchedCount);
            Assert.Null(report.AgreementScore.Score);
            Assert.NotNull(report.AgreementScore.UnavailableReason);
        }

        [Fact]
        public void Compare_TimeframeMismatch_Throws()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Candidate with different timeframe
            var candidateIdentity = new CandidateIdentity(
                new SourceIdentity("candidate.csv", 100, Sha256()),
                new ValidationContextSnapshot(
                    "H1", // Different from benchmark's D1
                    new CalendarContext("forex", "Forex"),
                    TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                    "comma", false, null));

            var useCase = new CompareDatasetsUseCase();
            Assert.Throws<InvalidOperationException>(
                () => useCase.Compare(benchmark, benchmarkCandles, candidateCandles, candidateIdentity));
        }

        [Fact]
        public void Compare_DiscrepanciesOrderedCorrectly()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Create multiple material discrepancies across different timestamps and fields
            candidateCandles[0] = candidateCandles[0] with { Open = 0.63421m + 0.00050m }; // timestamp 1, Open
            candidateCandles[2] = candidateCandles[2] with { Close = 0.63720m - 0.00050m }; // timestamp 3, Close
            candidateCandles[1] = candidateCandles[1] with { High = 0.63650m + 0.00050m }; // timestamp 2, High

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(
                benchmark, benchmarkCandles, candidateCandles, CreateCandidateIdentity());

            Assert.Equal(3, report.MaterialDiscrepancies.Count);

            // Should be sorted by timestamp ascending
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                report.MaterialDiscrepancies[0].TimestampUtc);
            Assert.Equal(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
                report.MaterialDiscrepancies[1].TimestampUtc);
            Assert.Equal(new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero),
                report.MaterialDiscrepancies[2].TimestampUtc);
        }

        [Fact]
        public void Compare_ToleratedSummary_CorrectCounts()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Add one material difference to Open
            candidateCandles[0] = candidateCandles[0] with { Open = 0.63421m + 0.00050m };

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(
                benchmark, benchmarkCandles, candidateCandles, CreateCandidateIdentity());

            var openSummary = report.ToleratedSummary.First(s => s.Field == OhlcvField.Open);
            Assert.Equal(5, openSummary.TotalCompared);
            Assert.Equal(0, openSummary.AcceptedCount);
            Assert.Equal(1, openSummary.MaterialCount);
        }

        [Fact]
        public void Compare_AgreementScore_CorrectCalculation()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Add material differences to two different timestamps
            candidateCandles[0] = candidateCandles[0] with { Open = 0.63421m + 0.00050m };
            candidateCandles[2] = candidateCandles[2] with { High = 0.63780m + 0.00050m };

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(
                benchmark, benchmarkCandles, candidateCandles, CreateCandidateIdentity());

            // 5 matched timestamps, 2 with material discrepancies
            // Score = 100 * (5 - 2) / 5 = 60.00
            Assert.Equal(60m, report.AgreementScore.Score!.Value.Rounded);
            Assert.Equal(5, report.AgreementScore.MatchedPopulation);
            Assert.Equal(2, report.AgreementScore.MaterialDiscrepancyCount);
        }

        [Fact]
        public void Compare_NullBenchmark_Throws()
        {
            var useCase = new CompareDatasetsUseCase();
            Assert.Throws<ArgumentNullException>(
                () => useCase.Compare(null!, new List<PriceCandle>(), new List<PriceCandle>(), CreateCandidateIdentity()));
        }

        [Fact]
        public void Compare_CandidateScore_NotSetByDefault()
        {
            var benchmark = CreateBenchmark("test");
            var candles = CreateCandleSet();

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(benchmark, candles, candles, CreateCandidateIdentity());

            // CandidateScore is null by default (caller sets it if --score is used)
            Assert.Null(report.CandidateScore);
        }

        [Fact]
        public void Compare_VolumeWithinRelative_AcceptsByRelative()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Volume difference within 5% relative tolerance
            // 125000 vs 120000 = 4% difference, within 5% relative
            candidateCandles[0] = candidateCandles[0] with { Volume = 120000m };

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(
                benchmark, benchmarkCandles, candidateCandles, CreateCandidateIdentity());

            Assert.Empty(report.MaterialDiscrepancies);
            var volumeSummary = report.ToleratedSummary.First(s => s.Field == OhlcvField.Volume);
            Assert.Equal(1, volumeSummary.AcceptedCount);
            Assert.Equal(1, volumeSummary.AcceptedByRelativeCount);
        }

        [Fact]
        public void Compare_VolumeExceedsRelative_MaterialDifference()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Volume difference exceeds 5% relative tolerance
            // 125000 vs 118200 = 5.44% difference, exceeds 5%
            candidateCandles[0] = candidateCandles[0] with { Volume = 118200m };

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(
                benchmark, benchmarkCandles, candidateCandles, CreateCandidateIdentity());

            Assert.Single(report.MaterialDiscrepancies);
            Assert.Equal(OhlcvField.Volume, report.MaterialDiscrepancies[0].Field);
        }

        [Fact]
        public void Compare_DisabledField_SkippedInComparison()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Disable Open field
            var overrides = new[]
            {
                new ComparedField(OhlcvField.Open, false, null, null, 0, 0)
            };

            // Modify Open to have material difference - should be ignored
            candidateCandles[0] = candidateCandles[0] with { Open = 0.63421m + 0.00050m };

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(
                benchmark, benchmarkCandles, candidateCandles, CreateCandidateIdentity(), overrides);

            Assert.Empty(report.MaterialDiscrepancies);
        }

        [Fact]
        public void Compare_DifferentCalendarProfile_ProducesContextWarning()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            var candidateIdentity = new CandidateIdentity(
                new SourceIdentity("candidate.csv", 100, Sha256()),
                new ValidationContextSnapshot(
                    "D1",
                    new CalendarContext("equities", "Equities"), // Different calendar
                    TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                    "comma", false, null));

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, candidateIdentity);

            Assert.Contains(report.ContextWarnings, w => w.Contains("Calendar profile differs"));
        }

        [Fact]
        public void Compare_DifferentTimezone_ProducesContextWarning()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            var candidateIdentity = new CandidateIdentity(
                new SourceIdentity("candidate.csv", 100, Sha256()),
                new ValidationContextSnapshot(
                    "D1",
                    new CalendarContext("forex", "Forex"),
                    TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+00:00"), // Different offset
                    "comma", false, null));

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, candidateIdentity);

            Assert.Contains(report.ContextWarnings, w => w.Contains("Source timestamp offset differs"));
        }

        [Fact]
        public void Compare_DifferentTimestampMode_ProducesContextWarning()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            var candidateIdentity = new CandidateIdentity(
                new SourceIdentity("candidate.csv", 100, Sha256()),
                new ValidationContextSnapshot(
                    "D1",
                    new CalendarContext("forex", "Forex"),
                    TimestampInterpretation.CreateCombined("yyyy.MM.dd HH:mm", "Timestamp", "+02:00"), // Combined mode
                    "comma", false, null));

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, candidateIdentity);

            Assert.Contains(report.ContextWarnings, w => w.Contains("Timestamp interpretation differs"));
        }

        [Fact]
        public void Compare_DifferentDateRange_ProducesContextWarning()
        {
            // Build benchmark with a specific DateRange
            var benchmarkContext = new ValidationContextSnapshot(
                "D1",
                new CalendarContext("forex", "Forex"),
                TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                "comma", false,
                new DateRange(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 5, 14, 0, 0, 0, TimeSpan.Zero)));
            var benchmark = CreateBenchmarkWithContext("test", benchmarkContext);
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Candidate with different date range
            var candidateIdentity = new CandidateIdentity(
                new SourceIdentity("candidate.csv", 100, Sha256()),
                new ValidationContextSnapshot(
                    "D1",
                    new CalendarContext("forex", "Forex"),
                    TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                    "comma", false,
                    new DateRange(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero))));

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, candidateIdentity);

            Assert.Contains(report.ContextWarnings, w => w.Contains("Date range differs"));
        }

        [Fact]
        public void Compare_DifferentHeaderMode_ProducesContextWarning()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            var candidateIdentity = new CandidateIdentity(
                new SourceIdentity("candidate.csv", 100, Sha256()),
                new ValidationContextSnapshot(
                    "D1",
                    new CalendarContext("forex", "Forex"),
                    TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                    "comma", true, null)); // hasHeader = true vs benchmark's false

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, candidateIdentity);

            Assert.Contains(report.ContextWarnings, w => w.Contains("Header mode differs"));
        }

        [Fact]
        public void Compare_DifferentHeaderMode_Reverse_ProducesContextWarning()
        {
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Benchmark with hasHeader=true, candidate with hasHeader=false
            var benchmarkContext = new ValidationContextSnapshot(
                "D1",
                new CalendarContext("forex", "Forex"),
                TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                "comma", true, null);
            var benchmark = CreateBenchmarkWithContext("test", benchmarkContext);

            var candidateIdentity = new CandidateIdentity(
                new SourceIdentity("candidate.csv", 100, Sha256()),
                new ValidationContextSnapshot(
                    "D1",
                    new CalendarContext("forex", "Forex"),
                    TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                    "comma", false, null));

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, candidateIdentity);

            Assert.Contains(report.ContextWarnings, w => w.Contains("Header mode differs"));
        }

        [Fact]
        public void Compare_IdenticalContext_NoWarnings()
        {
            var benchmark = CreateBenchmark("test");
            var candles = CreateCandleSet();

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(benchmark, candles, candles, CreateCandidateIdentity());

            Assert.Empty(report.ContextWarnings);
        }

        [Fact]
        public void Compare_NullDateRange_NoDateRangeWarning()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Candidate with no date range
            var candidateIdentity = new CandidateIdentity(
                new SourceIdentity("candidate.csv", 100, Sha256()),
                new ValidationContextSnapshot(
                    "D1",
                    new CalendarContext("forex", "Forex"),
                    TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                    "comma", false, null)); // null date range = benchmark's null date range

            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, candidateIdentity);

            // Should have no date range warning since both are null
            Assert.DoesNotContain(report.ContextWarnings, w => w.Contains("Date range"));
        }

        #region Test Helpers

        private static BenchmarkSnapshot CreateBenchmark(string name) =>
            CreateBenchmarkWithContext(name, CreateContext("D1"));

        private static BenchmarkSnapshot CreateBenchmarkWithContext(string name, ValidationContextSnapshot context)
        {
            var source = new SourceIdentity("AUDUSD_D1_reference.csv", 1024567, Sha256());
            var metrics = MetricPopulationMap.CanonicalOrder.Select(cat =>
                MetricScoreCalculator.ScoreMetric(cat, 0, 100, MetricPopulationMap.KindFor(cat))
            ).ToList();
            var weighting = ScoreWeightResolver.Default();
            var datasetScore = DatasetScore.Available(
                new ScoreValue(new ExactRatio(100, 1)),
                MetricPopulationMap.CanonicalOrder.ToList(),
                new List<ExcludedMetric>());

            return new BenchmarkSnapshot(
                name: name,
                establishedAtUtc: DateTimeOffset.UtcNow,
                source: source,
                context: context,
                coverage: new ScanCoverage(5, 5, 0),
                checks: CanonicalChecks(),
                metrics: metrics,
                dataset: datasetScore,
                weighting: weighting);
        }

        private static CandidateIdentity CreateCandidateIdentity()
        {
            return new CandidateIdentity(
                new SourceIdentity("AUDUSD_D1_candidate.csv", 1024567, Sha256()),
                CreateContext("D1"));
        }

        private static ValidationContextSnapshot CreateContext(string timeframe)
        {
            return new ValidationContextSnapshot(
                timeframe,
                new CalendarContext("forex", "Forex"),
                TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                "comma", false, null);
        }

        private static CheckExecution[] CanonicalChecks() => new[]
        {
            new CheckExecution(CheckName.MissingCandles, CheckStatus.Completed),
            new CheckExecution(CheckName.DuplicateRecords, CheckStatus.Completed),
            new CheckExecution(CheckName.InvalidOhlc, CheckStatus.Completed),
            new CheckExecution(CheckName.ClosedMarketRecords, CheckStatus.Completed),
            new CheckExecution(CheckName.TimeGaps, CheckStatus.Completed),
            new CheckExecution(CheckName.MalformedRows, CheckStatus.Completed)
        };

        private static List<PriceCandle> CreateCandleSet()
        {
            return new List<PriceCandle>
            {
                new(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                    0.63421m, 0.63580m, 0.63310m, 0.63502m, 125000m),
                new(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
                    0.63502m, 0.63650m, 0.63420m, 0.63612m, 118000m),
                new(new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero),
                    0.63612m, 0.63780m, 0.63550m, 0.63720m, 132000m),
                new(new DateTimeOffset(2026, 1, 7, 0, 0, 0, TimeSpan.Zero),
                    0.63720m, 0.63890m, 0.63680m, 0.63850m, 115000m),
                new(new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero),
                    0.63850m, 0.63920m, 0.63750m, 0.63810m, 128000m),
            };
        }

        private static string Sha256() => "abc123def456abc123def456abc123def456abc123def456abc123def456abcd";

        private sealed class FixedClock : Validator.Application.Abstractions.IApplicationClock
        {
            public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }

        #endregion
    }
}
