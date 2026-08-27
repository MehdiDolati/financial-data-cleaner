using System;
using System.Collections.Generic;

namespace Validator.Domain.Comparison
{
    /// <summary>
    /// Pure function: matches sorted timestamp sequences from benchmark and candidate,
    /// producing matched/missing/extra sets and a ComparisonCoverage summary.
    /// Deterministic ordering (FR-031).
    /// </summary>
    public static class TimestampMatcher
    {
        /// <summary>
        /// Matches two sorted timestamp sequences and computes coverage statistics.
        /// </summary>
        /// <param name="benchmarkTimestamps">Sorted timestamps from the benchmark.</param>
        /// <param name="candidateTimestamps">Sorted timestamps from the candidate.</param>
        /// <param name="benchmarkRecordCount">Total record count in benchmark.</param>
        /// <param name="candidateRecordCount">Total record count in candidate.</param>
        /// <returns>A result containing matched, missing, and extra timestamps plus coverage.</returns>
        public static TimestampMatchResult Match(
            IReadOnlyList<DateTimeOffset> benchmarkTimestamps,
            IReadOnlyList<DateTimeOffset> candidateTimestamps,
            long benchmarkRecordCount,
            long candidateRecordCount)
        {
            if (benchmarkTimestamps is null)
                throw new ArgumentNullException(nameof(benchmarkTimestamps));
            if (candidateTimestamps is null)
                throw new ArgumentNullException(nameof(candidateTimestamps));
            if (benchmarkRecordCount < 0)
                throw new ArgumentOutOfRangeException(nameof(benchmarkRecordCount));
            if (candidateRecordCount < 0)
                throw new ArgumentOutOfRangeException(nameof(candidateRecordCount));

            var matched = new List<DateTimeOffset>();
            var missingFromCandidate = new List<DateTimeOffset>();
            var extraInCandidate = new List<DateTimeOffset>();

            var bi = 0;
            var ci = 0;

            while (bi < benchmarkTimestamps.Count && ci < candidateTimestamps.Count)
            {
                var bTs = benchmarkTimestamps[bi];
                var cTs = candidateTimestamps[ci];

                if (bTs == cTs)
                {
                    matched.Add(bTs);
                    bi++;
                    ci++;
                }
                else if (bTs < cTs)
                {
                    missingFromCandidate.Add(bTs);
                    bi++;
                }
                else
                {
                    extraInCandidate.Add(cTs);
                    ci++;
                }
            }

            // Remaining benchmark timestamps are missing
            while (bi < benchmarkTimestamps.Count)
            {
                missingFromCandidate.Add(benchmarkTimestamps[bi]);
                bi++;
            }

            // Remaining candidate timestamps are extra
            while (ci < candidateTimestamps.Count)
            {
                extraInCandidate.Add(candidateTimestamps[ci]);
                ci++;
            }

            DateTimeOffset? overlappingStart = matched.Count > 0 ? matched[0] : null;
            DateTimeOffset? overlappingEnd = matched.Count > 0 ? matched[^1] : null;

            var coverage = new ComparisonCoverage(
                benchmarkRecordCount,
                candidateRecordCount,
                matched.Count,
                missingFromCandidate.Count,
                extraInCandidate.Count,
                overlappingStart,
                overlappingEnd);

            return new TimestampMatchResult(matched, missingFromCandidate, extraInCandidate, coverage);
        }
    }

    /// <summary>
    /// The result of matching two timestamp sequences.
    /// </summary>
    public sealed record TimestampMatchResult
    {
        public IReadOnlyList<DateTimeOffset> MatchedTimestamps { get; }
        public IReadOnlyList<DateTimeOffset> MissingFromCandidateTimestamps { get; }
        public IReadOnlyList<DateTimeOffset> ExtraInCandidateTimestamps { get; }
        public ComparisonCoverage Coverage { get; }

        public TimestampMatchResult(
            IReadOnlyList<DateTimeOffset> matchedTimestamps,
            IReadOnlyList<DateTimeOffset> missingFromCandidateTimestamps,
            IReadOnlyList<DateTimeOffset> extraInCandidateTimestamps,
            ComparisonCoverage coverage)
        {
            MatchedTimestamps = matchedTimestamps ?? throw new ArgumentNullException(nameof(matchedTimestamps));
            MissingFromCandidateTimestamps = missingFromCandidateTimestamps ?? throw new ArgumentNullException(nameof(missingFromCandidateTimestamps));
            ExtraInCandidateTimestamps = extraInCandidateTimestamps ?? throw new ArgumentNullException(nameof(extraInCandidateTimestamps));
            Coverage = coverage ?? throw new ArgumentNullException(nameof(coverage));
        }
    }
}
