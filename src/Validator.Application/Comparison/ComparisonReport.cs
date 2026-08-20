using System;
using System.Collections.Generic;
using Validator.Application.Benchmark;
using Validator.Application.Scoring;
using Validator.Domain.Comparison;

namespace Validator.Application.Comparison
{
    /// <summary>
    /// The complete result of one comparison run, combining both datasets' independent
    /// scores with the comparison results.
    /// </summary>
    public sealed record ComparisonReport
    {
        public BenchmarkSnapshot Benchmark { get; init; }
        public CandidateIdentity Candidate { get; init; }
        public ComparisonConfiguration Configuration { get; init; }
        public ComparisonCoverage Coverage { get; init; }
        public IReadOnlyList<FieldDiscrepancy> MaterialDiscrepancies { get; init; }
        public IReadOnlyList<ToleratedDifferenceAggregate> ToleratedSummary { get; init; }
        public DatasetScoreReport? CandidateScore { get; init; }
        public BenchmarkAgreementScore AgreementScore { get; init; }
        public IReadOnlyList<string> ContextWarnings { get; init; }
        public DateTimeOffset ResolutionTimestamp { get; init; }

        public ComparisonReport(
            BenchmarkSnapshot benchmark,
            CandidateIdentity candidate,
            ComparisonConfiguration configuration,
            ComparisonCoverage coverage,
            IReadOnlyList<FieldDiscrepancy> materialDiscrepancies,
            IReadOnlyList<ToleratedDifferenceAggregate> toleratedSummary,
            DatasetScoreReport? candidateScore,
            BenchmarkAgreementScore agreementScore,
            IReadOnlyList<string>? contextWarnings = null,
            DateTimeOffset resolutionTimestamp = default)
        {
            ArgumentNullException.ThrowIfNull(benchmark);
            ArgumentNullException.ThrowIfNull(candidate);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(coverage);
            ArgumentNullException.ThrowIfNull(materialDiscrepancies);
            ArgumentNullException.ThrowIfNull(toleratedSummary);
            ArgumentNullException.ThrowIfNull(agreementScore);

            Benchmark = benchmark;
            Candidate = candidate;
            Configuration = configuration;
            Coverage = coverage;
            MaterialDiscrepancies = materialDiscrepancies;
            ToleratedSummary = toleratedSummary;
            CandidateScore = candidateScore;
            AgreementScore = agreementScore;
            ContextWarnings = contextWarnings ?? Array.Empty<string>();
            ResolutionTimestamp = resolutionTimestamp == default ? DateTimeOffset.UtcNow : resolutionTimestamp;
        }
    }
}
