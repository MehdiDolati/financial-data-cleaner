using System;
using System.Globalization;
using System.Numerics;

namespace Validator.Domain.Scoring
{
    // The presentation form of a score on the 0-to-100 scale. The unrounded
    // exact ratio is retained so an average is always computed from unrounded
    // inputs; the rounded value exists only for display and is produced once,
    // to two decimals, half away from zero, culture-invariantly.
    public readonly struct ScoreValue : IEquatable<ScoreValue>
    {
        private static readonly ExactRatio Hundred = new(100, 1);
        private readonly BigInteger _hundredths;

        public ExactRatio Exact { get; }

        public ScoreValue(ExactRatio exact)
        {
            if (exact.CompareTo(ExactRatio.Zero) < 0 || exact.CompareTo(Hundred) > 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exact),
                    "A score must lie within the inclusive range 0..100.");
            }

            Exact = exact;
            _hundredths = RoundToHundredths(exact);
        }

        // The two-decimal value for presentation. Callers that need arithmetic
        // must use Exact; this exists only so a formatted line and a machine
        // field agree on the same rounded number.
        public decimal Rounded => (decimal)_hundredths / 100m;

        // Always exactly two decimal places, including trailing zeros, using the
        // invariant culture so the point is a period on every host. A score is
        // constrained to 0..100, so no sign handling is required.
        public string Format()
        {
            var whole = _hundredths / 100;
            var fraction = _hundredths % 100;
            return string.Concat(
                whole.ToString(CultureInfo.InvariantCulture),
                ".",
                fraction.ToString("D2", CultureInfo.InvariantCulture));
        }

        // Rounds a non-negative numerator/denominator to hundredths, half away
        // from zero. Both the quotient and remainder are exact BigInteger
        // operations, so the midpoint decision never depends on a binary floating
        // approximation. The value is always in 0..100, so it is never negative.
        private static BigInteger RoundToHundredths(ExactRatio value)
        {
            var scaledNumerator = value.Numerator * 100;
            var denominator = value.Denominator;
            var quotient = BigInteger.DivRem(scaledNumerator, denominator, out var remainder);
            if (remainder * 2 >= denominator)
            {
                quotient += BigInteger.One;
            }

            return quotient;
        }


        public bool Equals(ScoreValue other) => Exact.Equals(other.Exact);

        public override bool Equals(object? obj) => obj is ScoreValue other && Equals(other);

        public override int GetHashCode() => Exact.GetHashCode();

        public override string ToString() => Format();
    }
}
