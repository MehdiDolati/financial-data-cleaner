namespace Validator.Domain.Findings.Evidence
{
    // Category-specific evidence shape carried by a detailed finding.
    // The evidence kind must correspond to the established finding category.
    public enum EvidenceKind
    {
        MissingCandle = 0,
        DuplicateRecord = 1,
        InvalidOhlc = 2,
        ClosedMarketRecord = 3,
        TimeGap = 4,
        MalformedRow = 5
    }
}