using System;
using System.Collections.Generic;
using System.Linq;

namespace Validator.Domain.Findings
{
    // Source traceability of one detailed finding. No line number is invented
    // for an expected-but-absent record; source lines are positive 64-bit values.
    public sealed record FindingLocation
    {
        public IReadOnlyList<long> SourceLines { get; }
        public DateTimeOffset? TimestampUtc { get; }
        public string? OriginalTimestampText { get; }

        public FindingLocation(
            IReadOnlyList<long>? sourceLines,
            DateTimeOffset? timestampUtc = null,
            string? originalTimestampText = null)
        {
            var lines = sourceLines ?? Array.Empty<long>();
            if (lines.Any(line => line <= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(sourceLines), "Source lines must be positive.");
            }

            if (timestampUtc.HasValue && timestampUtc.Value.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Location timestamp must be UTC.", nameof(timestampUtc));
            }

            SourceLines = lines;
            TimestampUtc = timestampUtc;
            OriginalTimestampText = originalTimestampText;
        }
    }
}