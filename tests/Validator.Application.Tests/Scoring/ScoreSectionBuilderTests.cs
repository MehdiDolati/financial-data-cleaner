using System.Collections.Generic;
using System.Linq;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Findings;
using Xunit;

namespace Validator.Application.Tests.Scoring
{
    // ScoreSectionBuilder assembles the whole section from the summary, the
    // resolved populations, and the check statuses. These tests cover metric
    // applicability (T022), zero populations (T023), the impossible-rate failure
    // (T024), the default average (T035), coverage reporting (T036), the flawless
    // average (T037), unavailable averages (T038), and rounding (T039/T039a).
    public sealed class ScoreSectionBuilderTests
    {
        private static readonly string SequenceReason =
            "Fewer than two open-market timestamps bound an expected sequence.";

        private static DetailedSummary Summary(
            long missing = 0, long duplicate = 0, long invalid = 0,
            long closed = 0, long gaps = 0, long malformed = 0) =>
            new(missing, duplicate, invalid, closed, gaps, malformed);

        private static IReadOnlyList<CheckExecution> AllCompleted() =>
        [
            new(CheckName.MissingCandles, CheckStatus.Completed),
            new(CheckName.DuplicateRecords, CheckStatus.Completed),
            new(CheckName.InvalidOhlc, CheckStatus.Completed),
            new(CheckName.ClosedMarketRecords, CheckStatus.Completed),
            new(CheckName.TimeGaps, CheckStatus.Completed),
            new(CheckName.MalformedRows, CheckStatus.Completed)
        ];

        private static IReadOnlyList<CheckExecution> SequenceNotApplicable() =>
        [
            new(CheckName.MissingCandles, CheckStatus.NotApplicable, SequenceReason),
            new(CheckName.DuplicateRecords, CheckStatus.Completed),
            new(CheckName.InvalidOhlc, CheckStatus.Completed),
            new(CheckName.ClosedMarketRecords, CheckStatus.Completed),
            new(CheckName.TimeGaps, CheckStatus.NotApplicable, SequenceReason),
            new(CheckName.MalformedRows, CheckStatus.Completed)
        ];

        private static MetricScore MetricFor(DatasetScoreReport report, FindingCategory category) =>
            report.Metrics.Single(metric => metric.Category == category);

        // --- Applicability (T022) ---

        [Fact]
        public void SequenceCheckNotRun_MarksTimeMetricsNotApplicableWithTheCheckReason()
        {
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(50, 50, 0), expectedCandles: null);

            var report = ScoreSectionBuilder.Build(
                Summary(),
                populations,
                SequenceNotApplicable(),
                ScoreWeightResolver.Default());

            var missing = MetricFor(report, FindingCategory.MissingCandle);
            var gaps = MetricFor(report, FindingCategory.TimeGap);
            Assert.Equal(MetricScoreState.NotApplicable, missing.State);
            Assert.Equal(MetricScoreState.NotApplicable, gaps.State);
            Assert.Equal(SequenceReason, missing.Reason);
            Assert.Null(missing.Score);
            Assert.Null(gaps.Score);
        }

        // --- Zero population (T023) ---

        [Fact]
        public void ZeroAcceptedRows_MarksRecordMetricsNotScoredNeverPerfect()
        {
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(0, 0, 0), expectedCandles: 84);

            var report = ScoreSectionBuilder.Build(
                Summary(),
                populations,
                AllCompleted(),
                ScoreWeightResolver.Default());

            var duplicate = MetricFor(report, FindingCategory.DuplicateRecord);
            Assert.Equal(MetricScoreState.NotScored, duplicate.State);
            Assert.Null(duplicate.Score);
            Assert.False(string.IsNullOrWhiteSpace(duplicate.Reason));
        }

        // --- Impossible rate (T024) ---

        [Fact]
        public void CountExceedingPopulation_ThrowsImpossibleDefectRate()
        {
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(50, 50, 0), expectedCandles: 84);

            Assert.Throws<ImpossibleDefectRateException>(() => ScoreSectionBuilder.Build(
                Summary(duplicate: 51),
                populations,
                AllCompleted(),
                ScoreWeightResolver.Default()));
        }

        // --- Default average (T035) ---

        [Fact]
        public void DefaultWeights_AverageIsThePlainMeanOfAllSixScores()
        {
            // All six scored: missing/gaps over 84 expected candles, the rest
            // over 50 accepted/examined rows, all zero-defect -> average 100.00.
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(50, 50, 0), expectedCandles: 84);

            var report = ScoreSectionBuilder.Build(
                Summary(),
                populations,
                AllCompleted(),
                ScoreWeightResolver.Default());

            Assert.Equal(6, report.Dataset.MetricsCovered);
            Assert.Equal("100.00", report.Dataset.Average!.Value.Format());
        }

        // --- Coverage and exclusions (T036) ---

        [Fact]
        public void ReducedCoverage_ReportsCoveredCountAndExcludedMetrics()
        {
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(50, 50, 0), expectedCandles: null);

            var report = ScoreSectionBuilder.Build(
                Summary(),
                populations,
                SequenceNotApplicable(),
                ScoreWeightResolver.Default());

            Assert.Equal(4, report.Dataset.MetricsCovered);
            Assert.Equal(
                new[] { FindingCategory.MissingCandle, FindingCategory.TimeGap },
                report.Dataset.ExcludedCategories.Select(excluded => excluded.Category).ToArray());
            Assert.All(report.Dataset.ExcludedCategories, excluded =>
                Assert.False(string.IsNullOrWhiteSpace(excluded.Reason)));
        }

        // --- Flawless average (T037) ---

        [Fact]
        public void Average_IsExactly100_OnlyWhenEveryCoveredMetricIs100()
        {
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(50, 50, 0), expectedCandles: 84);

            var flawless = ScoreSectionBuilder.Build(
                Summary(), populations, AllCompleted(), ScoreWeightResolver.Default());
            Assert.Equal("100.00", flawless.Dataset.Average!.Value.Format());

            var oneDefect = ScoreSectionBuilder.Build(
                Summary(duplicate: 1), populations, AllCompleted(), ScoreWeightResolver.Default());
            Assert.NotEqual("100.00", oneDefect.Dataset.Average!.Value.Format());
        }

        // --- Unavailable average (T038) ---

        [Fact]
        public void NoScoredMetric_ReportsUnavailableAverageWithReasonNeverASubstitute()
        {
            // Empty dataset: no expected candles and zero rows, so nothing scores.
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(0, 0, 0), expectedCandles: null);

            var report = ScoreSectionBuilder.Build(
                Summary(),
                populations,
                SequenceNotApplicable(),
                ScoreWeightResolver.Default());

            Assert.Null(report.Dataset.Average);
            Assert.False(string.IsNullOrWhiteSpace(report.Dataset.UnavailableReason));
        }

        // --- Rounding once, from unrounded scores (T039/T039a) ---

        [Fact]
        public void Average_IsExactly98_40_NotThe98_41_OfAveragingRoundedScores()
        {
            // The documented cli.md example: missing=1/84, duplicates=1/50,
            // invalidOhlc=2/50, closedMarket=0/50, timeGaps=2/84, malformed=0/50.
            // Averaging the unrounded scores gives 98.4047... -> 98.40, whereas
            // averaging the printed two-decimal values would give 98.41.
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(50, 50, 0), expectedCandles: 84);

            var report = ScoreSectionBuilder.Build(
                Summary(missing: 1, duplicate: 1, invalid: 2, closed: 0, gaps: 2, malformed: 0),
                populations,
                AllCompleted(),
                ScoreWeightResolver.Default());

            Assert.Equal("98.40", report.Dataset.Average!.Value.Format());
            Assert.NotEqual("98.41", report.Dataset.Average!.Value.Format());
        }

        // --- All scored weights zero (T038 second cause) ---

        [Fact]
        public void EveryScoredWeightZero_ReportsUnavailableAverageWithMetricsStillCovered()
        {
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(50, 50, 0), expectedCandles: 84);
            var allZero = new ScoreWeighting(
                ScoreWeightingSource.CallerSupplied,
                MetricPopulationMap.CanonicalOrder.Select(category => new MetricWeight(category, 0m)).ToList());

            var report = ScoreSectionBuilder.Build(Summary(), populations, AllCompleted(), allZero);

            Assert.Null(report.Dataset.Average);
            Assert.Equal(DatasetAverageCalculator.AllScoredWeightsZeroReason, report.Dataset.UnavailableReason);
            Assert.Equal(6, report.Dataset.MetricsCovered);
        }

        // --- A time metric whose population is null but check not NotApplicable ---

        [Fact]
        public void NullExpectedCandlesWithCompletedSequenceCheck_IsNotApplicableWithAPopulationReason()
        {
            // Defensive path: the population is unavailable although the sequence
            // check is not flagged NotApplicable. The metric must still not be
            // scored and must state a reason rather than inventing a denominator.
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(50, 50, 0), expectedCandles: null);

            var report = ScoreSectionBuilder.Build(
                Summary(), populations, AllCompleted(), ScoreWeightResolver.Default());

            var missing = MetricFor(report, FindingCategory.MissingCandle);
            Assert.Equal(MetricScoreState.NotApplicable, missing.State);
            Assert.False(string.IsNullOrWhiteSpace(missing.Reason));
        }

        // --- Zero examined rows exercises the examined-row NotScored reason ---

        [Fact]
        public void ZeroExaminedRows_MarksMalformedNotScoredWithItsPopulationKindReason()
        {
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(0, 0, 0), expectedCandles: 84);

            var report = ScoreSectionBuilder.Build(
                Summary(), populations, AllCompleted(), ScoreWeightResolver.Default());

            var malformed = MetricFor(report, FindingCategory.MalformedRow);
            Assert.Equal(MetricScoreState.NotScored, malformed.State);
            Assert.Contains("examined-row", malformed.Reason!, System.StringComparison.Ordinal);
        }

        // --- Missing check exercises the FindCheck null-return path (T004) ---

        [Fact]
        public void MissingCheck_StillScoresTheMetricUsingPopulationAndCount()
        {
            // When the checks list does not include a check for a given category,
            // FindCheck returns null and the metric is scored using population and
            // count directly, rather than being marked NotApplicable.
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(50, 50, 0), expectedCandles: 84);

            // Provide only 5 checks — MissingCandles is absent.
            var incompleteChecks = new List<CheckExecution>
            {
                new(CheckName.DuplicateRecords, CheckStatus.Completed),
                new(CheckName.InvalidOhlc, CheckStatus.Completed),
                new(CheckName.ClosedMarketRecords, CheckStatus.Completed),
                new(CheckName.TimeGaps, CheckStatus.Completed),
                new(CheckName.MalformedRows, CheckStatus.Completed)
            };

            var report = ScoreSectionBuilder.Build(
                Summary(), populations, incompleteChecks, ScoreWeightResolver.Default());

            // MissingCandle should still be scored (not NotApplicable) because
            // FindCheck returned null and the population is available.
            var missing = MetricFor(report, FindingCategory.MissingCandle);
            Assert.Equal(MetricScoreState.Scored, missing.State);
            Assert.NotNull(missing.Score);
        }
    }
}


