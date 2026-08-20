using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Benchmark;
using Validator.Application.Scoring;
using Validator.Domain.Comparison;

namespace Validator.Application.Comparison
{
    /// <summary>
    /// Assembles a ComparisonReport from comparison results: attaches BenchmarkSnapshot,
    /// CandidateIdentity, Configuration, Coverage, ordered discrepancies, tolerated summary,
    /// candidate scores, and agreement score. Computes per-field tolerated aggregates
    /// from raw comparison results.
    /// </summary>
    public static class BenchmarkComparisonReportBuilder
    {
        /// <summary>
        /// Builds a ComparisonReport from the comparison results produced by CompareDatasetsUseCase.
        /// </summary>
        /// <param name="benchmark">The benchmark snapshot.</param>
        /// <param name="candidateIdentity">Identity of the candidate dataset.</param>
        /// <param name="configuration">The resolved comparison configuration.</param>
        /// <param name="coverage">The timestamp matching coverage.</param>
        /// <param name="allFieldResults">Raw per-field comparison results for computing aggregates.</param>
        /// <param name="materialDiscrepancies">Ordered list of material discrepancies.</param>
        /// <param name="candidateScore">Optional candidate independent score report.</param>
        /// <returns>A complete ComparisonReport.</returns>
        public static ComparisonReport Build(
            BenchmarkSnapshot benchmark,
            CandidateIdentity candidateIdentity,
            ComparisonConfiguration configuration,
            ComparisonCoverage coverage,
            IReadOnlyList<FieldComparisonResult> allFieldResults,
            IReadOnlyList<FieldDiscrepancy> materialDiscrepancies,
            DatasetScoreReport? candidateScore = null)
        {
            ArgumentNullException.ThrowIfNull(benchmark);
            ArgumentNullException.ThrowIfNull(candidateIdentity);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(coverage);
            ArgumentNullException.ThrowIfNull(allFieldResults);
            ArgumentNullException.ThrowIfNull(materialDiscrepancies);

            // Build tolerated summary from field results
            var toleratedSummary = BuildToleratedSummary(configuration, allFieldResults);

            // Compute agreement score
            var agreementScore = ComputeAgreementScore(coverage.MatchedCount, materialDiscrepancies);

            return new ComparisonReport(
                benchmark,
                candidateIdentity,
                configuration,
                coverage,
                materialDiscrepancies,
                toleratedSummary,
                candidateScore,
                agreementScore,
                DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Builds the per-field tolerated difference aggregates from raw comparison results.
        /// </summary>
        public static IReadOnlyList<ToleratedDifferenceAggregate> BuildToleratedSummary(
            ComparisonConfiguration configuration,
            IReadOnlyList<FieldComparisonResult> allFieldResults)
        {
            var summary = new List<ToleratedDifferenceAggregate>();

            foreach (var field in configuration.Fields.Where(f => f.Enabled))
            {
                var fieldResults = allFieldResults.Where(r => r.Field == field.Field).ToList();

                var totalCompared = (long)fieldResults.Count;
                var acceptedByAbsolute = fieldResults.Count(r =>
                    r.Decision is ToleranceDecision.AcceptedByAbsolute);
                var acceptedByRelative = fieldResults.Count(r =>
                    r.Decision is ToleranceDecision.AcceptedByRelative);
                var material = fieldResults.Count(r =>
                    r.Decision is ToleranceDecision.MaterialDifference);
                var accepted = acceptedByAbsolute + acceptedByRelative;

                summary.Add(new ToleratedDifferenceAggregate(
                    field.Field,
                    totalCompared,
                    accepted,
                    acceptedByAbsolute,
                    acceptedByRelative,
                    material));
            }

            return summary;
        }

        /// <summary>
        /// Computes the benchmark agreement score from comparison results.
        /// </summary>
        public static BenchmarkAgreementScore ComputeAgreementScore(
            long matchedCount,
            IReadOnlyList<FieldDiscrepancy> discrepancies)
        {
            if (matchedCount <= 0)
            {
                return BenchmarkAgreementScore.Unavailable(
                    "No overlapping timestamps between benchmark and candidate");
            }

            // Count timestamps with at least one material discrepancy
            var timestampsWithMaterial = discrepancies
                .Select(d => d.TimestampUtc)
                .Distinct()
                .Count();

            return BenchmarkAgreementScore.Available(matchedCount, timestampsWithMaterial);
        }
    }

}
