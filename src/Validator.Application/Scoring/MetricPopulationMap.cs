using System;
using System.Collections.Generic;
using Validator.Domain.Findings;

namespace Validator.Application.Scoring
{
    // The fixed metric-to-population-kind mapping and the canonical order of the
    // six established metrics. This is the sole authority for which denominator
    // each metric's defect rate is measured against, and for the order every
    // scoring surface must present them in.
    public static class MetricPopulationMap
    {
        // The six established categories in canonical order. Every scoring
        // collection is presented in exactly this sequence.
        public static readonly IReadOnlyList<FindingCategory> CanonicalOrder =
        [
            FindingCategory.MissingCandle,
            FindingCategory.DuplicateRecord,
            FindingCategory.InvalidOhlc,
            FindingCategory.ClosedMarketRecord,
            FindingCategory.TimeGap,
            FindingCategory.MalformedRow
        ];

        public static MetricPopulationKind KindFor(FindingCategory category) => category switch
        {
            FindingCategory.MissingCandle => MetricPopulationKind.ExpectedCandles,
            FindingCategory.TimeGap => MetricPopulationKind.ExpectedCandles,
            FindingCategory.DuplicateRecord => MetricPopulationKind.AcceptedRows,
            FindingCategory.InvalidOhlc => MetricPopulationKind.AcceptedRows,
            FindingCategory.ClosedMarketRecord => MetricPopulationKind.AcceptedRows,
            FindingCategory.MalformedRow => MetricPopulationKind.ExaminedRows,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Only the six established metrics have a population kind.")
        };
    }
}
