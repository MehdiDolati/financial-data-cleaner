namespace Validator.Application.Scoring
{
    // The denominator a metric's defect rate is measured against. The mapping
    // from each of the six established metrics to one of these kinds is fixed in
    // MetricPopulationMap and is the sole authority for that choice.
    public enum MetricPopulationKind
    {
        // Expected open-market candles in the evaluated range; used by the two
        // time-based metrics (missing candles and time gaps).
        ExpectedCandles = 0,

        // Rows that parsed into candles; used by the record-level metrics
        // (duplicates, invalid OHLC, closed-market records).
        AcceptedRows = 1,

        // Every physical row examined; used by the malformed-row metric.
        ExaminedRows = 2
    }
}
