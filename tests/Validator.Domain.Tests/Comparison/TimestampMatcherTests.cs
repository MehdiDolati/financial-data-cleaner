using Validator.Domain.Comparison;
using Xunit;

namespace Validator.Domain.Tests.Comparison
{
    public class TimestampMatcherTests
    {
        private static DateTimeOffset Ts(int year, int month, int day) =>
            new(year, month, day, 0, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Match_FullOverlap_AllMatched()
        {
            var benchmark = new[] { Ts(2026, 1, 2), Ts(2026, 1, 3), Ts(2026, 1, 6) };
            var candidate = new[] { Ts(2026, 1, 2), Ts(2026, 1, 3), Ts(2026, 1, 6) };

            var result = TimestampMatcher.Match(benchmark, candidate, 3, 3);

            Assert.Equal(3, result.MatchedTimestamps.Count);
            Assert.Empty(result.MissingFromCandidateTimestamps);
            Assert.Empty(result.ExtraInCandidateTimestamps);
            Assert.Equal(3, result.Coverage.MatchedCount);
            Assert.Equal(0, result.Coverage.MissingFromCandidateCount);
            Assert.Equal(0, result.Coverage.ExtraInCandidateCount);
            Assert.Equal(Ts(2026, 1, 2), result.Coverage.OverlappingRangeStart);
            Assert.Equal(Ts(2026, 1, 6), result.Coverage.OverlappingRangeEnd);
        }

        [Fact]
        public void Match_MissingFromCandidate_ReportedCorrectly()
        {
            var benchmark = new[] { Ts(2026, 1, 2), Ts(2026, 1, 3), Ts(2026, 1, 6) };
            var candidate = new[] { Ts(2026, 1, 2), Ts(2026, 1, 6) };

            var result = TimestampMatcher.Match(benchmark, candidate, 3, 2);

            Assert.Equal(2, result.MatchedTimestamps.Count);
            Assert.Single(result.MissingFromCandidateTimestamps);
            Assert.Equal(Ts(2026, 1, 3), result.MissingFromCandidateTimestamps[0]);
            Assert.Empty(result.ExtraInCandidateTimestamps);
            Assert.Equal(2, result.Coverage.MatchedCount);
            Assert.Equal(1, result.Coverage.MissingFromCandidateCount);
            Assert.Equal(0, result.Coverage.ExtraInCandidateCount);
        }

        [Fact]
        public void Match_ExtraInCandidate_ReportedCorrectly()
        {
            var benchmark = new[] { Ts(2026, 1, 2), Ts(2026, 1, 6) };
            var candidate = new[] { Ts(2026, 1, 2), Ts(2026, 1, 3), Ts(2026, 1, 6) };

            var result = TimestampMatcher.Match(benchmark, candidate, 2, 3);

            Assert.Equal(2, result.MatchedTimestamps.Count);
            Assert.Empty(result.MissingFromCandidateTimestamps);
            Assert.Single(result.ExtraInCandidateTimestamps);
            Assert.Equal(Ts(2026, 1, 3), result.ExtraInCandidateTimestamps[0]);
            Assert.Equal(2, result.Coverage.MatchedCount);
            Assert.Equal(0, result.Coverage.MissingFromCandidateCount);
            Assert.Equal(1, result.Coverage.ExtraInCandidateCount);
        }

        [Fact]
        public void Match_NoOverlap_AllMissingAndExtra()
        {
            var benchmark = new[] { Ts(2020, 1, 2), Ts(2020, 1, 3) };
            var candidate = new[] { Ts(2026, 1, 2), Ts(2026, 1, 3) };

            var result = TimestampMatcher.Match(benchmark, candidate, 2, 2);

            Assert.Empty(result.MatchedTimestamps);
            Assert.Equal(2, result.MissingFromCandidateTimestamps.Count);
            Assert.Equal(2, result.ExtraInCandidateTimestamps.Count);
            Assert.Equal(0, result.Coverage.MatchedCount);
            Assert.Null(result.Coverage.OverlappingRangeStart);
            Assert.Null(result.Coverage.OverlappingRangeEnd);
        }

        [Fact]
        public void Match_EmptyBoth_AllCountsZero()
        {
            var benchmark = Array.Empty<DateTimeOffset>();
            var candidate = Array.Empty<DateTimeOffset>();

            var result = TimestampMatcher.Match(benchmark, candidate, 0, 0);

            Assert.Empty(result.MatchedTimestamps);
            Assert.Empty(result.MissingFromCandidateTimestamps);
            Assert.Empty(result.ExtraInCandidateTimestamps);
            Assert.Equal(0, result.Coverage.MatchedCount);
            Assert.Equal(0, result.Coverage.BenchmarkRecordCount);
            Assert.Equal(0, result.Coverage.CandidateRecordCount);
        }

        [Fact]
        public void Match_EmptyBenchmark_AllCandidateExtra()
        {
            var benchmark = Array.Empty<DateTimeOffset>();
            var candidate = new[] { Ts(2026, 1, 2), Ts(2026, 1, 3) };

            var result = TimestampMatcher.Match(benchmark, candidate, 0, 2);

            Assert.Empty(result.MatchedTimestamps);
            Assert.Empty(result.MissingFromCandidateTimestamps);
            Assert.Equal(2, result.ExtraInCandidateTimestamps.Count);
        }

        [Fact]
        public void Match_EmptyCandidate_AllBenchmarkMissing()
        {
            var benchmark = new[] { Ts(2026, 1, 2), Ts(2026, 1, 3) };
            var candidate = Array.Empty<DateTimeOffset>();

            var result = TimestampMatcher.Match(benchmark, candidate, 2, 0);

            Assert.Empty(result.MatchedTimestamps);
            Assert.Equal(2, result.MissingFromCandidateTimestamps.Count);
            Assert.Empty(result.ExtraInCandidateTimestamps);
        }

        [Fact]
        public void Match_SingleOverlap_Works()
        {
            var benchmark = new[] { Ts(2026, 1, 2) };
            var candidate = new[] { Ts(2026, 1, 2) };

            var result = TimestampMatcher.Match(benchmark, candidate, 1, 1);

            Assert.Single(result.MatchedTimestamps);
            Assert.Empty(result.MissingFromCandidateTimestamps);
            Assert.Empty(result.ExtraInCandidateTimestamps);
            Assert.Equal(Ts(2026, 1, 2), result.Coverage.OverlappingRangeStart);
            Assert.Equal(Ts(2026, 1, 2), result.Coverage.OverlappingRangeEnd);
        }

        [Fact]
        public void Match_CoverageInvariants_AlwaysSatisfied()
        {
            var benchmark = new[] { Ts(2026, 1, 2), Ts(2026, 1, 3), Ts(2026, 1, 6), Ts(2026, 1, 7) };
            var candidate = new[] { Ts(2026, 1, 2), Ts(2026, 1, 6), Ts(2026, 1, 8) };

            var result = TimestampMatcher.Match(benchmark, candidate, 4, 3);

            // BenchmarkRecordCount = MatchedCount + MissingFromCandidateCount
            Assert.Equal(result.Coverage.BenchmarkRecordCount,
                result.Coverage.MatchedCount + result.Coverage.MissingFromCandidateCount);
            // CandidateRecordCount = MatchedCount + ExtraInCandidateCount
            Assert.Equal(result.Coverage.CandidateRecordCount,
                result.Coverage.MatchedCount + result.Coverage.ExtraInCandidateCount);
        }

        [Fact]
        public void Match_MultipleMissingAndExtra_CorrectOrdering()
        {
            var benchmark = new[] { Ts(2026, 1, 2), Ts(2026, 1, 3), Ts(2026, 1, 5), Ts(2026, 1, 6) };
            var candidate = new[] { Ts(2026, 1, 2), Ts(2026, 1, 4), Ts(2026, 1, 6) };

            var result = TimestampMatcher.Match(benchmark, candidate, 4, 3);

            Assert.Equal(2, result.MatchedTimestamps.Count); // 1/2, 1/6
            Assert.Equal(2, result.MissingFromCandidateTimestamps.Count); // 1/3, 1/5
            Assert.Single(result.ExtraInCandidateTimestamps); // 1/4
            Assert.Equal(Ts(2026, 1, 3), result.MissingFromCandidateTimestamps[0]);
            Assert.Equal(Ts(2026, 1, 5), result.MissingFromCandidateTimestamps[1]);
            Assert.Equal(Ts(2026, 1, 4), result.ExtraInCandidateTimestamps[0]);
        }

        [Fact]
        public void Match_NullBenchmarkTimestamps_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => TimestampMatcher.Match(null!, Array.Empty<DateTimeOffset>(), 0, 0));
        }

        [Fact]
        public void Match_NullCandidateTimestamps_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => TimestampMatcher.Match(Array.Empty<DateTimeOffset>(), null!, 0, 0));
        }
    }
}
