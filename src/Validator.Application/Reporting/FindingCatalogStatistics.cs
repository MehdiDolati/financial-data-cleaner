using System;
using Validator.Domain.Findings;

namespace Validator.Application.Reporting
{
    // Constant-size statistics of the completed finding catalog. The completed
    // values are the authoritative input to report reconciliation.
    public sealed record CategoryStatistics
    {
        public long EntryCount { get; }
        public long ContributionSum { get; }

        public CategoryStatistics(long entryCount, long contributionSum)
        {
            if (entryCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entryCount), "Entry count must be non-negative.");
            }

            if (contributionSum < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(contributionSum), "Contribution sum must be non-negative.");
            }

            EntryCount = entryCount;
            ContributionSum = contributionSum;
        }
    }

    /// <summary>
    /// The per-category counts of a completed catalog.
    /// </summary>
    /// <remarks>
    /// Constant in size no matter how many findings exist, and the authoritative
    /// input to reconciliation: the report's summary must agree with these counts
    /// or the run fails rather than publishing totals it cannot support.
    /// </remarks>
    public sealed record FindingCatalogStatistics
    {
        public CategoryStatistics MissingCandles { get; }
        public CategoryStatistics DuplicateRecords { get; }
        public CategoryStatistics InvalidOhlc { get; }
        public CategoryStatistics ClosedMarketRecords { get; }
        public CategoryStatistics TimeGaps { get; }
        public CategoryStatistics MalformedRows { get; }

        public FindingCatalogStatistics(
            CategoryStatistics missingCandles,
            CategoryStatistics duplicateRecords,
            CategoryStatistics invalidOhlc,
            CategoryStatistics closedMarketRecords,
            CategoryStatistics timeGaps,
            CategoryStatistics malformedRows)
        {
            MissingCandles = missingCandles ?? throw new ArgumentNullException(nameof(missingCandles));
            DuplicateRecords = duplicateRecords ?? throw new ArgumentNullException(nameof(duplicateRecords));
            InvalidOhlc = invalidOhlc ?? throw new ArgumentNullException(nameof(invalidOhlc));
            ClosedMarketRecords = closedMarketRecords ?? throw new ArgumentNullException(nameof(closedMarketRecords));
            TimeGaps = timeGaps ?? throw new ArgumentNullException(nameof(timeGaps));
            MalformedRows = malformedRows ?? throw new ArgumentNullException(nameof(malformedRows));
        }

        public CategoryStatistics For(FindingCategory category) => category switch
        {
            FindingCategory.MissingCandle => MissingCandles,
            FindingCategory.DuplicateRecord => DuplicateRecords,
            FindingCategory.InvalidOhlc => InvalidOhlc,
            FindingCategory.ClosedMarketRecord => ClosedMarketRecords,
            FindingCategory.TimeGap => TimeGaps,
            FindingCategory.MalformedRow => MalformedRows,
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
    }
}