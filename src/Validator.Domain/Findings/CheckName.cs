namespace Validator.Domain.Findings
{
    // The six established validation checks in canonical order.
    public enum CheckName
    {
        MissingCandles = 0,
        DuplicateRecords = 1,
        InvalidOhlc = 2,
        ClosedMarketRecords = 3,
        TimeGaps = 4,
        MalformedRows = 5
    }
}