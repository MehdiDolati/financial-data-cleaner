using System;
using System.Collections.Generic;

namespace Validator.Application.Validation
{
    // One observed record's timestamp paired with the physical line it occupies.
    public sealed record ObservedRowAnchor
    {
        public DateTimeOffset TimestampUtc { get; }
        public long SourceLine { get; }

        public ObservedRowAnchor(DateTimeOffset timestampUtc, long sourceLine)
        {
            if (timestampUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Observed timestamp must be UTC.", nameof(timestampUtc));
            }

            if (sourceLine <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceLine), "Source line must be positive.");
            }

            TimestampUtc = timestampUtc;
            SourceLine = sourceLine;
        }
    }

    // Resolves an observed timestamp to the physical line that best brackets an
    // adjacent absence (FR-039, FR-040).
    //
    // A timestamp can occupy several physical rows, so each side picks the row
    // closest to the absence: the highest line among rows sharing the preceding
    // timestamp, and the lowest line among rows sharing the following one. That
    // is the "tightest bracket" — the two rows a reader would look between.
    //
    // Because unsorted input is accepted, the resolved pair identifies the
    // temporal neighbours, not physically adjacent rows: the lines may be
    // non-consecutive or even descending, and that is correct rather than a
    // defect. A timestamp with no observed row resolves to no line, so an
    // absence is never given an invented one.
    public sealed class AbsenceAnchorResolver
    {
        private readonly Dictionary<DateTimeOffset, (long Lowest, long Highest)> _lines;

        private AbsenceAnchorResolver(Dictionary<DateTimeOffset, (long Lowest, long Highest)> lines)
        {
            _lines = lines;
        }

        public static AbsenceAnchorResolver Build(IEnumerable<ObservedRowAnchor> anchors)
        {
            ArgumentNullException.ThrowIfNull(anchors);

            var lines = new Dictionary<DateTimeOffset, (long Lowest, long Highest)>();
            foreach (var anchor in anchors)
            {
                if (lines.TryGetValue(anchor.TimestampUtc, out var seen))
                {
                    lines[anchor.TimestampUtc] = (
                        Math.Min(seen.Lowest, anchor.SourceLine),
                        Math.Max(seen.Highest, anchor.SourceLine));
                }
                else
                {
                    lines.Add(anchor.TimestampUtc, (anchor.SourceLine, anchor.SourceLine));
                }
            }

            return new AbsenceAnchorResolver(lines);
        }

        // The line of the observed record immediately preceding an absence. When
        // the timestamp repeats, the highest such line is the tightest bracket.
        public long? PrecedingLine(DateTimeOffset? observedTimestampUtc) =>
            Lookup(observedTimestampUtc, highest: true);

        // The line of the observed record immediately following an absence. When
        // the timestamp repeats, the lowest such line is the tightest bracket.
        public long? FollowingLine(DateTimeOffset? observedTimestampUtc) =>
            Lookup(observedTimestampUtc, highest: false);

        private long? Lookup(DateTimeOffset? observedTimestampUtc, bool highest)
        {
            if (observedTimestampUtc is null)
            {
                return null;
            }

            if (!_lines.TryGetValue(observedTimestampUtc.Value, out var bounds))
            {
                return null;
            }

            return highest ? bounds.Highest : bounds.Lowest;
        }
    }
}