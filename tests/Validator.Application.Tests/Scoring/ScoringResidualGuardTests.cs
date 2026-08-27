using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Findings;
using Xunit;

namespace Validator.Application.Tests.Scoring
{
    // The scoring paths that only a particular shape of input reaches: a weight
    // list that is absent or has a hole in it, a metric handed a negative count,
    // a report whose six metrics arrive out of order, a metric whose originating
    // check was never reported at all, and a zero expected-candle population.
    // Each decides whether a score is refused or quietly wrong, so each is proven.
    public sealed class ScoringResidualGuardTests
    {
        private static DetailedSummary Summary(
            long missing = 0, long duplicate = 0, long invalid = 0,
            long closed = 0, long gaps = 0, long malformed = 0) =>
            new(missing, duplicate, invalid, closed, gaps, malformed);

        private static IReadOnlyList<CheckExecution> AllCompleted() =>
        [
            new(CheckName.MissingCandles, CheckStatus.Completed),
            new(CheckName.DuplicateRecords, CheckStatus.Completed),
            new(CheckName.InvalidOhlc, CheckStatus.Completed),
            new(CheckName.ClosedMarketRecords, CheckStatus.Completed),
            new(CheckName.TimeGaps, CheckStatus.Completed),
            new(CheckName.MalformedRows, CheckStatus.Completed)
        ];

        // ------------------------------------------------------- weight parsing

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Parse_WithNoWeightsAtAll_StatesTheProblemAndTheAcceptedForm(string? value)
        {
            // An absent value is a caller mistake rather than a request for the
            // default weighting, so it is refused instead of being interpreted.
            var error = Assert.Throws<ScoreWeightFormatException>(() => ScoreWeightParser.Parse(value!));

            Assert.Contains("No weights were supplied.", error.Message, StringComparison.Ordinal);
            Assert.Contains("missingCandles", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Parse_WithAnEmptyEntryBetweenSeparators_IsRefusedRatherThanSkipped()
        {
            // Silently skipping a hole in the list would let a caller believe they
            // supplied six weights when they supplied five.
            var error = Assert.Throws<ScoreWeightFormatException>(() => ScoreWeightParser.Parse(
                "missingCandles=1,,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,timeGaps=1,malformedRows=1"));

            Assert.Contains("An empty weight entry was supplied.", error.Message, StringComparison.Ordinal);
        }

        // ------------------------------------------------------------ metric guards

        [Fact]
        public void MetricScore_WithANegativeCount_Throws()
        {
            // A count is a number of defects found. A negative one cannot be
            // reported, and it would drive a defect rate below zero if scored.
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => MetricScore.NotScored(
                FindingCategory.MalformedRow,
                MetricPopulationKind.ExaminedRows,
                "The examined-row population was zero.",
                count: -1));

            Assert.Equal("count", error.ParamName);
        }

        [Fact]
        public void ExcludedMetric_CarriesTheStateThatKeptItOutOfTheAverage()
        {
            // The state is how a reader tells "the check never ran" apart from
            // "the population was zero", so it is carried, not just the reason.
            var notApplicable = new ExcludedMetric(
                FindingCategory.MissingCandle, MetricScoreState.NotApplicable, "the sequence checks did not run");
            var notScored = new ExcludedMetric(
                FindingCategory.MalformedRow, MetricScoreState.NotScored, "the population was zero");

            Assert.Equal(MetricScoreState.NotApplicable, notApplicable.State);
            Assert.Equal(MetricScoreState.NotScored, notScored.State);
        }

        [Fact]
        public void DatasetScoreReport_WithSixMetricsOutOfCanonicalOrder_Throws()
        {
            // Six metrics is not enough on its own: the order is what makes two
            // reports comparable, so a complete set in the wrong order is refused.
            var outOfOrder = MetricPopulationMap.CanonicalOrder
                .Reverse()
                .Select(category => MetricScore.NotScored(
                    category,
                    MetricPopulationMap.KindFor(category),
                    "the population was zero"))
                .ToList();
            var weighting = ScoreWeightResolver.Default();
            var dataset = DatasetScore.Unavailable(
                DatasetAverageCalculator.NoScoredMetricReason,
                coveredCategories: [],
                excludedCategories: MetricPopulationMap.CanonicalOrder
                    .Select(category => new ExcludedMetric(
                        category, MetricScoreState.NotScored, "the population was zero"))
                    .ToList());

            var error = Assert.Throws<ArgumentException>(
                () => new DatasetScoreReport(outOfOrder, weighting, dataset));

            Assert.Equal("metrics", error.ParamName);
        }

        // ------------------------------------------------- unreported check status

        [Fact]
        public void Build_WhenACheckWasNeverReported_ScoresTheMetricFromItsPopulationAlone()
        {
            // The check list is how a metric learns it was not applicable. When a
            // check is absent altogether there is nothing to defer to, so the
            // metric is scored from its population rather than being guessed at.
            IReadOnlyList<CheckExecution> withoutMalformed =
            [
                new(CheckName.MissingCandles, CheckStatus.Completed),
                new(CheckName.DuplicateRecords, CheckStatus.Completed),
                new(CheckName.InvalidOhlc, CheckStatus.Completed),
                new(CheckName.ClosedMarketRecords, CheckStatus.Completed),
                new(CheckName.TimeGaps, CheckStatus.Completed)
            ];
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(50, 50, 0), expectedCandles: 84);

            var report = ScoreSectionBuilder.Build(
                Summary(), populations, withoutMalformed, ScoreWeightResolver.Default());

            var malformed = report.Metrics.Single(metric => metric.Category == FindingCategory.MalformedRow);
            Assert.Equal(MetricScoreState.Scored, malformed.State);
            Assert.Equal(50, malformed.Population);
        }

        // ------------------------------------------- zero expected-candle population

        [Fact]
        public void Build_WithZeroExpectedCandles_MarksTheTimeMetricsNotScoredWithTheirOwnPopulationReason()
        {
            // An evaluated range that contains no open-market slot at all has a
            // known population of zero rather than an unknown one. The rate is
            // undefined, so the metric is not scored and says which population
            // was empty rather than being credited as flawless.
            var populations = MetricPopulations.FromScanCoverage(new ScanCoverage(50, 50, 0), expectedCandles: 0);

            var report = ScoreSectionBuilder.Build(
                Summary(), populations, AllCompleted(), ScoreWeightResolver.Default());

            var missing = report.Metrics.Single(metric => metric.Category == FindingCategory.MissingCandle);
            Assert.Equal(MetricScoreState.NotScored, missing.State);
            Assert.Null(missing.Score);
            Assert.Contains("expected-candle", missing.Reason!, StringComparison.Ordinal);
        }
    }
}
