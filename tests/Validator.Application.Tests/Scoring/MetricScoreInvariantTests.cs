using System;
using Validator.Application.Scoring;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;
using Xunit;

namespace Validator.Application.Tests.Scoring
{
    // MetricScore enforces the core safety invariant of the whole feature: a
    // score exists exactly when the metric is Scored, and a reason exists exactly
    // when it is not. No instance can be both unscored and valued, so no metric
    // can be silently credited as flawless.
    public sealed class MetricScoreInvariantTests
    {
        private static ScoreValue Score(long numerator, long denominator) =>
            new(new ExactRatio(numerator, denominator));

        [Fact]
        public void Scored_RequiresAScoreAndNoReason()
        {
            var score = MetricScore.Scored(
                FindingCategory.DuplicateRecord,
                count: 1,
                population: 50,
                MetricPopulationKind.AcceptedRows,
                Score(9800, 100));

            Assert.Equal(MetricScoreState.Scored, score.State);
            Assert.NotNull(score.Score);
            Assert.Null(score.Reason);
        }

        [Fact]
        public void NotApplicable_RequiresAReasonAndNoScore()
        {
            var score = MetricScore.NotApplicable(
                FindingCategory.MissingCandle,
                MetricPopulationKind.ExpectedCandles,
                "Fewer than two open-market timestamps bound an expected sequence.");

            Assert.Equal(MetricScoreState.NotApplicable, score.State);
            Assert.Null(score.Score);
            Assert.False(string.IsNullOrWhiteSpace(score.Reason));
        }

        [Fact]
        public void NotScored_RequiresAReasonAndNoScore()
        {
            var score = MetricScore.NotScored(
                FindingCategory.MalformedRow,
                MetricPopulationKind.ExaminedRows,
                "The examined-row population was zero.");

            Assert.Equal(MetricScoreState.NotScored, score.State);
            Assert.Null(score.Score);
            Assert.False(string.IsNullOrWhiteSpace(score.Reason));
        }

        [Fact]
        public void Scored_WithABlankReason_IsNotConstructible()
        {
            // The factory keeps a scored metric's reason null; a scored metric can
            // never carry an explanation because it needs none.
            var score = MetricScore.Scored(
                FindingCategory.InvalidOhlc,
                count: 0,
                population: 50,
                MetricPopulationKind.AcceptedRows,
                Score(10000, 100));

            Assert.Null(score.Reason);
        }

        [Fact]
        public void NotApplicable_WithABlankReason_Throws()
        {
            Assert.Throws<ArgumentException>(() => MetricScore.NotApplicable(
                FindingCategory.TimeGap,
                MetricPopulationKind.ExpectedCandles,
                "   "));
        }

        [Fact]
        public void NotScored_WithABlankReason_Throws()
        {
            Assert.Throws<ArgumentException>(() => MetricScore.NotScored(
                FindingCategory.MalformedRow,
                MetricPopulationKind.ExaminedRows,
                ""));
        }
    }
}
