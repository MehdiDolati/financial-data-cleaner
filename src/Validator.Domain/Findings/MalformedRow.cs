namespace Validator.Domain.Findings
{
    public sealed record MalformedRow
    {
        public long LineNumber { get; init; }
        public string RawText { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;

        public MalformedRow(long lineNumber, string rawText, string reason)
        {
            LineNumber = lineNumber;
            RawText = rawText ?? string.Empty;
            Reason = reason ?? string.Empty;
        }
    }
}