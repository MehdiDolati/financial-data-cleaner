using System;
using Validator.Domain.Comparison;
using Xunit;

namespace Validator.Domain.Tests.Comparison
{
    public class ComparisonCoverageTests
    {
        [Fact]
        public void Constructor_WithValidCounts_Succeeds()
        {
            var coverage = new ComparisonCoverage(
                benchmarkRecordCount: 100,
                candidateRecordCount: 98,
                matchedCount: 95,
                missingFromCandidateCount: 5,
                extraInCandidateCount: 3);

            Assert.Equal(100, coverage.BenchmarkRecordCount);
            Assert.Equal(98, coverage.CandidateRecordCount);
            Assert.Equal(95, coverage.MatchedCount);
            Assert.Equal(5, coverage.MissingFromCandidateCount);
            Assert.Equal(3, coverage.ExtraInCandidateCount);
        }

        [Fact]
        public void Constructor_WhenMatchedPlusMissingNotEqualBenchmark_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => new ComparisonCoverage(
                benchmarkRecordCount: 100,
                candidateRecordCount: 98,
                matchedCount: 95,
                missingFromCandidateCount: 3, // should be 5
                extraInCandidateCount: 3));

            Assert.Contains("MatchedCount + MissingFromCandidateCount must equal BenchmarkRecordCount", ex.Message);
        }

        [Fact]
        public void Constructor_WhenMatchedPlusExtraNotEqualCandidate_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => new ComparisonCoverage(
                benchmarkRecordCount: 100,
                candidateRecordCount: 98,
                matchedCount: 95,
                missingFromCandidateCount: 5,
                extraInCandidateCount: 1)); // should be 3

            Assert.Contains("MatchedCount + ExtraInCandidateCount must equal CandidateRecordCount", ex.Message);
        }

        [Fact]
        public void Constructor_WithZeroMatch_Succeeds()
        {
            var coverage = new ComparisonCoverage(
                benchmarkRecordCount: 100,
                candidateRecordCount: 80,
                matchedCount: 0,
                missingFromCandidateCount: 100,
                extraInCandidateCount: 80);

            Assert.Equal(0, coverage.MatchedCount);
        }

        [Fact]
        public void Constructor_WithNegativeCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ComparisonCoverage(
                benchmarkRecordCount: 100,
                candidateRecordCount: 98,
                matchedCount: -1,
                missingFromCandidateCount: 101,
                extraInCandidateCount: 3));
        }

        [Fact]
        public void Constructor_WithOverlappingRange_Succeeds()
        {
            var start = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
            var end = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero);

            var coverage = new ComparisonCoverage(
                benchmarkRecordCount: 100,
                candidateRecordCount: 98,
                matchedCount: 95,
                missingFromCandidateCount: 5,
                extraInCandidateCount: 3,
                overlappingRangeStart: start,
                overlappingRangeEnd: end);

            Assert.Equal(start, coverage.OverlappingRangeStart);
            Assert.Equal(end, coverage.OverlappingRangeEnd);
        }
    }
}
