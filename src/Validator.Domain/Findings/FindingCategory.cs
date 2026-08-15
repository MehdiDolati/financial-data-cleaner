namespace Validator.Domain.Findings
{
    public enum FindingCategory
    {
        MissingCandle = 0,
        DuplicateRecord = 1,
        InvalidOhlc = 2,
        ClosedMarketRecord = 3,
        TimeGap = 4,
        MalformedRow = 5,

        // Backward-compatible severity values retained for existing consumers.
        Informational = 100,
        Minor = 101,
        Major = 102,
        Critical = 103
    }
}