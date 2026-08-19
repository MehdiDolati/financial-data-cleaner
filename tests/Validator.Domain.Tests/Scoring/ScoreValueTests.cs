using System;
using Validator.Domain.Scoring;
using Xunit;

namespace Validator.Domain.Tests.Scoring
{
    // ScoreValue is the presentation form of a score: it keeps the unrounded
    // exact ratio for further arithmetic and exposes a two-decimal,
    // half-away-from-zero, culture-invariant rendering with trailing zeros.
    public sealed class ScoreValueTests
    {
        [Fact]
        public void Exact_IsRetainedUnrounded()
        {
            var value = new ScoreValue(new ExactRatio(200, 3));

            Assert.Equal(new ExactRatio(200, 3), value.Exact);
        }

        [Theory]
        [InlineData(200, 3, "66.67")]   // 66.666... rounds up
        [InlineData(100, 1, "100.00")]  // trailing zeros are kept
        [InlineData(0, 1, "0.00")]      // zero keeps two decimals
        [InlineData(8300, 84, "98.81")] // 98.809... rounds up
        [InlineData(8200, 84, "97.62")] // 97.619... rounds up
        [InlineData(9840470, 100000, "98.40")]
        public void Rounded_IsTwoDecimalsHalfAwayFromZero(long numerator, long denominator, string expected)
        {
            var value = new ScoreValue(new ExactRatio(numerator, denominator));

            Assert.Equal(expected, value.Format());
        }

        [Fact]
        public void Rounded_AtAMidpoint_RoundsAwayFromZero()
        {
            // 98.405 is an exact midpoint; half away from zero gives 98.41.
            var value = new ScoreValue(new ExactRatio(98405, 1000));

            Assert.Equal(98.41m, value.Rounded);
            Assert.Equal("98.41", value.Format());
        }

        [Fact]
        public void Format_UsesAPeriodDecimalSeparatorNotTheCurrentCulture()
        {
            // The whole platform runs in globalization-invariant mode, so the
            // decimal separator can never be a comma; the formatting must emit a
            // period and two fixed decimals independent of any host locale.
            var value = new ScoreValue(new ExactRatio(9840470, 100000));

            Assert.Equal("98.40", value.Format());
            Assert.DoesNotContain(",", value.Format());
        }


        [Theory]
        [InlineData(-1, 1)]
        [InlineData(10001, 100)]
        public void Construction_OutsideZeroToOneHundred_Throws(long numerator, long denominator)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoreValue(new ExactRatio(numerator, denominator)));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(10000, 100)]
        public void Construction_AtTheBoundaries_IsAccepted(long numerator, long denominator)
        {
            var value = new ScoreValue(new ExactRatio(numerator, denominator));

            Assert.Equal(new ExactRatio(numerator, denominator), value.Exact);
        }

        [Fact]
        public void Equals_Object_DistinguishesEqualUnequalAndNonScore()
        {
            object equal = new ScoreValue(new ExactRatio(9800, 100));
            object unequal = new ScoreValue(new ExactRatio(9700, 100));
            object notAScore = "98.00";

            Assert.True(new ScoreValue(new ExactRatio(98, 1)).Equals(equal));
            Assert.False(new ScoreValue(new ExactRatio(98, 1)).Equals(unequal));
            Assert.False(new ScoreValue(new ExactRatio(98, 1)).Equals(notAScore));
            Assert.False(new ScoreValue(new ExactRatio(98, 1)).Equals(null));
        }

        [Fact]
        public void GetHashCode_IsEqualForEqualScores()
        {
            Assert.Equal(
                new ScoreValue(new ExactRatio(98, 1)).GetHashCode(),
                new ScoreValue(new ExactRatio(9800, 100)).GetHashCode());
        }

        [Fact]
        public void ToString_IsTheTwoDecimalFormattedValue()
        {
            Assert.Equal("66.67", new ScoreValue(new ExactRatio(200, 3)).ToString());
        }

        [Fact]
        public void Zero_FormatsWithoutASignAndRoundsCleanly()
        {
            // The zero boundary exercises the non-negative formatting and rounding
            // paths, which are the only paths a valid 0..100 score can reach.
            var value = new ScoreValue(ExactRatio.Zero);

            Assert.Equal("0.00", value.Format());
            Assert.Equal(0m, value.Rounded);
        }
    }
}


