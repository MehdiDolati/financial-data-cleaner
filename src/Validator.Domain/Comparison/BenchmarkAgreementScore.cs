using System;
using Validator.Domain.Scoring;

namespace Validator.Domain.Comparison
{
    /// <summary>
    /// The benchmark-relative agreement result, kept separate from the candidate's independent quality score.
    /// Score is null if and only if UnavailableReason is non-null (no overlapping timestamps).
    /// </summary>
    public sealed record BenchmarkAgreementScore
    {
        public ScoreValue? Score { get; init; }
        public string Formula { get; init; }
        public long MatchedPopulation { get; init; }
        public long MaterialDiscrepancyCount { get; init; }
        public string? UnavailableReason { get; init; }

        public BenchmarkAgreementScore(
            ScoreValue? score,
            string formula,
            long matchedPopulation,
            long materialDiscrepancyCount,
            string? unavailableReason)
        {
            if (score is null && unavailableReason is null)
                throw new ArgumentException("An unavailable score must carry a reason.");
            if (score is not null && unavailableReason is not null)
                throw new ArgumentException("An available score must not carry an unavailability reason.");
            if (score is not null && matchedPopulation <= 0)
                throw new ArgumentOutOfRangeException(nameof(matchedPopulation), "A scored agreement requires a positive matched population.");

            Score = score;
            Formula = formula;
            MatchedPopulation = matchedPopulation;
            MaterialDiscrepancyCount = materialDiscrepancyCount;
            UnavailableReason = unavailableReason;
        }

        /// <summary>
        /// Creates an available agreement score from comparison results.
        /// </summary>
        public static BenchmarkAgreementScore Available(long matchedPopulation, long materialDiscrepancyTimestamps)
        {
            if (matchedPopulation <= 0)
                throw new ArgumentOutOfRangeException(nameof(matchedPopulation), "Must be positive for an available score.");

            var formula = "100 × (matchedPopulation - materialDiscrepancyTimestamps) / matchedPopulation";
            var numerator = matchedPopulation - materialDiscrepancyTimestamps;
            var ratio = new ExactRatio(numerator, matchedPopulation);
            var score = new ScoreValue(ratio.Multiply(new ExactRatio(100, 1)));

            return new BenchmarkAgreementScore(score, formula, matchedPopulation, materialDiscrepancyTimestamps, null);
        }

        /// <summary>
        /// Creates an unavailable agreement score with a reason.
        /// </summary>
        public static BenchmarkAgreementScore Unavailable(string reason, long matchedPopulation = 0, long materialDiscrepancyTimestamps = 0)
        {
            var formula = "100 × (matchedPopulation - materialDiscrepancyTimestamps) / matchedPopulation";
            return new BenchmarkAgreementScore(null, formula, matchedPopulation, materialDiscrepancyTimestamps, reason);
        }
    }
}
