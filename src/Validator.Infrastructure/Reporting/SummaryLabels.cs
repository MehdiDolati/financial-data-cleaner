using System;
using System.Collections.Generic;
using Validator.Application.Reporting;
using Validator.Domain.Findings;

namespace Validator.Infrastructure.Reporting
{
    // The single source of the six established summary labels. Both text writers
    // emit their leading six lines from here so the concise and verbose
    // renderings cannot drift, which is what makes the byte-identical guarantee
    // (SC-006) testable rather than aspirational.
    public static class SummaryLabels
    {
        // The label for each metric, in the established category order, exactly
        // as it has always appeared at the head of a text report.
        public static readonly IReadOnlyList<(FindingCategory Category, string Label)> Ordered =
        [
            (FindingCategory.MissingCandle, "Missing candles"),
            (FindingCategory.DuplicateRecord, "Duplicate records"),
            (FindingCategory.InvalidOhlc, "Invalid OHLC"),
            (FindingCategory.ClosedMarketRecord, "Closed-market records"),
            (FindingCategory.TimeGap, "Time gaps"),
            (FindingCategory.MalformedRow, "Malformed rows")
        ];

        public static string LabelFor(FindingCategory category)
        {
            foreach (var (candidate, label) in Ordered)
            {
                if (candidate == category)
                {
                    return label;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(category), category, "No summary label exists for the category.");
        }

        // The six summary lines ("<label>: <count>") for one summary, in order.
        public static IEnumerable<string> Lines(DetailedSummary summary)
        {
            foreach (var (category, label) in Ordered)
            {
                yield return $"{label}: {summary.For(category)}";
            }
        }
    }
}
