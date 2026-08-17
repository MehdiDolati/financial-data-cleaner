using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Ingestion;
using Validator.Domain.Findings;

namespace Validator.Application.Reporting
{
    // Runtime reconciliation gate before a successful report may be rendered.
    // Category contribution sums must equal the established summary counts,
    // physical row totals must reconcile, all six checks must have finished,
    // and the finding catalog must agree with the summary. Any failure returns
    // a REPORT_RECONCILIATION_FAILED fatal diagnostic.
    public static class ReconciliationValidator
    {
        private static readonly CheckName[] CanonicalChecks =
        [
            CheckName.MissingCandles,
            CheckName.DuplicateRecords,
            CheckName.InvalidOhlc,
            CheckName.ClosedMarketRecords,
            CheckName.TimeGaps,
            CheckName.MalformedRows
        ];

        public static FatalDiagnostic? Validate(
            IReadOnlyList<CheckExecution> checks,
            DetailedSummary summary,
            ScanCoverage coverage,
            FindingCatalogStatistics catalog) =>
            Reconcile(checks, summary, coverage, catalog);

        private static FatalDiagnostic? Reconcile(
            IReadOnlyList<CheckExecution> checks,
            DetailedSummary summary,
            ScanCoverage coverage,
            FindingCatalogStatistics catalog)
        {
            var failure = FindFailure(checks, summary, coverage, catalog);
            if (failure is null)
            {
                return null;
            }

            return new FatalDiagnostic(
                "REPORT_RECONCILIATION_FAILED",
                failure.Value.Reason,
                failure.Value.Guidance,
                checks: failure.Value.Checks);
        }

        private static (string Reason, string Guidance, IReadOnlyList<CheckExecution>? Checks)? FindFailure(
            IReadOnlyList<CheckExecution> checks,
            DetailedSummary summary,
            ScanCoverage coverage,
            FindingCatalogStatistics catalog)
        {
            if (checks is null)
            {
                return ("The check executions are missing.", "Reproduce the validation run.", null);
            }

            if (summary is null)
            {
                return ("The summary is missing.", "Reproduce the validation run.", ChecksIfCanonical(checks));
            }

            if (coverage is null)
            {
                return ("The scan coverage is missing.", "Reproduce the validation run.", ChecksIfCanonical(checks));
            }

            if (catalog is null)
            {
                return ("The finding catalog statistics are missing.", "Reproduce the validation run.", ChecksIfCanonical(checks));
            }

            if (checks.Count != 6 || !CanonicalOrder(checks))
            {
                return (
                    "The six check executions must appear exactly once in canonical order.",
                    "Reproduce the validation run and ensure every check completed.",
                    ChecksIfCanonical(checks));
            }

            var notCompleted = checks.Where(check => check.Status == CheckStatus.NotCompleted).ToList();
            if (notCompleted.Count > 0)
            {
                return (
                    $"Check '{notCompleted[0].Check}' did not complete.",
                    "Resolve the incomplete check before rendering a report.",
                    checks);
            }

            if (!coverage.IsReconciled)
            {
                return (
                    $"Scan coverage does not reconcile: examined {coverage.PhysicalRowsExamined}, accepted {coverage.AcceptedRows} plus malformed {coverage.MalformedRows}.",
                    "Recount physical rows and re-run validation.",
                    checks);
            }

            var categories = new (FindingCategory Category, long SummaryCount, long ContributionSum)[]
            {
                (FindingCategory.MissingCandle, summary.MissingCandles, catalog.MissingCandles.ContributionSum),
                (FindingCategory.DuplicateRecord, summary.DuplicateRecords, catalog.DuplicateRecords.ContributionSum),
                (FindingCategory.InvalidOhlc, summary.InvalidOhlc, catalog.InvalidOhlc.ContributionSum),
                (FindingCategory.ClosedMarketRecord, summary.ClosedMarketRecords, catalog.ClosedMarketRecords.ContributionSum),
                (FindingCategory.TimeGap, summary.TimeGaps, catalog.TimeGaps.ContributionSum),
                (FindingCategory.MalformedRow, summary.MalformedRows, catalog.MalformedRows.ContributionSum)
            };

            foreach (var (category, summaryCount, contributionSum) in categories)
            {
                if (summaryCount != contributionSum)
                {
                    return (
                        $"Category '{category}' summary count {summaryCount} does not equal the finding contribution sum {contributionSum}.",
                        "Re-run validation so the catalog contributes exactly the reported counts.",
                        checks);
                }
            }

            return null;
        }

        private static bool CanonicalOrder(IReadOnlyList<CheckExecution> checks)
        {
            for (var index = 0; index < 6; index++)
            {
                if (checks[index].Check != CanonicalChecks[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static IReadOnlyList<CheckExecution>? ChecksIfCanonical(IReadOnlyList<CheckExecution> checks)
        {
            if (checks.Count == 6 && CanonicalOrder(checks))
            {
                return checks;
            }

            return null;
        }
    }
}