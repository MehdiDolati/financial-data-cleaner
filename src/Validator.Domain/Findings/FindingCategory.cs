namespace Validator.Domain.Findings
{
    /// <summary>
    /// The kind of problem a finding describes.
    /// </summary>
    /// <remarks>
    /// The first six values are the established validation checks, and they are
    /// declared in the order findings appear in a report. That ordering is relied
    /// upon, so these values must not be renumbered or reordered.
    /// </remarks>
    public enum FindingCategory
    {
        /// <summary>A candle the timeframe expected but the source does not contain.</summary>
        MissingCandle = 0,

        /// <summary>A record whose timestamp repeats one already present in the source.</summary>
        DuplicateRecord = 1,

        /// <summary>A record whose open, high, low, and close values contradict each other.</summary>
        InvalidOhlc = 2,

        /// <summary>A record timestamped when the market was closed.</summary>
        ClosedMarketRecord = 3,

        /// <summary>A span between consecutive records that is longer than the timeframe.</summary>
        TimeGap = 4,

        /// <summary>A source row that could not be parsed into a candle.</summary>
        MalformedRow = 5,

        // Backward-compatible severity values retained for existing consumers.
        // These are severities rather than checks, so they have no position in
        // the report ordering and are not produced by the detailed report.

        /// <summary>Severity retained for older consumers; not a validation check.</summary>
        Informational = 100,

        /// <summary>Severity retained for older consumers; not a validation check.</summary>
        Minor = 101,

        /// <summary>Severity retained for older consumers; not a validation check.</summary>
        Major = 102,

        /// <summary>Severity retained for older consumers; not a validation check.</summary>
        Critical = 103
    }
}
