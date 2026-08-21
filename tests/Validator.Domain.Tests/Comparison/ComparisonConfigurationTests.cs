using System;
using Validator.Domain.Comparison;
using Xunit;

namespace Validator.Domain.Tests.Comparison
{
    public class ComparisonConfigurationTests
    {
        [Fact]
        public void Constructor_WithValidConfig_Succeeds()
        {
            var fields = new[]
            {
                new ComparedField(OhlcvField.Open, true, null, null, 0.00010m, 0.0001m),
                new ComparedField(OhlcvField.High, true, null, null, 0.00010m, 0.0001m),
                new ComparedField(OhlcvField.Low, true, null, null, 0.00010m, 0.0001m),
                new ComparedField(OhlcvField.Close, true, null, null, 0.00010m, 0.0001m),
                new ComparedField(OhlcvField.Volume, true, null, null, 0m, 0.05m)
            };

            var config = new ComparisonConfiguration("audusd-daily", fields, TimestampMode.Exact);

            Assert.Equal("audusd-daily", config.BenchmarkName);
            Assert.Equal(5, config.Fields.Count);
            Assert.Equal(TimestampMode.Exact, config.TimestampMode);
        }

        [Fact]
        public void Constructor_WithEmptyName_Throws()
        {
            var fields = new[] { new ComparedField(OhlcvField.Open, true, null, null, 0.00010m, 0.0001m) };

            var ex = Assert.Throws<ArgumentException>(() => new ComparisonConfiguration("", fields, TimestampMode.Exact));

            Assert.Contains("Benchmark name must not be empty", ex.Message);
        }

        [Fact]
        public void Constructor_WithDuplicateFields_Throws()
        {
            var fields = new[]
            {
                new ComparedField(OhlcvField.Open, true, null, null, 0.00010m, 0.0001m),
                new ComparedField(OhlcvField.Open, true, null, null, 0.00010m, 0.0001m)
            };

            var ex = Assert.Throws<ArgumentException>(() => new ComparisonConfiguration("test", fields, TimestampMode.Exact));

            Assert.Contains("Duplicate fields", ex.Message);
        }

        [Fact]
        public void Constructor_WithEmptyFields_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => new ComparisonConfiguration("test", Array.Empty<ComparedField>(), TimestampMode.Exact));

            Assert.Contains("At least one field must be configured", ex.Message);
        }
    }
}
