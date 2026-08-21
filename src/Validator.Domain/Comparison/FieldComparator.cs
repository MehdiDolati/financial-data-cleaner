namespace Validator.Domain.Comparison
{
    /// <summary>
    /// Pure function: compares two decimal values against resolved tolerances.
    /// A difference is accepted when it falls within either the absolute or
    /// relative tolerance (OR logic, FR-017). Deterministic and culture-invariant (FR-018).
    /// </summary>
    public static class FieldComparator
    {
        /// <summary>
        /// Compares a benchmark value against a candidate value using the resolved tolerances.
        /// </summary>
        /// <param name="benchmarkValue">The reference value from the benchmark.</param>
        /// <param name="candidateValue">The value from the candidate dataset.</param>
        /// <param name="resolvedAbsolute">The resolved absolute tolerance.</param>
        /// <param name="resolvedRelative">The resolved relative tolerance (as a fraction, e.g. 0.0001 for 0.01%).</param>
        /// <returns>A ToleranceDecision indicating how the difference was classified.</returns>
        public static ToleranceDecision Compare(
            decimal benchmarkValue,
            decimal candidateValue,
            decimal resolvedAbsolute,
            decimal resolvedRelative)
        {
            if (resolvedAbsolute < 0)
                throw new ArgumentOutOfRangeException(nameof(resolvedAbsolute), "Absolute tolerance must be non-negative.");
            if (resolvedRelative < 0)
                throw new ArgumentOutOfRangeException(nameof(resolvedRelative), "Relative tolerance must be non-negative.");

            var difference = benchmarkValue - candidateValue;
            var absoluteDifference = difference < 0 ? -difference : difference;

            // Check absolute tolerance first
            if (absoluteDifference <= resolvedAbsolute)
                return new ToleranceDecision.AcceptedByAbsolute();

            // Check relative tolerance: threshold = relative * |benchmarkValue|
            // For zero benchmark values, only absolute tolerance applies
            if (benchmarkValue != 0)
            {
                var relativeThreshold = resolvedRelative * (benchmarkValue < 0 ? -benchmarkValue : benchmarkValue);
                if (absoluteDifference <= relativeThreshold)
                    return new ToleranceDecision.AcceptedByRelative();
            }

            return new ToleranceDecision.MaterialDifference();
        }

        /// <summary>
        /// Creates a FieldDiscrepancy for a material difference at a given timestamp.
        /// </summary>
        public static FieldDiscrepancy CreateDiscrepancy(
            DateTimeOffset timestampUtc,
            OhlcvField field,
            decimal benchmarkValue,
            decimal candidateValue,
            decimal resolvedAbsolute,
            decimal resolvedRelative,
            long? candidateSourceLine = null)
        {
            var difference = benchmarkValue - candidateValue;
            var absoluteDifference = difference < 0 ? -difference : difference;
            var directionalDifference = candidateValue - benchmarkValue;
            var decision = Compare(benchmarkValue, candidateValue, resolvedAbsolute, resolvedRelative);

            return new FieldDiscrepancy(
                timestampUtc,
                field,
                benchmarkValue,
                candidateValue,
                absoluteDifference,
                directionalDifference,
                resolvedAbsolute,
                resolvedRelative,
                decision,
                candidateSourceLine);
        }
    }
}
