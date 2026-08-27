using Validator.Application.Scoring;
using Validator.Domain.Findings;
using Xunit;

namespace Validator.Application.Tests.Scoring
{
    // The metric-to-population-kind mapping is the sole authority for which
    // denominator each metric is measured against. It is fixed and must never
    // drift: time-based metrics use expected candles, record-level metrics use
    // accepted rows, and the malformed metric uses examined rows.
    public sealed class MetricPopulationMappingTests
    {
        [Theory]
        [InlineData(FindingCategory.MissingCandle, MetricPopulationKind.ExpectedCandles)]
        [InlineData(FindingCategory.TimeGap, MetricPopulationKind.ExpectedCandles)]
        [InlineData(FindingCategory.DuplicateRecord, MetricPopulationKind.AcceptedRows)]
        [InlineData(FindingCategory.InvalidOhlc, MetricPopulationKind.AcceptedRows)]
        [InlineData(FindingCategory.ClosedMarketRecord, MetricPopulationKind.AcceptedRows)]
        [InlineData(FindingCategory.MalformedRow, MetricPopulationKind.ExaminedRows)]
        public void ForCategory_MapsToTheFixedPopulationKind(FindingCategory category, MetricPopulationKind expected)
        {
            Assert.Equal(expected, MetricPopulationMap.KindFor(category));
        }
    }
}
