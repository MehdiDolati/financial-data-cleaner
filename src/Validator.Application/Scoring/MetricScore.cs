using System;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;

namespace Validator.Application.Scoring
{
    // The scored result for one of the six established metrics. Exactly one state
    // holds: a Scored metric carries a value and no reason; a NotApplicable or
    // NotScored metric carries a reason and no value. The constructor enforces
    // both halves of that invariant, so no instance can be both unscored and
    // valued, and none can be silently credited as flawless.
    public sealed record MetricScore
    {
        public FindingCategory Category { get; }

        public MetricScoreState State { get; }

        public long Count { get; }

        public long? Population { get; }

        public MetricPopulationKind PopulationKind { get; }

        public ScoreValue? Score { get; }

        public string? Reason { get; }

        [System.Text.Json.Serialization.JsonConstructor]
        internal MetricScore(
            FindingCategory category,
            MetricScoreState state,
            long count,
            long? population,
            MetricPopulationKind populationKind,
            ScoreValue? score,
            string? reason)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "A metric count must be non-negative.");
            }

            if (state == MetricScoreState.Scored)
            {
                if (score is null)
                {
                    throw new ArgumentException("A scored metric must carry a score.", nameof(score));
                }

                if (reason is not null)
                {
                    throw new ArgumentException("A scored metric must not carry a reason.", nameof(reason));
                }
            }
            else
            {
                if (score is not null)
                {
                    throw new ArgumentException("An unscored metric must not carry a score.", nameof(score));
                }

                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new ArgumentException("An unscored metric must carry a non-empty reason.", nameof(reason));
                }
            }

            Category = category;
            State = state;
            Count = count;
            Population = population;
            PopulationKind = populationKind;
            Score = score;
            Reason = reason;
        }

        public static MetricScore Scored(
            FindingCategory category,
            long count,
            long population,
            MetricPopulationKind populationKind,
            ScoreValue score)
        {
            if (population <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(population), "A scored metric requires a positive population.");
            }

            if (count > population)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    "A metric count cannot exceed its population; that is an internal inconsistency, not a clampable value.");
            }

            return new MetricScore(category, MetricScoreState.Scored, count, population, populationKind, score, reason: null);
        }

        public static MetricScore NotApplicable(
            FindingCategory category,
            MetricPopulationKind populationKind,
            string reason) =>
            new(category, MetricScoreState.NotApplicable, count: 0, population: null, populationKind, score: null, reason);

        public static MetricScore NotScored(
            FindingCategory category,
            MetricPopulationKind populationKind,
            string reason,
            long count = 0) =>
            new(category, MetricScoreState.NotScored, count, population: 0, populationKind, score: null, reason);
    }
}
