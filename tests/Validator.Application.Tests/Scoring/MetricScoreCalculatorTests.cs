using System;
using Validator.Application.Scoring;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;
using Xunit;

namespace Validator.Application.Tests.Scoring
{
    // The per-metric score is exactly 100 x (population - count) / population,
    // computed as an exact rational. Zero defects score a perfect 100.00; a total
    // defect rate scores 0.00; a count exceeding its population is an internal
    // inconsistency that fails the run rather than being clamped.
    public sealed class MetricScoreCalculatorTests
    {
        [Theory]
        [InlineData(0, 84, 100, 1)]     // no defects -> exactly 100
        [InlineData(84, 84, 0, 1)]      // total defect rate -> exactly 0
        [InlineData(1, 84, 8300, 84)]   // 100 x 83/84
        [InlineData(1, 50, 98, 1)]      // 100 x 49/50 = 98
        [InlineData(2, 50, 96, 1)]      // 100 x 48/50 = 96
        public void Score_IsExactlyOneHundredTimesGoodFraction(long count, long population, long expectedNum, long expectedDen)
        {
            var score = MetricScoreCalculator.Score(count, population);

            Assert.Equal(new ExactRatio(expectedNum, expectedDen), score.Exact);
        }

        [Fact]
        public void Score_WithZeroDefects_IsExactlyOneHundred()
        {
            var score = MetricScoreCalculator.Score(count: 0, population: 50);

            Assert.Equal("100.00", score.Format());
        }

        [Fact]
        public void Score_WithATotalDefectRate_IsExactlyZero()
        {
            var score = MetricScoreCalculator.Score(count: 50, population: 50);

            Assert.Equal("0.00", score.Format());
        }

        [Fact]
        public void Score_WhenCountExceedsPopulation_ThrowsImpossibleRate()
        {
            var error = Assert.Throws<ImpossibleDefectRateException>(
                () => MetricScoreCalculator.Score(count: 51, population: 50));

            Assert.Equal(51, error.Count);
            Assert.Equal(50, error.Population);
        }

        [Fact]
        public void Score_WithZeroPopulation_ThrowsImpossibleRate()
        {
            // A zero population has no defined rate; scoring must never be asked
            // to divide by it, and doing so is an internal inconsistency.
            Assert.Throws<ImpossibleDefectRateException>(
                () => MetricScoreCalculator.Score(count: 0, population: 0));
        }

        [Fact]
        public void ScoreMetric_ForAScoredMetric_CarriesCountPopulationAndKind()
        {
            var metric = MetricScoreCalculator.ScoreMetric(
                FindingCategory.DuplicateRecord,
                count: 1,
                population: 50,
                MetricPopulationKind.AcceptedRows);

            Assert.Equal(MetricScoreState.Scored, metric.State);
            Assert.Equal(1, metric.Count);
            Assert.Equal(50, metric.Population);
            Assert.Equal(MetricPopulationKind.AcceptedRows, metric.PopulationKind);
            Assert.Equal("98.00", metric.Score!.Value.Format());
        }
    }
}
