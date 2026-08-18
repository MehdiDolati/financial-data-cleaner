using System;

namespace Validator.Domain.Findings.Evidence
{
    // Evidence for one malformed row. Field errors and checks that could not be
    // applied are streamed separately; the row contributes exactly one count.
    public sealed record MalformedRowEvidence
    {
        public long SourceLine { get; }
        public DateTimeOffset? ParsedTimestampUtc { get; }
        public string? OriginalTimestampText { get; }
        public bool ExpectedSlotReserved { get; }

        public MalformedRowEvidence(
            long sourceLine,
            DateTimeOffset? parsedTimestampUtc = null,
            string? originalTimestampText = null,
            bool expectedSlotReserved = false)
        {
            if (sourceLine <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceLine), "Source line must be positive.");
            }

            if (parsedTimestampUtc.HasValue && parsedTimestampUtc.Value.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Parsed timestamp must be UTC.", nameof(parsedTimestampUtc));
            }

            SourceLine = sourceLine;
            ParsedTimestampUtc = parsedTimestampUtc;
            OriginalTimestampText = originalTimestampText;
            ExpectedSlotReserved = expectedSlotReserved;
        }
    }
}