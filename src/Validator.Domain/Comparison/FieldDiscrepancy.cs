using System;

namespace Validator.Domain.Comparison
{
    /// <summary>
    /// A single material difference between a benchmark and candidate value at a shared timestamp.
    /// Missing and extra timestamps produce no FieldDiscrepancy — they are reported at the coverage level.
    /// </summary>
    public sealed record FieldDiscrepancy
    {
        public DateTimeOffset TimestampUtc { get; init; }
        public OhlcvField Field { get; init; }
        public decimal BenchmarkValue { get; init; }
        public decimal CandidateValue { get; init; }
        public decimal Difference { get; init; }
        public decimal DirectionalDifference { get; init; }
        public decimal ResolvedAbsoluteTolerance { get; init; }
        public decimal ResolvedRelativeTolerance { get; init; }
        public ToleranceDecision ToleranceDecision { get; init; }
        public long? CandidateSourceLine { get; init; }

        public FieldDiscrepancy(
            DateTimeOffset timestampUtc,
            OhlcvField field,
            decimal benchmarkValue,
            decimal candidateValue,
            decimal difference,
            decimal directionalDifference,
            decimal resolvedAbsoluteTolerance,
            decimal resolvedRelativeTolerance,
            ToleranceDecision toleranceDecision,
            long? candidateSourceLine = null)
        {
            if (difference < 0)
                throw new ArgumentOutOfRangeException(nameof(difference), "Difference must be non-negative.");

            TimestampUtc = timestampUtc;
            Field = field;
            BenchmarkValue = benchmarkValue;
            CandidateValue = candidateValue;
            Difference = difference;
            DirectionalDifference = directionalDifference;
            ResolvedAbsoluteTolerance = resolvedAbsoluteTolerance;
            ResolvedRelativeTolerance = resolvedRelativeTolerance;
            ToleranceDecision = toleranceDecision;
            CandidateSourceLine = candidateSourceLine;
        }
    }
}
