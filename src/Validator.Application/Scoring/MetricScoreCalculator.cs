using Validator.Domain.Findings;
using Validator.Domain.Scoring;

namespace Validator.Application.Scoring
{
    // Computes one metric's score as the exact rational 100 x (population - count)
    // / population. The score is never rounded here; ScoreValue holds the exact
    // ratio and rounds only for presentation. A count exceeding its population,
    // or a non-positive population, is an internal inconsistency and fails.
    public static class MetricScoreCalculator
    {
        private static readonly ExactRatio Hundred = new(100, 1);

        // The exact score for a positive population. Throws
        // ImpossibleDefectRateException when the inputs cannot form a rate in
        // 0..1, so the run fails rather than clamping.
        public static ScoreValue Score(long count, long population)
        {
            if (population <= 0 || count > population || count < 0)
            {
                throw new ImpossibleDefectRateException(count, population);
            }

            var goodFraction = new ExactRatio(population - count, population);
            return new ScoreValue(Hundred.Multiply(goodFraction));
        }

        // Builds the complete scored MetricScore for a metric whose check ran and
        // whose population is positive.
        public static MetricScore ScoreMetric(
            FindingCategory category,
            long count,
            long population,
            MetricPopulationKind populationKind) =>
            MetricScore.Scored(category, count, population, populationKind, Score(count, population));
    }
}
