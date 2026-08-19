using System;

namespace Validator.Domain.Comparison
{
    /// <summary>
    /// Configuration for a single OHLCV field in a comparison, including user overrides and resolved tolerances.
    /// </summary>
    public sealed record ComparedField
    {
        public OhlcvField Field { get; init; }
        public bool Enabled { get; init; }
        public decimal? AbsoluteTolerance { get; init; }
        public decimal? RelativeTolerance { get; init; }
        public decimal ResolvedAbsolute { get; init; }
        public decimal ResolvedRelative { get; init; }

        public ComparedField(
            OhlcvField field,
            bool enabled,
            decimal? absoluteTolerance,
            decimal? relativeTolerance,
            decimal resolvedAbsolute,
            decimal resolvedRelative)
        {
            if (absoluteTolerance is < 0)
                throw new ArgumentOutOfRangeException(nameof(absoluteTolerance), "Must be non-negative when specified.");
            if (relativeTolerance is < 0)
                throw new ArgumentOutOfRangeException(nameof(relativeTolerance), "Must be non-negative when specified.");
            if (resolvedAbsolute < 0)
                throw new ArgumentOutOfRangeException(nameof(resolvedAbsolute), "Must be non-negative.");
            if (resolvedRelative < 0)
                throw new ArgumentOutOfRangeException(nameof(resolvedRelative), "Must be non-negative.");

            Field = field;
            Enabled = enabled;
            AbsoluteTolerance = absoluteTolerance;
            RelativeTolerance = relativeTolerance;
            ResolvedAbsolute = resolvedAbsolute;
            ResolvedRelative = resolvedRelative;
        }
    }
}
