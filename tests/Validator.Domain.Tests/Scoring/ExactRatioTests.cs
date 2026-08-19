using System;
using System.Numerics;
using Validator.Domain.Scoring;
using Xunit;

namespace Validator.Domain.Tests.Scoring
{
    // ExactRatio is the exact-rational backbone of every score. These theories
    // pin GCD normalisation, sign normalisation, the arithmetic used to build a
    // score and an average, exact comparison, and the refusal to divide by zero.
    public sealed class ExactRatioTests
    {
        [Theory]
        [InlineData(2, 4, 1, 2)]
        [InlineData(50, 100, 1, 2)]
        [InlineData(-2, 4, -1, 2)]
        [InlineData(2, -4, -1, 2)]
        [InlineData(-2, -4, 1, 2)]
        [InlineData(0, 5, 0, 1)]
        [InlineData(6, 3, 2, 1)]
        public void Construction_ReducesByGcdAndNormalisesSign(long numerator, long denominator, long expectedNumerator, long expectedDenominator)
        {
            var ratio = new ExactRatio(numerator, denominator);

            Assert.Equal(new BigInteger(expectedNumerator), ratio.Numerator);
            Assert.Equal(new BigInteger(expectedDenominator), ratio.Denominator);
        }

        [Fact]
        public void Construction_WithZeroDenominator_Throws()
        {
            Assert.Throws<ArgumentException>(() => new ExactRatio(1, 0));
        }

        [Fact]
        public void EqualValues_HaveOneRepresentationAndCompareEqual()
        {
            var a = new ExactRatio(1, 2);
            var b = new ExactRatio(3, 6);

            Assert.Equal(a, b);
            Assert.Equal(0, a.CompareTo(b));
        }

        [Fact]
        public void Add_ProducesExactSum()
        {
            var sum = new ExactRatio(1, 3).Add(new ExactRatio(1, 6));

            Assert.Equal(new ExactRatio(1, 2), sum);
        }

        [Fact]
        public void Multiply_ProducesExactProduct()
        {
            var product = new ExactRatio(2, 3).Multiply(new ExactRatio(3, 4));

            Assert.Equal(new ExactRatio(1, 2), product);
        }

        [Fact]
        public void Divide_ProducesExactQuotient()
        {
            var quotient = new ExactRatio(1, 2).Divide(new ExactRatio(1, 4));

            Assert.Equal(new ExactRatio(2, 1), quotient);
        }

        [Fact]
        public void Divide_ByZero_Throws()
        {
            Assert.Throws<DivideByZeroException>(() => new ExactRatio(1, 2).Divide(ExactRatio.Zero));
        }

        [Theory]
        [InlineData(1, 3, 1, 2, -1)]
        [InlineData(1, 2, 1, 3, 1)]
        [InlineData(2, 4, 1, 2, 0)]
        public void Compare_IsExactWithoutWidening(long leftNum, long leftDen, long rightNum, long rightDen, int expectedSign)
        {
            var comparison = new ExactRatio(leftNum, leftDen).CompareTo(new ExactRatio(rightNum, rightDen));

            Assert.Equal(expectedSign, Math.Sign(comparison));
        }

        [Fact]
        public void Score_100TimesOneMinusOneThird_IsExactlyTwoHundredOverThree()
        {
            // 100 x (1 - 1/3) = 200/3, a value no decimal can hold exactly.
            var score = new ExactRatio(100, 1)
                .Multiply(ExactRatio.One.Add(new ExactRatio(1, 3).Multiply(new ExactRatio(-1, 1))));

            Assert.Equal(new ExactRatio(200, 3), score);
        }

        [Fact]
        public void Equals_Object_DistinguishesEqualUnequalAndNonRatio()
        {
            object equal = new ExactRatio(1, 2);
            object unequal = new ExactRatio(1, 3);
            object notARatio = "1/2";

            Assert.True(new ExactRatio(1, 2).Equals(equal));
            Assert.False(new ExactRatio(1, 2).Equals(unequal));
            Assert.False(new ExactRatio(1, 2).Equals(notARatio));
            Assert.False(new ExactRatio(1, 2).Equals(null));
        }

        [Fact]
        public void GetHashCode_IsEqualForEqualValues()
        {
            Assert.Equal(
                new ExactRatio(1, 2).GetHashCode(),
                new ExactRatio(3, 6).GetHashCode());
        }

        [Fact]
        public void ToString_ShowsTheReducedNumeratorOverDenominator()
        {
            Assert.Equal("1/2", new ExactRatio(3, 6).ToString());
        }
    }
}


