using System;
using System.Collections.Generic;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;

namespace Validator.Application.Scoring
{
    // One excluded metric on the dataset average: the category that was left out
    // and the state and reason it carried, so a reader sees why the coverage
    // narrowed rather than inferring it from a missing entry.
    public sealed record ExcludedMetric
    {
        public FindingCategory Category { get; }

        public MetricScoreState State { get; }

        public string Reason { get; }

        public ExcludedMetric(FindingCategory category, MetricScoreState state, string reason)
        {
            if (state == MetricScoreState.Scored)
            {
                throw new ArgumentException("A scored metric cannot be excluded from the average.", nameof(state));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("An excluded metric must carry a non-empty reason.", nameof(reason));
            }

            Category = category;
            State = state;
            Reason = reason;
        }
    }

    // The dataset's single average score and its coverage. The average is present
    // exactly when it is available; when it is unavailable the reason is stated
    // and no substitute value (0.00, 100.00, or otherwise) is offered.
    public sealed record DatasetScore
    {
        public ScoreValue? Average { get; }

        public int MetricsCovered { get; }

        public IReadOnlyList<FindingCategory> CoveredCategories { get; }

        public IReadOnlyList<ExcludedMetric> ExcludedCategories { get; }

        public string? UnavailableReason { get; }

        private DatasetScore(
            ScoreValue? average,
            int metricsCovered,
            IReadOnlyList<FindingCategory> coveredCategories,
            IReadOnlyList<ExcludedMetric> excludedCategories,
            string? unavailableReason)
        {
            ArgumentNullException.ThrowIfNull(coveredCategories);
            ArgumentNullException.ThrowIfNull(excludedCategories);

            if (average is null)
            {
                if (string.IsNullOrWhiteSpace(unavailableReason))
                {
                    throw new ArgumentException("An unavailable average must carry a reason.", nameof(unavailableReason));
                }
            }
            else if (unavailableReason is not null)
            {
                throw new ArgumentException("An available average must not carry an unavailability reason.", nameof(unavailableReason));
            }

            if (coveredCategories.Count + excludedCategories.Count != 6)
            {
                throw new ArgumentException("Covered and excluded categories must total the six established metrics.");
            }

            Average = average;
            MetricsCovered = metricsCovered;
            CoveredCategories = coveredCategories;
            ExcludedCategories = excludedCategories;
            UnavailableReason = unavailableReason;
        }

        public static DatasetScore Available(
            ScoreValue average,
            IReadOnlyList<FindingCategory> coveredCategories,
            IReadOnlyList<ExcludedMetric> excludedCategories) =>
            new(average, coveredCategories.Count, coveredCategories, excludedCategories, unavailableReason: null);

        public static DatasetScore Unavailable(
            string reason,
            IReadOnlyList<FindingCategory> coveredCategories,
            IReadOnlyList<ExcludedMetric> excludedCategories) =>
            new(average: null, coveredCategories.Count, coveredCategories, excludedCategories, reason);
    }
}
