using System;
using System.Globalization;
using System.Text;
using Validator.Application.Comparison;
using Validator.Domain.Comparison;
using Validator.Domain.Scoring;

namespace Validator.Application.Reporting
{
    /// <summary>
    /// Renders a ComparisonReport as human-readable text per comparison-report-contract.md text format.
    /// Includes benchmark section, coverage, material discrepancies, tolerated differences, and scores.
    /// </summary>
    public sealed class ComparisonTextReportWriter
    {
        /// <summary>
        /// Renders the comparison report as human-readable text.
        /// </summary>
        public string Write(ComparisonReport report)
        {
            ArgumentNullException.ThrowIfNull(report);

            var sb = new StringBuilder();

            sb.AppendLine("=== BENCHMARK COMPARISON ===");
            sb.AppendLine();

            // Benchmark section
            sb.AppendLine($"Benchmark: {report.Benchmark.Name}");
            sb.AppendLine($"  Source: {report.Benchmark.Source.FileName} " +
                $"({report.Benchmark.Source.ByteSize:N0} bytes, " +
                $"sha256={report.Benchmark.Source.Sha256[..16]}...)");
            sb.AppendLine($"  Scores: {FormatBenchmarkScores(report)}");
            sb.AppendLine($"  Dataset Average: {FormatScore(report.Benchmark.Dataset.Average)}");
            sb.AppendLine();

            // Coverage section
            sb.AppendLine("Coverage:");
            sb.AppendLine($"  Benchmark records: {report.Coverage.BenchmarkRecordCount:N0}");
            sb.AppendLine($"  Candidate records: {report.Coverage.CandidateRecordCount:N0}");
            sb.AppendLine($"  Matched timestamps: {report.Coverage.MatchedCount:N0}");
            sb.AppendLine($"  Missing from candidate: {report.Coverage.MissingFromCandidateCount:N0}");
            sb.AppendLine($"  Extra in candidate: {report.Coverage.ExtraInCandidateCount:N0}");
            WriteOverlappingRange(sb, report.Coverage);
            sb.AppendLine();

            // Material discrepancies section
            if (report.MaterialDiscrepancies.Count > 0)
            {
                sb.AppendLine($"Material Discrepancies ({report.MaterialDiscrepancies.Count} found):");
                sb.AppendLine();

                for (var i = 0; i < report.MaterialDiscrepancies.Count; i++)
                {
                    WriteDiscrepancy(sb, report.MaterialDiscrepancies[i], i + 1);
                }
            }
            else if (report.Coverage.MatchedCount > 0)
            {
                sb.AppendLine("Material Discrepancies (0 found):");
                sb.AppendLine();
            }

            // Tolerated differences section
            sb.AppendLine("Tolerated Differences:");
            foreach (var summary in report.ToleratedSummary)
            {
                sb.AppendLine($"  {summary.Field}: {summary.AcceptedCount:N0} of {summary.TotalCompared:N0} accepted ({summary.MaterialCount:N0} material)");
            }
            sb.AppendLine();

            // Scores section
            if (report.CandidateScore is not null)
            {
                sb.AppendLine($"Candidate Quality Score: {FormatScore(report.CandidateScore.Dataset.Average)}");
            }

            if (report.AgreementScore.Score.HasValue)
            {
                sb.AppendLine($"Benchmark-Agreement Score: {report.AgreementScore.Score.Value.Format()} " +
                    $"({report.AgreementScore.MaterialDiscrepancyCount}/{report.AgreementScore.MatchedPopulation:N0} timestamps with material discrepancies)");
            }
            else
            {
                sb.AppendLine("Benchmark-Agreement Score: UNAVAILABLE");
                sb.AppendLine($"  Reason: {report.AgreementScore.UnavailableReason}");
            }

            return sb.ToString();
        }

        private static void WriteOverlappingRange(StringBuilder sb, ComparisonCoverage coverage)
        {
            var start = coverage.OverlappingRangeStart;
            var end = coverage.OverlappingRangeEnd;
            if (start.HasValue && end.HasValue)
            {
                sb.AppendLine($"  Overlapping range: {start.Value:yyyy-MM-dd} to {end.Value:yyyy-MM-dd}");
            }
        }

        private static void WriteDiscrepancy(StringBuilder sb, FieldDiscrepancy d, int index)
        {
            var sign = d.DirectionalDifference >= 0 ? "+" : "";
            var relativePercent = d.BenchmarkValue != 0
                ? $" ({(d.Difference / d.BenchmarkValue * 100):F2}%)"
                : "";

            sb.AppendLine($"  [{index}] {d.TimestampUtc:yyyy-MM-dd} {d.Field}");
            sb.AppendLine($"      Benchmark: {FormatDecimal(d.BenchmarkValue)}  Candidate: {FormatDecimal(d.CandidateValue)}  Diff: {sign}{FormatDecimal(d.DirectionalDifference)}{relativePercent}");
            sb.AppendLine($"      Tolerance: absolute={FormatDecimal(d.ResolvedAbsoluteTolerance)}, relative={FormatDecimal(d.ResolvedRelativeTolerance * 100)}%");
            sb.AppendLine($"      Decision: Material (exceeds both tolerances)");
            sb.AppendLine();
        }

        private static string FormatScore(ScoreValue? score) => score?.Format() ?? "N/A";

        private static string FormatBenchmarkScores(ComparisonReport report)
        {
            var parts = new string[report.Benchmark.Metrics.Count];
            for (var i = 0; i < report.Benchmark.Metrics.Count; i++)
            {
                var metric = report.Benchmark.Metrics[i];
                parts[i] = $"{metric.Category}={FormatScore(metric.Score)}";
            }
            return string.Join(", ", parts);
        }

        private static string FormatDecimal(decimal value)
        {
            return value.ToString("G", CultureInfo.InvariantCulture);
        }
    }
}
