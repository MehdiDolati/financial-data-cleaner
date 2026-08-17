using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Domain.Findings;

namespace Validator.Application.Reporting
{
    // One category's reconciliation: the established summary count, the number
    // of detailed entries, and the sum of every entry's positive contribution.
    // SummaryCount must equal ContributionSum; EntryCount may differ when one
    // entry contributes more than one count.
    public sealed record CategoryReconciliation
    {
        public FindingCategory Category { get; }
        public long SummaryCount { get; }
        public long EntryCount { get; }
        public long ContributionSum { get; }

        public CategoryReconciliation(
            FindingCategory category,
            long summaryCount,
            long entryCount,
            long contributionSum)
        {
            if (!DetailedFindingHeader.IsEstablishedCategory(category))
            {
                throw new ArgumentException("Category must be one of the six established categories.", nameof(category));
            }

            if (summaryCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(summaryCount), "Summary count must be non-negative.");
            }

            if (entryCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entryCount), "Entry count must be non-negative.");
            }

            if (contributionSum < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(contributionSum), "Contribution sum must be non-negative.");
            }

            if (summaryCount != contributionSum)
            {
                throw new ArgumentException(
                    "Summary count must equal the contribution sum for every category.",
                    nameof(summaryCount));
            }

            Category = category;
            SummaryCount = summaryCount;
            EntryCount = entryCount;
            ContributionSum = contributionSum;
        }
    }
}