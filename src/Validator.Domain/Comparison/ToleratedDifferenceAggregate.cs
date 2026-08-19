using System;

namespace Validator.Domain.Comparison
{
    /// <summary>
    /// Aggregate count of differences that were within tolerance (not material) for a single OHLCV field.
    /// </summary>
    public sealed record ToleratedDifferenceAggregate
    {
        public OhlcvField Field { get; init; }
        public long TotalCompared { get; init; }
        public long AcceptedCount { get; init; }
        public long AcceptedByAbsoluteCount { get; init; }
        public long AcceptedByRelativeCount { get; init; }
        public long MaterialCount { get; init; }

        public ToleratedDifferenceAggregate(
            OhlcvField field,
            long totalCompared,
            long acceptedCount,
            long acceptedByAbsoluteCount,
            long acceptedByRelativeCount,
            long materialCount)
        {
            if (totalCompared < 0)
                throw new ArgumentOutOfRangeException(nameof(totalCompared), "Must be non-negative.");
            if (acceptedCount < 0)
                throw new ArgumentOutOfRangeException(nameof(acceptedCount), "Must be non-negative.");
            if (acceptedByAbsoluteCount < 0)
                throw new ArgumentOutOfRangeException(nameof(acceptedByAbsoluteCount), "Must be non-negative.");
            if (acceptedByRelativeCount < 0)
                throw new ArgumentOutOfRangeException(nameof(acceptedByRelativeCount), "Must be non-negative.");
            if (materialCount < 0)
                throw new ArgumentOutOfRangeException(nameof(materialCount), "Must be non-negative.");

            Field = field;
            TotalCompared = totalCompared;
            AcceptedCount = acceptedCount;
            AcceptedByAbsoluteCount = acceptedByAbsoluteCount;
            AcceptedByRelativeCount = acceptedByRelativeCount;
            MaterialCount = materialCount;
        }
    }
}
