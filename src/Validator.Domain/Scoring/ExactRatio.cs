using System;
using System.Numerics;

namespace Validator.Domain.Scoring
{
    // An exact rational used for every score computation. The value is reduced
    // by its greatest common divisor on construction and the sign is carried by
    // the numerator, so equal values share one representation and compare and
    // format identically. No operation rounds; rounding exists only at the
    // presentation boundary in ScoreValue. float and double appear nowhere.
    public readonly struct ExactRatio : IEquatable<ExactRatio>, IComparable<ExactRatio>
    {
        public BigInteger Numerator { get; }

        public BigInteger Denominator { get; }

        public ExactRatio(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.IsZero)
            {
                throw new ArgumentException("A ratio denominator must not be zero.", nameof(denominator));
            }

            // The sign is carried entirely by the numerator so that equal values
            // reduce to one representation regardless of how the sign was split.
            if (denominator.Sign < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            if (divisor > BigInteger.One)
            {
                numerator /= divisor;
                denominator /= divisor;
            }

            Numerator = numerator;
            Denominator = denominator;
        }

        public static ExactRatio Zero { get; } = new(BigInteger.Zero, BigInteger.One);

        public static ExactRatio One { get; } = new(BigInteger.One, BigInteger.One);

        public ExactRatio Add(ExactRatio other) => new(
            (Numerator * other.Denominator) + (other.Numerator * Denominator),
            Denominator * other.Denominator);

        public ExactRatio Multiply(ExactRatio other) => new(
            Numerator * other.Numerator,
            Denominator * other.Denominator);

        public ExactRatio Divide(ExactRatio other)
        {
            if (other.Numerator.IsZero)
            {
                throw new DivideByZeroException("A ratio cannot be divided by zero.");
            }

            return new ExactRatio(
                Numerator * other.Denominator,
                Denominator * other.Numerator);
        }

        public int CompareTo(ExactRatio other)
        {
            // Cross-multiplication stays exact because both denominators are
            // positive after normalisation, so the inequality direction holds.
            var left = Numerator * other.Denominator;
            var right = other.Numerator * Denominator;
            return left.CompareTo(right);
        }

        public bool Equals(ExactRatio other) =>
            Numerator == other.Numerator && Denominator == other.Denominator;

        public override bool Equals(object? obj) => obj is ExactRatio other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

        public override string ToString() => $"{Numerator}/{Denominator}";
    }
}
