using System;
using System.Globalization;
using Validator.Domain.Findings;

namespace Validator.Application.Validation
{
    // Deterministic reference builders for the six established finding shapes.
    // Every reference is a stable invariant ASCII string derived only from
    // canonical source facts, so two runs over the same bytes agree exactly.
    public static class FindingReferenceFactory
    {
        public static FindingReference MissingCandle(DateTimeOffset expectedUtc) =>
            new($"missing-candle:{UtcKey(expectedUtc)}");

        public static FindingReference TimeGap(DateTimeOffset firstMissingUtc, DateTimeOffset lastMissingUtc) =>
            new($"time-gap:{UtcKey(firstMissingUtc)}:{UtcKey(lastMissingUtc)}");

        public static FindingReference DuplicateRecord(DateTimeOffset sharedTimestampUtc, long lowestSourceLine) =>
            new($"duplicate-record:{UtcKey(sharedTimestampUtc)}:line-{lowestSourceLine}");

        public static FindingReference PhysicalRecord(FindingCategory category, long sourceLine) =>
            new($"{CategorySegment(category)}:line-{sourceLine}");

        internal static string UtcKey(DateTimeOffset value) =>
            value.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture);

        private static string CategorySegment(FindingCategory category) => category switch
        {
            FindingCategory.MissingCandle => "missing-candle",
            FindingCategory.DuplicateRecord => "duplicate-record",
            FindingCategory.InvalidOhlc => "invalid-ohlc",
            FindingCategory.ClosedMarketRecord => "closed-market-record",
            FindingCategory.TimeGap => "time-gap",
            FindingCategory.MalformedRow => "malformed-row",
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
    }
}
