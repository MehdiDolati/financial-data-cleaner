using System;
using Validator.Application.Ingestion;

namespace Validator.Application.Scoring
{
    // The three population values for one run, resolved once and shared by all
    // six metrics. Row populations are copied from the established scan coverage;
    // the expected open-market candle count comes from the sequence walk the
    // orchestrator already performs, and is null when that walk did not run.
    public sealed record MetricPopulations
    {
        public long? ExpectedCandles { get; }

        public long AcceptedRows { get; }

        public long ExaminedRows { get; }

        private MetricPopulations(long? expectedCandles, long acceptedRows, long examinedRows)
        {
            if (expectedCandles < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedCandles), "Expected candles must be non-negative.");
            }

            ExpectedCandles = expectedCandles;
            AcceptedRows = acceptedRows;
            ExaminedRows = examinedRows;
        }

        // Values are copied from the established run, never recomputed. Because
        // the expected-candle count is produced by the same sequence walk that
        // reported the missing candles, it cannot disagree with that count.
        public static MetricPopulations FromScanCoverage(ScanCoverage coverage, long? expectedCandles)
        {
            ArgumentNullException.ThrowIfNull(coverage);

            return new MetricPopulations(
                expectedCandles,
                coverage.AcceptedRows,
                coverage.PhysicalRowsExamined);
        }

        // The population for one kind, or null when the kind is ExpectedCandles
        // and the sequence checks did not run.
        public long? For(MetricPopulationKind kind) => kind switch
        {
            MetricPopulationKind.ExpectedCandles => ExpectedCandles,
            MetricPopulationKind.AcceptedRows => AcceptedRows,
            MetricPopulationKind.ExaminedRows => ExaminedRows,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
