using System;
using System.Collections.Generic;
using System.Numerics;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;

namespace Validator.Application.Scoring
{
    // Computes the single dataset average as the weighted mean of exactly the
    // scored metrics, over their unrounded exact scores, and rounded once for
    // presentation. The average is unavailable when no metric is scored or when
    // every scored metric's weight is zero; it is then reported with a reason
    // and never with a substitute value.
    public static class DatasetAverageCalculator
    {
        public const string NoScoredMetricReason = "no metric could be scored";
        public const string AllScoredWeightsZeroReason = "every scored metric had a weight of zero";

        public static DatasetScore Compute(
            IReadOnlyList<MetricScore> metrics,
            ScoreWeighting weighting)
        {
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(weighting);

            var covered = new List<FindingCategory>();
            var excluded = new List<ExcludedMetric>();
            var weightedSum = ExactRatio.Zero;
            var totalWeight = ExactRatio.Zero;

            foreach (var metric in metrics)
            {
                if (metric.State != MetricScoreState.Scored || metric.Score is null)
                {
                    excluded.Add(new ExcludedMetric(metric.Category, metric.State, metric.Reason!));
                    continue;
                }

                covered.Add(metric.Category);
                var weight = ToRatio(weighting.For(metric.Category).Weight);
                weightedSum = weightedSum.Add(metric.Score.Value.Exact.Multiply(weight));
                totalWeight = totalWeight.Add(weight);
            }

            if (covered.Count == 0)
            {
                return DatasetScore.Unavailable(NoScoredMetricReason, covered, excluded);
            }

            if (totalWeight.CompareTo(ExactRatio.Zero) <= 0)
            {
                return DatasetScore.Unavailable(AllScoredWeightsZeroReason, covered, excluded);
            }

            var average = new ScoreValue(weightedSum.Divide(totalWeight));
            return DatasetScore.Available(average, covered, excluded);
        }

        private static ExactRatio ToRatio(decimal value)
        {
            var scaled = value * 1_000_000m;
            return new ExactRatio(new BigInteger(scaled), new BigInteger(1_000_000));
        }
    }
}
