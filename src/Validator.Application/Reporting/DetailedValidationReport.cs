using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Domain.Findings;

namespace Validator.Application.Reporting
{
    // Complete outcome of one successful scan. The report cannot exist until
    // the finding catalog is completed, every check completed, and all
    // reconciliation invariants pass. Fatal outcomes are a separate aggregate.
    public sealed record DetailedValidationReport
    {
        public int ContractVersion { get; init; } = 2;
        public ReportStatus Status { get; init; }
        public bool FindingSetComplete { get; init; } = true;
        public SourceIdentity Source { get; }
        public ValidationContextSnapshot Context { get; }
        public ScanCoverage Coverage { get; }
        public IReadOnlyList<CheckExecution> Checks { get; }
        public DetailedSummary Summary { get; }
        public ReportReconciliation Reconciliation { get; }
        public ICompletedFindingCatalog Findings { get; }

        public DetailedValidationReport(
            SourceIdentity source,
            ValidationContextSnapshot context,
            ScanCoverage coverage,
            IReadOnlyList<CheckExecution> checks,
            DetailedSummary summary,
            ReportReconciliation reconciliation,
            ICompletedFindingCatalog findings)
            : this(source, context, coverage, checks, summary, reconciliation, findings, summary.IsClean ? ReportStatus.Clean : ReportStatus.FindingsDetected)
        {
        }

        public DetailedValidationReport(
            SourceIdentity source,
            ValidationContextSnapshot context,
            ScanCoverage coverage,
            IReadOnlyList<CheckExecution> checks,
            DetailedSummary summary,
            ReportReconciliation reconciliation,
            ICompletedFindingCatalog findings,
            ReportStatus status)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (coverage is null)
            {
                throw new ArgumentNullException(nameof(coverage));
            }

            if (checks is null)
            {
                throw new ArgumentNullException(nameof(checks));
            }

            if (checks.Count != 6 || checks.Any(check => check.Status == CheckStatus.NotCompleted))
            {
                throw new ArgumentException(
                    "A successful report requires exactly six checks and no NotCompleted check.",
                    nameof(checks));
            }

            var expectedChecks = new[]
            {
                CheckName.MissingCandles,
                CheckName.DuplicateRecords,
                CheckName.InvalidOhlc,
                CheckName.ClosedMarketRecords,
                CheckName.TimeGaps,
                CheckName.MalformedRows
            };

            for (var index = 0; index < 6; index++)
            {
                if (checks[index].Check != expectedChecks[index])
                {
                    throw new ArgumentException(
                        "Checks must appear exactly once in canonical order.",
                        nameof(checks));
                }
            }

            if (summary is null)
            {
                throw new ArgumentNullException(nameof(summary));
            }

            if (reconciliation is null)
            {
                throw new ArgumentNullException(nameof(reconciliation));
            }

            if (findings is null)
            {
                throw new ArgumentNullException(nameof(findings));
            }

            if ((status == ReportStatus.Clean) != summary.IsClean)
            {
                throw new ArgumentException(
                    "Report status must be Clean exactly when every category count is zero.",
                    nameof(status));
            }

            Source = source;
            Context = context;
            Coverage = coverage;
            Checks = checks;
            Summary = summary;
            Reconciliation = reconciliation;
            Findings = findings;
            Status = status;
        }
    }

    // Discriminated outcome of a detailed validation run. Only Succeeded may
    // be passed to a successful report writer.
    public abstract record DetailedValidationOutcome
    {
        /// <summary>A complete, reconciled report was produced.</summary>
        public sealed record Succeeded(DetailedValidationReport Report) : DetailedValidationOutcome;

        /// <summary>No report could be produced, and the diagnostic says why.</summary>
        public sealed record Failed(FatalDiagnostic Diagnostic) : DetailedValidationOutcome;
    }
}