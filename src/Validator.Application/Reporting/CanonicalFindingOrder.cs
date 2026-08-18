using System;
using System.Collections.Generic;
using Validator.Domain.Findings;

namespace Validator.Application.Reporting
{
    // The one order every report representation uses: established category
    // order, then the applicable UTC instant, then the applicable first source
    // line, with the deterministic reference as the documented final
    // tie-breaker. Values that do not apply to a finding sort last rather than
    // being replaced by an invented position, and no comparison depends on the
    // order findings happened to be produced in.
    public sealed class CanonicalFindingOrder : IComparer<DetailedFindingHeader>
    {
        public static CanonicalFindingOrder Instance { get; } = new();

        private CanonicalFindingOrder()
        {
        }

        public int Compare(DetailedFindingHeader? x, DetailedFindingHeader? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var byCategory = CategoryRank(x.Category).CompareTo(CategoryRank(y.Category));
            if (byCategory != 0)
            {
                return byCategory;
            }

            var byTimestamp = CompareOptional(
                x.Location.TimestampUtc,
                y.Location.TimestampUtc,
                static (left, right) => left.UtcTicks.CompareTo(right.UtcTicks));
            if (byTimestamp != 0)
            {
                return byTimestamp;
            }

            var byLine = CompareOptional(
                FirstSourceLine(x),
                FirstSourceLine(y),
                static (left, right) => left.CompareTo(right));
            if (byLine != 0)
            {
                return byLine;
            }

            return string.CompareOrdinal(x.Reference.Value, y.Reference.Value);
        }

        // The ordering key of one finding, as the invariant ASCII text a spool
        // sorts by. Fixed-width segments keep ordinal text order identical to
        // the comparer's order, and an absent value uses the highest segment so
        // it sorts last within its group.
        public static string SortKey(DetailedFindingHeader header)
        {
            if (header is null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            var timestamp = header.Location.TimestampUtc.HasValue
                ? header.Location.TimestampUtc.Value.UtcTicks.ToString("D19", System.Globalization.CultureInfo.InvariantCulture)
                : new string('9', 19) + "9";
            var line = FirstSourceLine(header) is { } sourceLine
                ? sourceLine.ToString("D19", System.Globalization.CultureInfo.InvariantCulture)
                : new string('9', 19) + "9";

            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{CategoryRank(header.Category)}|{timestamp}|{line}|{header.Reference.Value}");
        }

        private static long? FirstSourceLine(DetailedFindingHeader header) =>
            header.Location.SourceLines.Count == 0 ? null : header.Location.SourceLines[0];

        private static int CompareOptional<T>(T? left, T? right, Comparison<T> comparison)
            where T : struct
        {
            if (left.HasValue && right.HasValue)
            {
                return comparison(left.Value, right.Value);
            }

            if (left.HasValue)
            {
                return -1;
            }

            return right.HasValue ? 1 : 0;
        }

        // A DetailedFindingHeader cannot exist with a category outside the six
        // established ones, and those are declared in canonical report order, so
        // the declared value is already the rank. Restating the mapping here
        // would be a second copy that could disagree with the first.
        private static int CategoryRank(FindingCategory category) => (int)category;
    }
}
