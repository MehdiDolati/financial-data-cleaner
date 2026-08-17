using System;
using Validator.Domain.Findings;

namespace Validator.Application.Reporting
{
    // The established six category counts of one successful detailed report.
    // Meanings are unchanged from feature 001; values use 64-bit counts.
    public sealed record DetailedSummary
    {
        public long MissingCandles { get; }
        public long DuplicateRecords { get; }
        public long InvalidOhlc { get; }
        public long ClosedMarketRecords { get; }
        public long TimeGaps { get; }
        public long MalformedRows { get; }

        public long TotalFindings => MissingCandles + DuplicateRecords + InvalidOhlc + ClosedMarketRecords + TimeGaps + MalformedRows;

        public bool IsClean => TotalFindings == 0;

        public DetailedSummary(
            long missingCandles,
            long duplicateRecords,
            long invalidOhlc,
            long closedMarketRecords,
            long timeGaps,
            long malformedRows)
        {
            RequireNonNegative(missingCandles, nameof(missingCandles));
            RequireNonNegative(duplicateRecords, nameof(duplicateRecords));
            RequireNonNegative(invalidOhlc, nameof(invalidOhlc));
            RequireNonNegative(closedMarketRecords, nameof(closedMarketRecords));
            RequireNonNegative(timeGaps, nameof(timeGaps));
            RequireNonNegative(malformedRows, nameof(malformedRows));

            MissingCandles = missingCandles;
            DuplicateRecords = duplicateRecords;
            InvalidOhlc = invalidOhlc;
            ClosedMarketRecords = closedMarketRecords;
            TimeGaps = timeGaps;
            MalformedRows = malformedRows;
        }

        public long For(FindingCategory category) => category switch
        {
            FindingCategory.MissingCandle => MissingCandles,
            FindingCategory.DuplicateRecord => DuplicateRecords,
            FindingCategory.InvalidOhlc => InvalidOhlc,
            FindingCategory.ClosedMarketRecord => ClosedMarketRecords,
            FindingCategory.TimeGap => TimeGaps,
            FindingCategory.MalformedRow => MalformedRows,
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };

        private static void RequireNonNegative(long value, string parameterName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Category counts must be non-negative.");
            }
        }
    }
}