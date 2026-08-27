using System;
using Validator.Domain.Comparison;
using Xunit;

namespace Validator.Domain.Tests.Comparison
{
    public class ToleratedDifferenceAggregateTests
    {
        [Fact]
        public void Constructor_WithValidCounts_Succeeds()
        {
            var aggregate = new ToleratedDifferenceAggregate(
                field: OhlcvField.Open,
                totalCompared: 100,
                acceptedCount: 95,
                acceptedByAbsoluteCount: 50,
                acceptedByRelativeCount: 45,
                materialCount: 5);

            Assert.Equal(OhlcvField.Open, aggregate.Field);
            Assert.Equal(100, aggregate.TotalCompared);
            Assert.Equal(95, aggregate.AcceptedCount);
            Assert.Equal(50, aggregate.AcceptedByAbsoluteCount);
            Assert.Equal(45, aggregate.AcceptedByRelativeCount);
            Assert.Equal(5, aggregate.MaterialCount);
        }

        [Fact]
        public void Constructor_WithNegativeTotalCompared_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ToleratedDifferenceAggregate(
                OhlcvField.Open, -1, 0, 0, 0, 0));
        }

        [Fact]
        public void Constructor_WithNegativeAcceptedCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ToleratedDifferenceAggregate(
                OhlcvField.Open, 100, -1, 0, 0, 0));
        }

        [Fact]
        public void Constructor_WithNegativeAcceptedByAbsoluteCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ToleratedDifferenceAggregate(
                OhlcvField.Open, 100, 0, -1, 0, 0));
        }

        [Fact]
        public void Constructor_WithNegativeAcceptedByRelativeCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ToleratedDifferenceAggregate(
                OhlcvField.Open, 100, 0, 0, -1, 0));
        }

        [Fact]
        public void Constructor_WithNegativeMaterialCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ToleratedDifferenceAggregate(
                OhlcvField.Open, 100, 0, 0, 0, -1));
        }

        [Fact]
        public void Constructor_WithZeroCounts_Succeeds()
        {
            var aggregate = new ToleratedDifferenceAggregate(
                OhlcvField.Volume, 0, 0, 0, 0, 0);

            Assert.Equal(OhlcvField.Volume, aggregate.Field);
            Assert.Equal(0, aggregate.TotalCompared);
        }

        [Fact]
        public void AllFields_AreAccessible()
        {
            var fields = new[] { OhlcvField.Open, OhlcvField.High, OhlcvField.Low, OhlcvField.Close, OhlcvField.Volume };
            foreach (var field in fields)
            {
                var aggregate = new ToleratedDifferenceAggregate(field, 10, 8, 5, 3, 2);
                Assert.Equal(field, aggregate.Field);
            }
        }
    }
}
