using System;
using Validator.Domain.Comparison;
using Validator.Domain.Scoring;
using Xunit;

namespace Validator.Domain.Tests.Comparison
{
    public class BenchmarkAgreementScoreTests
    {
        [Fact]
        public void Available_WithPositivePopulation_Succeeds()
        {
            var score = BenchmarkAgreementScore.Available(matchedPopulation: 100, materialDiscrepancyTimestamps: 5);

            Assert.NotNull(score.Score);
            Assert.NotNull(score.Score);
            Assert.Equal(95.00m, score.Score!.Value.Rounded);
            Assert.Equal(100, score.MatchedPopulation);
            Assert.Equal(5, score.MaterialDiscrepancyCount);
            Assert.Null(score.UnavailableReason);
        }

        [Fact]
        public void Available_WithZeroDiscrepancies_IsPerfect()
        {
            var score = BenchmarkAgreementScore.Available(matchedPopulation: 100, materialDiscrepancyTimestamps: 0);

            Assert.NotNull(score.Score);
            Assert.Equal(100.00m, score.Score!.Value.Rounded);
        }

        [Fact]
        public void Available_WithZeroPopulation_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                BenchmarkAgreementScore.Available(matchedPopulation: 0, materialDiscrepancyTimestamps: 0));
        }

        [Fact]
        public void Unavailable_WithReason_Succeeds()
        {
            var score = BenchmarkAgreementScore.Unavailable("No overlapping timestamps");

            Assert.Null(score.Score);
            Assert.Equal("No overlapping timestamps", score.UnavailableReason);
            Assert.Contains("matchedPopulation", score.Formula);
        }

        [Fact]
        public void Available_WithScore_AndReason_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => new BenchmarkAgreementScore(
                score: new ScoreValue(new ExactRatio(50, 1)),
                formula: "test",
                matchedPopulation: 100,
                materialDiscrepancyCount: 0,
                unavailableReason: "some reason"));

            Assert.Contains("available score must not carry an unavailability reason", ex.Message);
        }

        [Fact]
        public void Unavailable_WithoutReason_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => new BenchmarkAgreementScore(
                score: null,
                formula: "test",
                matchedPopulation: 100,
                materialDiscrepancyCount: 0,
                unavailableReason: null));

            Assert.Contains("unavailable score must carry a reason", ex.Message);
        }
    }
}
