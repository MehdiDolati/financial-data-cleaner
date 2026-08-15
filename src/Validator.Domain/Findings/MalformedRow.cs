namespace Validator.Domain.Findings
{
    public sealed record MalformedRow
    {
        public long LineNumber { get; init; }
        public string RawText { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public DateTimeOffset? ParsedTimestampUtc { get; init; }

        public MalformedRow(long lineNumber, string rawText, string reason)
            : this(lineNumber, rawText, reason, null)
        {
        }

        public MalformedRow(
            long lineNumber,
            string rawText,
            string reason,
            DateTimeOffset? parsedTimestampUtc)
        {
            if (lineNumber <= 0)
                throw new ArgumentOutOfRangeException(nameof(lineNumber));

            if (parsedTimestampUtc.HasValue && parsedTimestampUtc.Value.Offset != TimeSpan.Zero)
                throw new ArgumentException("Parsed timestamp must be UTC.", nameof(parsedTimestampUtc));

            LineNumber = lineNumber;
            RawText = rawText ?? string.Empty;
            Reason = reason ?? string.Empty;
            ParsedTimestampUtc = parsedTimestampUtc;
        }
    }
}