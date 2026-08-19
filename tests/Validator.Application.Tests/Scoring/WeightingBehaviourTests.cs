using System.Collections.Generic;
using System.Linq;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Findings;
using Xunit;

namespace Validator.Application.Tests.Scoring
{
    // Weighting refines only the average. These tests cover default equal
    // weights (T047), weight isolation from per-metric scores (T048), a zero
    // weight that still scores but contributes nothing (T049), and normalised
    // shares that are reported only for covered metrics, sum to exactly 1 when
    // unrounded, and are rounded independently (T050).
    public sealed class WeightingBehaviourTests
    {
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

        private static MetricPopulations Populations() =>
            MetricPopulations.FromScanCoverage(new ScanCoverage(50, 50, 0), expectedCandles: 84);

        [Fact]
        public void DefaultWeighting_IsEqualForAllSixAndReportedAsResolved()
        {
            var report = ScoreSectionBuilder.Build(
                Summary(), Populations(), AllCompleted(), ScoreWeightResolver.Default());

            Assert.Equal(ScoreWeightingSource.Default, report.Weighting.Source);
            Assert.All(report.Weighting.Weights, weight => Assert.Equal(1m, weight.Weight));
        }

        [Fact]
        public void SuppliedWeights_ChangeOnlyTheAverageNotThePerMetricScores()
        {
            var summary = Summary(missing: 1, duplicate: 1, invalid: 2, gaps: 2);

            var defaultReport = ScoreSectionBuilder.Build(
                summary, Populations(), AllCompleted(), ScoreWeightResolver.Default());
            var customReport = ScoreSectionBuilder.Build(
                summary,
                Populations(),
                AllCompleted(),
                ScoreWeightParser.Parse("missingCandles=3,duplicateRecords=1,invalidOhlc=2,closedMarketRecords=1,timeGaps=1,malformedRows=1"));

            // Per-metric scores, counts, populations, and states are untouched.
            foreach (var category in MetricPopulationMap.CanonicalOrder)
            {
                var left = defaultReport.Metrics.Single(metric => metric.Category == category);
                var right = customReport.Metrics.Single(metric => metric.Category == category);
                Assert.Equal(left.State, right.State);
                Assert.Equal(left.Count, right.Count);
                Assert.Equal(left.Population, right.Population);
                Assert.Equal(left.Score?.Format(), right.Score?.Format());
            }

            // Only the average differs.
            Assert.NotEqual(
                defaultReport.Dataset.Average!.Value.Format(),
                customReport.Dataset.Average!.Value.Format());
        }

        [Fact]
        public void ZeroWeight_StillScoresTheMetricButContributesNothingToTheAverage()
        {
            var summary = Summary(duplicate: 1);
            // Weight the (imperfect) duplicate metric zero; the average should
            // then equal 100.00 because every contributing metric is flawless.
            var weighting = ScoreWeightParser.Parse(
                "missingCandles=1,duplicateRecords=0,invalidOhlc=1,closedMarketRecords=1,timeGaps=1,malformedRows=1");

            var report = ScoreSectionBuilder.Build(summary, Populations(), AllCompleted(), weighting);

            var duplicate = report.Metrics.Single(metric => metric.Category == FindingCategory.DuplicateRecord);
            Assert.Equal(MetricScoreState.Scored, duplicate.State);
            Assert.Equal("98.00", duplicate.Score!.Value.Format());
            Assert.Equal("100.00", report.Dataset.Average!.Value.Format());
        }

        [Fact]
        public void NormalisedShares_AreReportedOnlyForCoveredMetricsAndRoundedIndependently()
        {
            var report = ScoreSectionBuilder.Build(
                Summary(), Populations(), AllCompleted(), ScoreWeightResolver.Default());

            // Six equal shares each print as 0.17 and therefore need not sum to 1.
            var shares = report.Weighting.Weights.Select(weight => weight.NormalisedShare).ToArray();
            Assert.All(shares, share => Assert.Equal(0.17m, share));
            Assert.Equal(1.02m, shares.Sum(share => share!.Value));
        }

        [Fact]
        public void NormalisedShares_AreAbsentForExcludedMetrics()
        {
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(50, 50, 0), expectedCandles: null);
            IReadOnlyList<CheckExecution> checks =
            [
                new(CheckName.MissingCandles, CheckStatus.NotApplicable, "seq"),
                new(CheckName.DuplicateRecords, CheckStatus.Completed),
                new(CheckName.InvalidOhlc, CheckStatus.Completed),
                new(CheckName.ClosedMarketRecords, CheckStatus.Completed),
                new(CheckName.TimeGaps, CheckStatus.NotApplicable, "seq"),
                new(CheckName.MalformedRows, CheckStatus.Completed)
            ];

            var report = ScoreSectionBuilder.Build(Summary(), populations, checks, ScoreWeightResolver.Default());

            Assert.Null(report.Weighting.For(FindingCategory.MissingCandle).NormalisedShare);
            Assert.Null(report.Weighting.For(FindingCategory.TimeGap).NormalisedShare);
            Assert.NotNull(report.Weighting.For(FindingCategory.DuplicateRecord).NormalisedShare);
        }
    }
}
