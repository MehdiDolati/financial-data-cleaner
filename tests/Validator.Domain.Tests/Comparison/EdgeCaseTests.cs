using Validator.Domain.Comparison;
using Xunit;

namespace Validator.Domain.Tests.Comparison
{
    public class EdgeCaseTests
    {
        [Fact]
        public void ZeroPrice_AbsoluteToleranceApplies()
        {
            var result = FieldComparator.Compare(0m, 0.00005m, 0.00010m, 0.0001m);
            Assert.IsType<ToleranceDecision.AcceptedByAbsolute>(result);
        }

        [Fact]
        public void ZeroPrice_ExceedsAbsolute_IsMaterial()
        {
            var result = FieldComparator.Compare(0m, 0.00020m, 0.00010m, 0.0001m);
            Assert.IsType<ToleranceDecision.MaterialDifference>(result);
        }

        [Fact]
        public void ZeroPrice_BothZero_IsAccepted()
        {
            var result = FieldComparator.Compare(0m, 0m, 0m, 0m);
            Assert.IsType<ToleranceDecision.AcceptedByAbsolute>(result);
        }

        [Fact]
        public void SingleOverlap_MatchCountIsOne()
        {
            var benchmark = new[] { new DateTimeOffset(2026, 1, 2, 0, 0, 0, System.TimeSpan.Zero) };
            var candidate = new[] { new DateTimeOffset(2026, 1, 2, 0, 0, 0, System.TimeSpan.Zero) };

            var result = TimestampMatcher.Match(benchmark, candidate, 1, 1);

            Assert.Single(result.MatchedTimestamps);
            Assert.Equal(1, result.Coverage.MatchedCount);
            Assert.Equal(0, result.Coverage.MissingFromCandidateCount);
            Assert.Equal(0, result.Coverage.ExtraInCandidateCount);
        }

        [Fact]
        public void IdenticalTextualPrecision_DecimalComparison()
        {
            // Both values have identical decimal representation but may differ in text
            var result = FieldComparator.Compare(0.63421m, 0.63421m, 0.00010m, 0.0001m);
            Assert.IsType<ToleranceDecision.AcceptedByAbsolute>(result);
        }

        [Fact]
        public void VerySmallDifference_WithinTolerance()
        {
            // 1e-8 difference should be within any reasonable tolerance
            var result = FieldComparator.Compare(0.63421000m, 0.63421001m, 0.00010m, 0.0001m);
            Assert.IsType<ToleranceDecision.AcceptedByAbsolute>(result);
        }

        [Fact]
        public void LargeVolume_WithinRelativeTolerance()
        {
            // 1,000,000 vs 1,050,000 = 5% difference, within 5% relative tolerance
            var result = FieldComparator.Compare(1000000m, 1050000m, 0m, 0.05m);
            Assert.IsType<ToleranceDecision.AcceptedByRelative>(result);
        }

        [Fact]
        public void LargeVolume_ExceedsRelative_IsMaterial()
        {
            // 1,000,000 vs 1,060,000 = 6% difference, exceeds 5% relative tolerance
            var result = FieldComparator.Compare(1000000m, 1060000m, 0m, 0.05m);
            Assert.IsType<ToleranceDecision.MaterialDifference>(result);
        }

        [Fact]
        public void NegativePrice_DifferenceIsAbsolute()
        {
            // Even if benchmark is negative (unlikely but possible in some contexts)
            // The absolute difference should still be computed correctly
            var result = FieldComparator.Compare(-1.0m, -1.00005m, 0.00010m, 0.0001m);
            Assert.IsType<ToleranceDecision.AcceptedByAbsolute>(result);
        }

        [Fact]
        public void TimestampMatcher_LargeDataset_AllMatched()
        {
            var timestamps = new System.Collections.Generic.List<System.DateTimeOffset>();
            for (int i = 0; i < 10000; i++)
            {
                timestamps.Add(new DateTimeOffset(2020, 1, 1, 0, 0, 0, System.TimeSpan.Zero).AddDays(i));
            }

            var result = TimestampMatcher.Match(timestamps, timestamps, 10000, 10000);

            Assert.Equal(10000, result.MatchedTimestamps.Count);
            Assert.Empty(result.MissingFromCandidateTimestamps);
            Assert.Empty(result.ExtraInCandidateTimestamps);
            Assert.Equal(10000, result.Coverage.MatchedCount);
        }

        [Fact]
        public void TimestampMatcher_LargeDataset_PartialOverlap()
        {
            var benchmarkTimestamps = new System.Collections.Generic.List<System.DateTimeOffset>();
            var candidateTimestamps = new System.Collections.Generic.List<System.DateTimeOffset>();

            // Benchmark: 0-4999
            for (int i = 0; i < 5000; i++)
            {
                benchmarkTimestamps.Add(new DateTimeOffset(2020, 1, 1, 0, 0, 0, System.TimeSpan.Zero).AddDays(i));
            }
            // Candidate: 1000-5999
            for (int i = 1000; i < 6000; i++)
            {
                candidateTimestamps.Add(new DateTimeOffset(2020, 1, 1, 0, 0, 0, System.TimeSpan.Zero).AddDays(i));
            }

            var result = TimestampMatcher.Match(benchmarkTimestamps, candidateTimestamps, 5000, 5000);

            Assert.Equal(4000, result.MatchedTimestamps.Count); // 1000-4999
            Assert.Equal(1000, result.MissingFromCandidateTimestamps.Count); // 0-999
            Assert.Equal(1000, result.ExtraInCandidateTimestamps.Count); // 5000-5999
        }

        [Fact]
        public void ComparisonCoverage_Invariants_HoldForAllCases()
        {
            // Test that invariants hold for various scenarios
            var scenarios = new[]
            {
                (benchmark: 0L, candidate: 0L, matched: 0L, missing: 0L, extra: 0L),
                (benchmark: 5L, candidate: 5L, matched: 5L, missing: 0L, extra: 0L),
                (benchmark: 5L, candidate: 3L, matched: 3L, missing: 2L, extra: 0L),
                (benchmark: 3L, candidate: 5L, matched: 3L, missing: 0L, extra: 2L),
                (benchmark: 5L, candidate: 5L, matched: 3L, missing: 2L, extra: 2L),
            };

            foreach (var (benchmark, candidate, matched, missing, extra) in scenarios)
            {
                var coverage = new ComparisonCoverage(benchmark, candidate, matched, missing, extra);
                Assert.Equal(coverage.BenchmarkRecordCount, coverage.MatchedCount + coverage.MissingFromCandidateCount);
                Assert.Equal(coverage.CandidateRecordCount, coverage.MatchedCount + coverage.ExtraInCandidateCount);
            }
        }
    }
}
