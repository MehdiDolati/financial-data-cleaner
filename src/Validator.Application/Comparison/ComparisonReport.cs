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
        public IReadOnlyList<DateTimeOffset> MissingFromCandidateTimestamps { get; init; }
        public IReadOnlyList<DateTimeOffset> ExtraInCandidateTimestamps { get; init; }
        public IReadOnlyList<TimestampAlignmentReference> MissingFromCandidateRecords { get; init; }
        public IReadOnlyList<TimestampAlignmentReference> ExtraInCandidateRecords { get; init; }
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
            MissingFromCandidateTimestamps = Array.Empty<DateTimeOffset>();
            ExtraInCandidateTimestamps = Array.Empty<DateTimeOffset>();
            CandidateScore = candidateScore;
            AgreementScore = agreementScore;
            ContextWarnings = contextWarnings ?? Array.Empty<string>();
            ResolutionTimestamp = resolutionTimestamp == default ? DateTimeOffset.UnixEpoch : resolutionTimestamp.ToUniversalTime();
            MissingFromCandidateRecords = Array.Empty<TimestampAlignmentReference>();
            ExtraInCandidateRecords = Array.Empty<TimestampAlignmentReference>();
        }

        public ComparisonReport(
            BenchmarkSnapshot benchmark,
            CandidateIdentity candidate,
            ComparisonConfiguration configuration,
            ComparisonCoverage coverage,
            IReadOnlyList<FieldDiscrepancy> materialDiscrepancies,
            IReadOnlyList<ToleratedDifferenceAggregate> toleratedSummary,
            IReadOnlyList<DateTimeOffset> missingFromCandidateTimestamps,
            IReadOnlyList<DateTimeOffset> extraInCandidateTimestamps,
            DatasetScoreReport? candidateScore,
            BenchmarkAgreementScore agreementScore,
            IReadOnlyList<string>? contextWarnings = null,
            DateTimeOffset resolutionTimestamp = default,
            IReadOnlyList<TimestampAlignmentReference>? missingFromCandidateRecords = null,
            IReadOnlyList<TimestampAlignmentReference>? extraInCandidateRecords = null)
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
            MissingFromCandidateTimestamps = missingFromCandidateTimestamps ?? Array.Empty<DateTimeOffset>();
            ExtraInCandidateTimestamps = extraInCandidateTimestamps ?? Array.Empty<DateTimeOffset>();
            CandidateScore = candidateScore;
            AgreementScore = agreementScore;
            ContextWarnings = contextWarnings ?? Array.Empty<string>();
            ResolutionTimestamp = resolutionTimestamp == default ? DateTimeOffset.UnixEpoch : resolutionTimestamp.ToUniversalTime();
            MissingFromCandidateRecords = missingFromCandidateRecords ?? Array.Empty<TimestampAlignmentReference>();
            ExtraInCandidateRecords = extraInCandidateRecords ?? Array.Empty<TimestampAlignmentReference>();
        }
    }
}
