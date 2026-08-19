using System;
using System.Linq;
using Validator.Application.Reporting;

namespace Validator.Application.Benchmark
{
    /// <summary>
    /// Validates that a DetailedValidationReport has all required fields before benchmark creation is allowed.
    /// Rejects incomplete reports per FR-004.
    /// </summary>
    public static class BenchmarkSnapshotValidator
    {
        /// <summary>
        /// Validates the report is complete enough to create a benchmark snapshot.
        /// Returns null if valid, or an error message if not.
        /// </summary>
        public static string? Validate(DetailedValidationReport report)
        {
            if (report is null)
                return "Report must not be null.";

            if (report.Source is null)
                return "Report must include a source identity.";

            if (report.Context is null)
                return "Report must include a validation context.";

            if (report.Checks is null || report.Checks.Count != 6)
                return "Report must include exactly six check results.";

            if (report.Checks.Any(check => check.Status == CheckStatus.NotCompleted))
                return "All six checks must be completed before establishing a benchmark.";

            if (report.Score is null)
                return "Report must include scoring results (use --score when establishing a benchmark).";

            if (report.Score.Dataset is null)
                return "Report must include a dataset score.";

            return null; // valid
        }
    }
}
