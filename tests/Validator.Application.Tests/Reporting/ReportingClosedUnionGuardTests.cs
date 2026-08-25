using System;
using System.Reflection;
using System.Text.Json;
using Validator.Application.Reporting;
using Validator.Application.Validation;
using Validator.Domain.Findings;
using Validator.Domain.Timeframes;
using Xunit;

namespace Validator.Application.Tests.Reporting
{
    // Exercises the default/guard arms of closed unions in the reporting layer
    // so every reachable branch of the invariant-protecting switches is proven
    // by a test that exercises the out-of-range enum cast path.
    public sealed class ReportingClosedUnionGuardTests
    {
        // --- DetailedSummary.For ---

        [Fact]
        public void DetailedSummary_For_RejectsANonMetricCategory()
        {
            var summary = new DetailedSummary(1, 2, 3, 4, 5, 6);

            Assert.Throws<ArgumentOutOfRangeException>(() => summary.For(FindingCategory.Critical));
        }

        // --- FindingCatalogStatistics.For ---

        [Fact]
        public void FindingCatalogStatistics_For_RejectsANonMetricCategory()
        {
            var stats = new FindingCatalogStatistics(
                new CategoryStatistics(1, 10),
                new CategoryStatistics(2, 20),
                new CategoryStatistics(3, 30),
                new CategoryStatistics(4, 40),
                new CategoryStatistics(5, 50),
                new CategoryStatistics(6, 60));

            Assert.Throws<ArgumentOutOfRangeException>(() => stats.For(FindingCategory.Critical));
        }

        // --- FindingReferenceFactory.PhysicalRecord ---

        [Fact]
        public void FindingReferenceFactory_PhysicalRecord_RejectsANonMetricCategory()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FindingReferenceFactory.PhysicalRecord(FindingCategory.Critical, sourceLine: 1));
        }

        // --- FindingCatalog.TimeframeJsonConverter.Read ---

        private sealed class TimeframeHolder
        {
            public Timeframe? Value { get; set; }
        }

        [Fact]
        public void TimeframeJsonConverter_Read_ParsesValidTimeframe()
        {
            // Access FindingCatalog's private SerializerOptions via reflection
            var optionsField = typeof(FindingCatalog).GetField(
                "SerializerOptions", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(optionsField);
            var options = (JsonSerializerOptions)optionsField!.GetValue(null)!;

            // Round-trip a Timeframe through the converter to exercise the Read path
            var holder = JsonSerializer.Deserialize<TimeframeHolder>(
                "{\"Value\": \"H1\"}"u8, options);
            Assert.NotNull(holder);
            Assert.Equal(1, holder!.Value!.Value);
            Assert.Equal('H', holder.Value!.Unit);
        }
    }
}
