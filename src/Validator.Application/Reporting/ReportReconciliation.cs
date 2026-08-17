using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Ingestion;
using Validator.Domain.Findings;

namespace Validator.Application.Reporting
{
    // Constant-size per-category counters used while findings stream into the
    // catalog. Memory stays bounded by the number of categories, never by the
    // number of findings.
    public sealed class CategoryCounters
    {
        private readonly long[] _entryCounts = new long[6];
        private readonly long[] _contributionSums = new long[6];

        public void Add(FindingCategory category, long contribution)
        {
            if (!DetailedFindingHeader.IsEstablishedCategory(category))
            {
                throw new ArgumentOutOfRangeException(nameof(category));
            }

            if (contribution <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(contribution), "Contribution must be positive.");
            }

            var index = (int)category;
            _entryCounts[index]++;
            _contributionSums[index] += contribution;
        }

        public FindingCatalogStatistics Snapshot() => new(
            For(FindingCategory.MissingCandle),
            For(FindingCategory.DuplicateRecord),
            For(FindingCategory.InvalidOhlc),
            For(FindingCategory.ClosedMarketRecord),
            For(FindingCategory.TimeGap),
            For(FindingCategory.MalformedRow));

        private CategoryStatistics For(FindingCategory category) =>
            new(_entryCounts[(int)category], _contributionSums[(int)category]);
    }

    // The reconciled six-category summary used by a successful detailed report.
    // It exposes no value named totalErrors or uniqueProblems.
    public sealed record ReportReconciliation
    {
        public IReadOnlyList<CategoryReconciliation> Categories { get; }
        public ScanCoverage Coverage { get; }
        public bool CoverageReconciled => Coverage.IsReconciled;

        public ReportReconciliation(
            IReadOnlyList<CategoryReconciliation> categories,
            ScanCoverage coverage)
        {
            if (categories is null)
            {
                throw new ArgumentNullException(nameof(categories));
            }

            if (coverage is null)
            {
                throw new ArgumentNullException(nameof(coverage));
            }

            if (!coverage.IsReconciled)
            {
                throw new ArgumentException(
                    "Scan coverage must reconcile (examined equals accepted plus malformed).",
                    nameof(coverage));
            }

            if (categories.Count != 6)
            {
                throw new ArgumentException("Exactly six category reconciliations are required.", nameof(categories));
            }

            var expectedCategories = new[]
            {
                FindingCategory.MissingCandle,
                FindingCategory.DuplicateRecord,
                FindingCategory.InvalidOhlc,
                FindingCategory.ClosedMarketRecord,
                FindingCategory.TimeGap,
                FindingCategory.MalformedRow
            };

            for (var index = 0; index < 6; index++)
            {
                if (categories[index].Category != expectedCategories[index])
                {
                    throw new ArgumentException(
                        "Categories must appear exactly once in canonical order.",
                        nameof(categories));
                }
            }

            Categories = categories;
            Coverage = coverage;
        }

        public static ReportReconciliation Create(
            DetailedSummary summary,
            ScanCoverage coverage,
            FindingCatalogStatistics catalog)
        {
            if (summary is null)
            {
                throw new ArgumentNullException(nameof(summary));
            }

            if (coverage is null)
            {
                throw new ArgumentNullException(nameof(coverage));
            }

            if (catalog is null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var categories = new List<CategoryReconciliation>(6)
            {
                new(FindingCategory.MissingCandle, summary.MissingCandles, catalog.MissingCandles.EntryCount, catalog.MissingCandles.ContributionSum),
                new(FindingCategory.DuplicateRecord, summary.DuplicateRecords, catalog.DuplicateRecords.EntryCount, catalog.DuplicateRecords.ContributionSum),
                new(FindingCategory.InvalidOhlc, summary.InvalidOhlc, catalog.InvalidOhlc.EntryCount, catalog.InvalidOhlc.ContributionSum),
                new(FindingCategory.ClosedMarketRecord, summary.ClosedMarketRecords, catalog.ClosedMarketRecords.EntryCount, catalog.ClosedMarketRecords.ContributionSum),
                new(FindingCategory.TimeGap, summary.TimeGaps, catalog.TimeGaps.EntryCount, catalog.TimeGaps.ContributionSum),
                new(FindingCategory.MalformedRow, summary.MalformedRows, catalog.MalformedRows.EntryCount, catalog.MalformedRows.ContributionSum)
            };

            return new ReportReconciliation(categories, coverage);
        }
    }
}