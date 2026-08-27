using System;
using Validator.Domain.Comparison;
using Xunit;

namespace Validator.Domain.Tests.Comparison
{
    public class ComparedFieldTests
    {
        [Fact]
        public void Constructor_WithValidParameters_Succeeds()
        {
            var field = new ComparedField(
                OhlcvField.Open,
                enabled: true,
                absoluteTolerance: 0.00010m,
                relativeTolerance: 0.0001m,
                resolvedAbsolute: 0.00010m,
                resolvedRelative: 0.0001m);

            Assert.Equal(OhlcvField.Open, field.Field);
            Assert.True(field.Enabled);
            Assert.Equal(0.00010m, field.AbsoluteTolerance);
            Assert.Equal(0.0001m, field.RelativeTolerance);
            Assert.Equal(0.00010m, field.ResolvedAbsolute);
            Assert.Equal(0.0001m, field.ResolvedRelative);
        }

        [Fact]
        public void Constructor_WithNullTolerances_Succeeds()
        {
            var field = new ComparedField(
                OhlcvField.Volume,
                enabled: true,
                absoluteTolerance: null,
                relativeTolerance: null,
                resolvedAbsolute: 0m,
                resolvedRelative: 0.05m);

            Assert.Null(field.AbsoluteTolerance);
            Assert.Null(field.RelativeTolerance);
        }

        [Fact]
        public void Constructor_WithDisabledField_Succeeds()
        {
            var field = new ComparedField(
                OhlcvField.High,
                enabled: false,
                absoluteTolerance: null,
                relativeTolerance: null,
                resolvedAbsolute: 0m,
                resolvedRelative: 0m);

            Assert.False(field.Enabled);
        }

        [Fact]
        public void Constructor_WithNegativeAbsoluteTolerance_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ComparedField(
                OhlcvField.Open, true, -0.001m, null, 0.00010m, 0.0001m));
        }

        [Fact]
        public void Constructor_WithNegativeRelativeTolerance_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ComparedField(
                OhlcvField.Open, true, null, -0.001m, 0.00010m, 0.0001m));
        }

        [Fact]
        public void Constructor_WithNegativeResolvedAbsolute_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ComparedField(
                OhlcvField.Open, true, null, null, -0.001m, 0.0001m));
        }

        [Fact]
        public void Constructor_WithNegativeResolvedRelative_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ComparedField(
                OhlcvField.Open, true, null, null, 0.00010m, -0.001m));
        }

        [Fact]
        public void Constructor_WithZeroTolerances_Succeeds()
        {
            var field = new ComparedField(
                OhlcvField.Open, true, 0m, 0m, 0m, 0m);

            Assert.Equal(0m, field.AbsoluteTolerance);
            Assert.Equal(0m, field.RelativeTolerance);
        }

        [Fact]
        public void AllFields_AreAccessible()
        {
            var fields = new[] { OhlcvField.Open, OhlcvField.High, OhlcvField.Low, OhlcvField.Close, OhlcvField.Volume };
            foreach (var field in fields)
            {
                var compared = new ComparedField(field, true, null, null, 0.00010m, 0.0001m);
                Assert.Equal(field, compared.Field);
            }
        }
    }
}
