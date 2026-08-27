using System;
using System.Collections.Generic;
using System.Numerics;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;

namespace Validator.Application.Scoring
{
    // Resolves the weighting actually applied to a run: the default equal
    // weighting when the caller supplied none, and the normalised share of every
    // metric included in the average. Shares are derived from exact rationals
    // over the scored weights and rounded independently to two decimals for
    // presentation, so the unrounded shares sum to exactly 1 even when the
    // printed ones do not (six equal shares print as 0.17 and sum to 1.02).
    public static class ScoreWeightResolver
    {
        // Every metric weighted 1 by default, so the default average is a plain
        // mean and is deliberately neutral.
        public static ScoreWeighting Default()
        {
            var weights = new List<MetricWeight>(6);
            foreach (var category in MetricPopulationMap.CanonicalOrder)
            {
                weights.Add(new MetricWeight(category, 1m));
            }

            return new ScoreWeighting(ScoreWeightingSource.Default, weights);
        }

        // Recomputes each metric's normalised share over exactly the covered
        // categories. A covered metric's share is its weight divided by the sum
        // of the covered weights; metrics outside the average carry no share, and
        // when the covered weights sum to zero no share is defined.
        public static ScoreWeighting WithNormalisedShares(
            ScoreWeighting weighting,
            IReadOnlyCollection<FindingCategory> coveredCategories)
        {
            ArgumentNullException.ThrowIfNull(weighting);
            ArgumentNullException.ThrowIfNull(coveredCategories);

            var covered = new HashSet<FindingCategory>(coveredCategories);
            var totalWeight = ExactRatio.Zero;
            foreach (var weight in weighting.Weights)
            {
                if (covered.Contains(weight.Category))
                {
                    totalWeight = totalWeight.Add(ToRatio(weight.Weight));
                }
            }

            var totalIsPositive = totalWeight.CompareTo(ExactRatio.Zero) > 0;
            var resolved = new List<MetricWeight>(6);
            foreach (var weight in weighting.Weights)
            {
                if (!covered.Contains(weight.Category) || !totalIsPositive)
                {
                    resolved.Add(weight.WithNormalisedShare(null));
                    continue;
                }

                var shareRatio = ToRatio(weight.Weight).Divide(totalWeight);
                resolved.Add(weight.WithNormalisedShare(RoundToTwoDecimals(shareRatio)));
            }

            return new ScoreWeighting(weighting.Source, resolved);
        }

        // A non-negative decimal weight as an exact rational over powers of ten,
        // so no precision is lost before division.
        private static ExactRatio ToRatio(decimal value)
        {
            var scaled = value * 1_000_000m;
            return new ExactRatio(new BigInteger(scaled), new BigInteger(1_000_000));
        }

        // Rounds a non-negative share in [0, 1] to two decimals, half away from
        // zero, using exact integer arithmetic so the midpoint decision never
        // depends on a binary floating approximation. A share is a weight divided
        // by a positive total, so it is never negative.
        private static decimal RoundToTwoDecimals(ExactRatio share)
        {
            var scaledNumerator = share.Numerator * 100;
            var quotient = BigInteger.DivRem(scaledNumerator, share.Denominator, out var remainder);
            if (remainder * 2 >= share.Denominator)
            {
                quotient += BigInteger.One;
            }

            return (decimal)quotient / 100m;
        }

    }
}
