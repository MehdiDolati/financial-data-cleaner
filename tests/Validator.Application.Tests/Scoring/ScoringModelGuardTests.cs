using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Scoring;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;
using Xunit;

namespace Validator.Application.Tests.Scoring
{
    // Exercises the constructor guards, property accessors, and boundary paths of
    // the scoring model so every branch of the invariants that protect the
    // feature is proven, not merely assumed.
    public sealed class ScoringModelGuardTests
    {
        private static ScoreValue Score(long numerator, long denominator) =>
            new(new ExactRatio(numerator, denominator));

        private static IReadOnlyList<MetricWeight> EqualWeights() =>
            MetricPopulationMap.CanonicalOrder.Select(category => new MetricWeight(category, 1m)).ToList();

        // --- MetricPopulationMap ---

        [Fact]
        public void MetricPopulationMap_KindFor_RejectsANonMetricCategory()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MetricPopulationMap.KindFor(FindingCategory.Critical));
        }

        // --- MetricPopulations ---

        [Fact]
        public void MetricPopulations_ForAnUnknownKind_Throws()
        {
            var populations = MetricPopulations.FromScanCoverage(new Validator.Application.Ingestion.ScanCoverage(6, 5, 1), 5);

            Assert.Throws<ArgumentOutOfRangeException>(() => populations.For((MetricPopulationKind)99));
        }

        [Fact]
        public void MetricPopulations_FromScanCoverage_NullCoverage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => MetricPopulations.FromScanCoverage(null!, 5));
        }

        [Fact]
        public void MetricPopulations_NegativeExpectedCandles_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MetricPopulations.FromScanCoverage(
                new Validator.Application.Ingestion.ScanCoverage(6, 5, 1), expectedCandles: -1));
        }


        // --- MetricScore ---

        [Fact]
        public void MetricScore_ScoredWithZeroPopulation_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MetricScore.Scored(
                FindingCategory.DuplicateRecord, count: 0, population: 0, MetricPopulationKind.AcceptedRows, Score(100, 1)));
        }

        [Fact]
        public void MetricScore_ScoredWithCountAbovePopulation_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MetricScore.Scored(
                FindingCategory.DuplicateRecord, count: 6, population: 5, MetricPopulationKind.AcceptedRows, Score(0, 1)));
        }

        [Fact]
        public void MetricScore_NotScored_CarriesItsCountAndZeroPopulation()
        {
            var metric = MetricScore.NotScored(
                FindingCategory.MalformedRow, MetricPopulationKind.ExaminedRows, "zero population", count: 3);

            Assert.Equal(3, metric.Count);
            Assert.Equal(0, metric.Population);
            Assert.Equal(MetricScoreState.NotScored, metric.State);
        }

        // --- MetricScore internal constructor guard arms (T038) ---

        [Fact]
        public void MetricScore_InternalCtor_NegativeCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MetricScore(
                    FindingCategory.MissingCandle,
                    MetricScoreState.Scored,
                    count: -1,
                    population: 10,
                    MetricPopulationKind.AcceptedRows,
                    Score(1, 1),
                    reason: null));
        }

        [Fact]
        public void MetricScore_InternalCtor_ScoredWithNullScore_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new MetricScore(
                    FindingCategory.MissingCandle,
                    MetricScoreState.Scored,
                    count: 1,
                    population: 10,
                    MetricPopulationKind.AcceptedRows,
                    score: null,
                    reason: null));
        }

        [Fact]
        public void MetricScore_InternalCtor_ScoredWithReason_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new MetricScore(
                    FindingCategory.MissingCandle,
                    MetricScoreState.Scored,
                    count: 1,
                    population: 10,
                    MetricPopulationKind.AcceptedRows,
                    Score(1, 1),
                    reason: "should not have a reason"));
        }

        [Fact]
        public void MetricScore_InternalCtor_UnscoredWithScore_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new MetricScore(
                    FindingCategory.MissingCandle,
                    MetricScoreState.NotApplicable,
                    count: 0,
                    population: null,
                    MetricPopulationKind.AcceptedRows,
                    Score(1, 1),
                    reason: null));
        }

        [Fact]
        public void MetricScore_InternalCtor_UnscoredWithBlankReason_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new MetricScore(
                    FindingCategory.MissingCandle,
                    MetricScoreState.NotScored,
                    count: 0,
                    population: 0,
                    MetricPopulationKind.ExaminedRows,
                    score: null,
                    reason: "   "));
        }

        [Fact]
        public void MetricScore_InternalCtor_UnscoredWithNullReason_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new MetricScore(
                    FindingCategory.MissingCandle,
                    MetricScoreState.NotScored,
                    count: 0,
                    population: 0,
                    MetricPopulationKind.ExaminedRows,
                    score: null,
                    reason: null));
        }

        // --- DatasetScore internal constructor guard arms (T039) ---

        [Fact]
        public void DatasetScore_InternalCtor_UnavailableWithBlankReason_Throws()
        {
            var allExcluded = MetricPopulationMap.CanonicalOrder
                .Select(c => new ExcludedMetric(c, MetricScoreState.NotScored, "zero")).ToList();

            Assert.Throws<ArgumentException>(() =>
                new DatasetScore(
                    average: null,
                    metricsCovered: 0,
                    coveredCategories: [],
                    excludedCategories: allExcluded,
                    unavailableReason: "   "));
        }

        [Fact]
        public void DatasetScore_InternalCtor_AvailableWithReason_Throws()
        {
            var allExcluded = MetricPopulationMap.CanonicalOrder
                .Select(c => new ExcludedMetric(c, MetricScoreState.NotScored, "zero")).ToList();

            Assert.Throws<ArgumentException>(() =>
                new DatasetScore(
                    average: Score(100, 1),
                    metricsCovered: 6,
                    coveredCategories: MetricPopulationMap.CanonicalOrder.ToList(),
                    excludedCategories: allExcluded,
                    unavailableReason: "should not have a reason"));
        }

        [Fact]
        public void DatasetScore_InternalCtor_CoveredPlusExcludedNotSix_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new DatasetScore(
                    average: Score(100, 1),
                    metricsCovered: 1,
                    coveredCategories: [FindingCategory.DuplicateRecord],
                    excludedCategories: [],
                    unavailableReason: null));
        }

        // --- ScoreWeighting ---

        [Fact]
        public void ScoreWeighting_RequiresSixWeights()
        {
            Assert.Throws<ArgumentException>(() => new ScoreWeighting(
                ScoreWeightingSource.Default,
                [new MetricWeight(FindingCategory.MissingCandle, 1m)]));
        }

        [Fact]
        public void ScoreWeighting_RequiresCanonicalOrder()
        {
            var outOfOrder = EqualWeights().Reverse().ToList();

            Assert.Throws<ArgumentException>(() => new ScoreWeighting(ScoreWeightingSource.Default, outOfOrder));
        }

        [Fact]
        public void ScoreWeighting_NullWeights_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ScoreWeighting(ScoreWeightingSource.Default, null!));
        }

        [Fact]
        public void ScoreWeighting_For_ReturnsEachCategoryAndRejectsUnknown()
        {
            var weighting = new ScoreWeighting(ScoreWeightingSource.Default, EqualWeights());

            Assert.Equal(1m, weighting.For(FindingCategory.TimeGap).Weight);
            Assert.Throws<ArgumentOutOfRangeException>(() => weighting.For(FindingCategory.Critical));
        }

        [Fact]
        public void MetricWeight_NegativeWeight_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MetricWeight(FindingCategory.MissingCandle, -1m));
        }

        // --- DatasetScore ---

        [Fact]
        public void DatasetScore_CoveredPlusExcludedMustTotalSix()
        {
            Assert.Throws<ArgumentException>(() => DatasetScore.Available(
                Score(100, 1),
                coveredCategories: [FindingCategory.DuplicateRecord],
                excludedCategories: []));
        }

        [Fact]
        public void ExcludedMetric_RejectsAScoredStateOrBlankReason()
        {
            Assert.Throws<ArgumentException>(() => new ExcludedMetric(
                FindingCategory.MissingCandle, MetricScoreState.Scored, "cannot be scored"));
            Assert.Throws<ArgumentException>(() => new ExcludedMetric(
                FindingCategory.MissingCandle, MetricScoreState.NotApplicable, "   "));
        }

        [Fact]
        public void DatasetScore_UnavailableWithoutAReason_Throws()
        {
            var allExcluded = MetricPopulationMap.CanonicalOrder
                .Select(category => new ExcludedMetric(category, MetricScoreState.NotScored, "zero")).ToList();

            Assert.Throws<ArgumentException>(() => DatasetScore.Unavailable("   ", [], allExcluded));
        }


        // --- DatasetScoreReport / ScoreScale ---

        [Fact]
        public void ScoreScale_Default_StatesTheFixedRangeAndDirection()
        {
            var scale = ScoreScale.Default;

            Assert.Equal(0, scale.Minimum);
            Assert.Equal(100, scale.Maximum);
            Assert.True(scale.HigherIsBetter);
            Assert.Equal(2, scale.DecimalPlaces);
        }

        [Fact]
        public void DatasetScoreReport_RequiresSixMetricsInCanonicalOrder()
        {
            var weighting = new ScoreWeighting(ScoreWeightingSource.Default, EqualWeights());
            var dataset = DatasetScore.Unavailable(
                "no metric could be scored",
                coveredCategories: [],
                excludedCategories: MetricPopulationMap.CanonicalOrder
                    .Select(category => new ExcludedMetric(category, MetricScoreState.NotScored, "zero")).ToList());

            Assert.Throws<ArgumentException>(() => new DatasetScoreReport(
                metrics: [MetricScore.NotScored(FindingCategory.MissingCandle, MetricPopulationKind.ExpectedCandles, "zero")],
                weighting,
                dataset));
        }

        // --- ScoreRequest ---

        [Fact]
        public void ScoreRequest_Default_CarriesTheEqualWeighting()
        {
            var request = ScoreRequest.Default();

            Assert.Equal(ScoreWeightingSource.Default, request.Weighting.Source);
        }

        [Fact]
        public void ScoreRequest_NullWeighting_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ScoreRequest(null!));
        }
    }
}
