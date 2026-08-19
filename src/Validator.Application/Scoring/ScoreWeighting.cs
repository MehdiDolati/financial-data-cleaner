using System;
using System.Collections.Generic;
using Validator.Domain.Findings;

namespace Validator.Application.Scoring
{
    // Where a run's weighting came from.
    public enum ScoreWeightingSource
    {
        // Every metric weighted equally, so the default average is a plain mean.
        Default = 0,

        // All six weights supplied explicitly by the caller.
        CallerSupplied = 1
    }

    // The resolved weight of one metric. NormalisedShare is the metric's share of
    // the weights actually used for the average, rounded to two decimals for
    // presentation, and is present only for metrics included in the average.
    public sealed record MetricWeight
    {
        public FindingCategory Category { get; }

        public decimal Weight { get; }

        public decimal? NormalisedShare { get; }

        public MetricWeight(FindingCategory category, decimal weight, decimal? normalisedShare = null)
        {
            if (weight < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(weight), "A weight must be non-negative.");
            }

            Category = category;
            Weight = weight;
            NormalisedShare = normalisedShare;
        }

        public MetricWeight WithNormalisedShare(decimal? share) =>
            new(Category, Weight, share);
    }

    // The complete resolved weighting for a run: its source and one weight per
    // metric in canonical order. Weights affect only the average; they never
    // change a per-metric score, count, population, or applicability state.
    public sealed record ScoreWeighting
    {
        public ScoreWeightingSource Source { get; }

        public IReadOnlyList<MetricWeight> Weights { get; }

        public ScoreWeighting(ScoreWeightingSource source, IReadOnlyList<MetricWeight> weights)
        {
            ArgumentNullException.ThrowIfNull(weights);
            if (weights.Count != 6)
            {
                throw new ArgumentException("A weighting must carry exactly six metric weights.", nameof(weights));
            }

            for (var index = 0; index < 6; index++)
            {
                if (weights[index].Category != MetricPopulationMap.CanonicalOrder[index])
                {
                    throw new ArgumentException(
                        "Weights must appear once per category in canonical order.",
                        nameof(weights));
                }
            }

            Source = source;
            Weights = weights;
        }

        public MetricWeight For(FindingCategory category)
        {
            foreach (var weight in Weights)
            {
                if (weight.Category == category)
                {
                    return weight;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(category), category, "No weight exists for the category.");
        }
    }
}
