using System;
using System.Collections.Generic;
using Validator.Application.Reporting;
using Validator.Domain.Findings;

namespace Validator.Application.Scoring
{
    // Assembles the complete score section from values the run already
    // established: the six summary counts, the resolved populations, and each
    // check's status. It scores every applicable metric, marks the rest with the
    // reason they carry, applies the weighting to the average, and echoes each
    // covered metric's normalised share. Nothing here reopens or re-scans data.
    public static class ScoreSectionBuilder
    {
        public static DatasetScoreReport Build(
            DetailedSummary summary,
            MetricPopulations populations,
            IReadOnlyList<CheckExecution> checks,
            ScoreWeighting requestedWeighting)
        {
            ArgumentNullException.ThrowIfNull(summary);
            ArgumentNullException.ThrowIfNull(populations);
            ArgumentNullException.ThrowIfNull(checks);
            ArgumentNullException.ThrowIfNull(requestedWeighting);

            var metrics = new List<MetricScore>(6);
            foreach (var category in MetricPopulationMap.CanonicalOrder)
            {
                metrics.Add(BuildMetric(category, summary, populations, checks));
            }

            // The average is computed first so its coverage drives which metrics
            // receive a normalised share; weights only ever affect the average.
            var dataset = DatasetAverageCalculator.Compute(metrics, requestedWeighting);
            var weighting = ScoreWeightResolver.WithNormalisedShares(requestedWeighting, dataset.CoveredCategories);

            return new DatasetScoreReport(metrics, weighting, dataset);
        }

        private static MetricScore BuildMetric(
            FindingCategory category,
            DetailedSummary summary,
            MetricPopulations populations,
            IReadOnlyList<CheckExecution> checks)
        {
            var kind = MetricPopulationMap.KindFor(category);
            var count = summary.For(category);

            // A time-based metric whose sequence check did not run is not
            // applicable and reuses the originating check's own reason, so the
            // report explains the fact once rather than twice.
            var check = FindCheck(checks, category);
            if (check is not null && check.Status == CheckStatus.NotApplicable)
            {
                return MetricScore.NotApplicable(category, kind, check.Reason!);
            }

            var population = populations.For(kind);
            if (population is null)
            {
                // No population exists although the check is not marked
                // NotApplicable; treat the metric as not applicable with a
                // population reason rather than inventing a denominator.
                return MetricScore.NotApplicable(
                    category,
                    kind,
                    "The expected-candle population is unavailable because the sequence checks did not run.");
            }

            if (population.Value == 0)
            {
                return MetricScore.NotScored(
                    category,
                    kind,
                    $"The {DescribeKind(kind)} population was zero, so the rate is undefined.",
                    count);
            }

            return MetricScoreCalculator.ScoreMetric(category, count, population.Value, kind);
        }

        private static CheckExecution? FindCheck(IReadOnlyList<CheckExecution> checks, FindingCategory category)
        {
            var checkName = CheckNameFor(category);
            foreach (var check in checks)
            {
                if (check.Check == checkName)
                {
                    return check;
                }
            }

            return null;
        }

        private static CheckName CheckNameFor(FindingCategory category) => category switch
        {
            FindingCategory.MissingCandle => CheckName.MissingCandles,
            FindingCategory.DuplicateRecord => CheckName.DuplicateRecords,
            FindingCategory.InvalidOhlc => CheckName.InvalidOhlc,
            FindingCategory.ClosedMarketRecord => CheckName.ClosedMarketRecords,
            FindingCategory.TimeGap => CheckName.TimeGaps,
            FindingCategory.MalformedRow => CheckName.MalformedRows,
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };

        private static string DescribeKind(MetricPopulationKind kind) => kind switch
        {
            MetricPopulationKind.ExpectedCandles => "expected-candle",
            MetricPopulationKind.AcceptedRows => "accepted-row",
            MetricPopulationKind.ExaminedRows => "examined-row",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
