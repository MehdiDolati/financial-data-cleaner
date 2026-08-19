using Validator.Application.Ingestion;
using Validator.Application.Scoring;
using Xunit;

namespace Validator.Application.Tests.Scoring
{
    // MetricPopulations carries the three denominators one scored run measures
    // against. Row populations are copied verbatim from the established scan
    // coverage; the expected open-market candle count is supplied from the
    // sequence walk and is null exactly when the sequence checks did not run.
    public sealed class MetricPopulationsTests
    {
        private static ScanCoverage Coverage(long examined, long accepted, long malformed) =>
            new(examined, accepted, malformed);

        [Fact]
        public void FromScanCoverage_CopiesAcceptedAndExaminedRowsVerbatim()
        {
            var populations = MetricPopulations.FromScanCoverage(Coverage(6, 5, 1), expectedCandles: 5);

            Assert.Equal(5, populations.AcceptedRows);
            Assert.Equal(6, populations.ExaminedRows);
        }

        [Fact]
        public void FromScanCoverage_WithExpectedCandles_RetainsTheWalkCount()
        {
            var populations = MetricPopulations.FromScanCoverage(Coverage(6, 5, 1), expectedCandles: 84);

            Assert.Equal(84, populations.ExpectedCandles);
        }

        [Fact]
        public void FromScanCoverage_WhenSequenceChecksDidNotRun_LeavesExpectedCandlesNull()
        {
            var populations = MetricPopulations.FromScanCoverage(Coverage(1, 1, 0), expectedCandles: null);

            Assert.Null(populations.ExpectedCandles);
        }

        [Fact]
        public void ForKind_ReturnsTheMatchingPopulation()
        {
            var populations = MetricPopulations.FromScanCoverage(Coverage(6, 5, 1), expectedCandles: 5);

            Assert.Equal(5, populations.For(MetricPopulationKind.ExpectedCandles));
            Assert.Equal(5, populations.For(MetricPopulationKind.AcceptedRows));
            Assert.Equal(6, populations.For(MetricPopulationKind.ExaminedRows));
        }

        [Fact]
        public void ForExpectedCandles_WhenNotRun_IsNull()
        {
            var populations = MetricPopulations.FromScanCoverage(Coverage(1, 1, 0), expectedCandles: null);

            Assert.Null(populations.For(MetricPopulationKind.ExpectedCandles));
            Assert.Equal(1, populations.For(MetricPopulationKind.AcceptedRows));
        }
    }
}
