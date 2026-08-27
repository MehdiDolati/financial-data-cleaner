using System;
using System.Collections.Generic;

namespace Validator.Application.Scoring
{
    // The fixed descriptor of the score scale, stated so the direction is
    // unambiguous: scores run 0..100 at two decimals, and higher is better.
    public sealed record ScoreScale
    {
        public int Minimum => 0;

        public int Maximum => 100;

        public bool HigherIsBetter => true;

        public int DecimalPlaces => 2;

        public static ScoreScale Default { get; } = new();
    }

    // The complete scoring result attached to one successful, reconciled run. It
    // exists only on a successful outcome, carries all six metrics exactly once
    // in canonical order, the resolved weighting, and the single dataset average,
    // and contains enough information to recompute every value from the report
    // alone.
    public sealed record DatasetScoreReport
    {
        public ScoreScale Scale { get; }

        public IReadOnlyList<MetricScore> Metrics { get; }

        public ScoreWeighting Weighting { get; }

        public DatasetScore Dataset { get; }

        public DatasetScoreReport(
            IReadOnlyList<MetricScore> metrics,
            ScoreWeighting weighting,
            DatasetScore dataset)
        {
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(weighting);
            ArgumentNullException.ThrowIfNull(dataset);

            if (metrics.Count != 6)
            {
                throw new ArgumentException("A score report must carry all six metrics.", nameof(metrics));
            }

            for (var index = 0; index < 6; index++)
            {
                if (metrics[index].Category != MetricPopulationMap.CanonicalOrder[index])
                {
                    throw new ArgumentException(
                        "Metrics must appear once per category in canonical order.",
                        nameof(metrics));
                }
            }

            Scale = ScoreScale.Default;
            Metrics = metrics;
            Weighting = weighting;
            Dataset = dataset;
        }
    }
}
