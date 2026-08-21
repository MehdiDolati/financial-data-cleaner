using System;

namespace Validator.Domain.Comparison
{
    /// <summary>
    /// Describes the overlap between benchmark and candidate datasets.
    /// Count invariants: BenchmarkRecordCount = MatchedCount + MissingFromCandidateCount
    ///                   CandidateRecordCount = MatchedCount + ExtraInCandidateCount
    /// </summary>
    public sealed record ComparisonCoverage
    {
        public long BenchmarkRecordCount { get; init; }
        public long CandidateRecordCount { get; init; }
        public long MatchedCount { get; init; }
        public long MissingFromCandidateCount { get; init; }
        public long ExtraInCandidateCount { get; init; }
        public DateTimeOffset? OverlappingRangeStart { get; init; }
        public DateTimeOffset? OverlappingRangeEnd { get; init; }

        public ComparisonCoverage(
            long benchmarkRecordCount,
            long candidateRecordCount,
            long matchedCount,
            long missingFromCandidateCount,
            long extraInCandidateCount,
            DateTimeOffset? overlappingRangeStart = null,
            DateTimeOffset? overlappingRangeEnd = null)
        {
            if (benchmarkRecordCount < 0)
                throw new ArgumentOutOfRangeException(nameof(benchmarkRecordCount), "Must be non-negative.");
            if (candidateRecordCount < 0)
                throw new ArgumentOutOfRangeException(nameof(candidateRecordCount), "Must be non-negative.");
            if (matchedCount < 0)
                throw new ArgumentOutOfRangeException(nameof(matchedCount), "Must be non-negative.");
            if (missingFromCandidateCount < 0)
                throw new ArgumentOutOfRangeException(nameof(missingFromCandidateCount), "Must be non-negative.");
            if (extraInCandidateCount < 0)
                throw new ArgumentOutOfRangeException(nameof(extraInCandidateCount), "Must be non-negative.");

            if (matchedCount + missingFromCandidateCount != benchmarkRecordCount)
                throw new ArgumentException("MatchedCount + MissingFromCandidateCount must equal BenchmarkRecordCount.");
            if (matchedCount + extraInCandidateCount != candidateRecordCount)
                throw new ArgumentException("MatchedCount + ExtraInCandidateCount must equal CandidateRecordCount.");

            BenchmarkRecordCount = benchmarkRecordCount;
            CandidateRecordCount = candidateRecordCount;
            MatchedCount = matchedCount;
            MissingFromCandidateCount = missingFromCandidateCount;
            ExtraInCandidateCount = extraInCandidateCount;
            OverlappingRangeStart = overlappingRangeStart;
            OverlappingRangeEnd = overlappingRangeEnd;
        }
    }
}
