using System.Collections.Generic;
using System.Linq;
using System.Text;
using Validator.Application.Scoring;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;
using Validator.Infrastructure.Reporting;
using Xunit;

namespace Validator.Infrastructure.Tests.Reporting
{
    // The scoring section renders after the six summary lines with every metric
    // in the established order, each stating its state and — when scored — its
    // count, population, population kind, weight, and share, followed by the
    // average line with coverage or an explicit unavailability.
    public sealed class ScoringTextSectionWriterTests
    {
        private static ScoreValue Score(long numerator, long denominator) =>
            new(new ExactRatio(numerator, denominator));

        private static ScoreWeighting Weighting(IEnumerable<decimal?> shares)
        {
            var shareList = shares.ToList();
            var weights = MetricPopulationMap.CanonicalOrder
                .Select((category, index) => new MetricWeight(category, 1m, shareList[index]))
                .ToList();
            return new ScoreWeighting(ScoreWeightingSource.Default, weights);
        }

        private static string Render(DatasetScoreReport score)
        {
            var buffer = new StringBuilder();
            ScoringTextSectionWriter.Append(buffer, score);
            return buffer.ToString();
        }

        [Fact]
        public void Append_ListsAllSixMetricsInCanonicalOrderAfterTheHeading()
        {
            var metrics = new List<MetricScore>
            {
                MetricScore.Scored(FindingCategory.MissingCandle, 1, 84, MetricPopulationKind.ExpectedCandles, Score(8300, 84)),
                MetricScore.Scored(FindingCategory.DuplicateRecord, 1, 50, MetricPopulationKind.AcceptedRows, Score(98, 1)),
                MetricScore.Scored(FindingCategory.InvalidOhlc, 2, 50, MetricPopulationKind.AcceptedRows, Score(96, 1)),
                MetricScore.Scored(FindingCategory.ClosedMarketRecord, 0, 50, MetricPopulationKind.AcceptedRows, Score(100, 1)),
                MetricScore.Scored(FindingCategory.TimeGap, 2, 84, MetricPopulationKind.ExpectedCandles, Score(8200, 84)),
                MetricScore.Scored(FindingCategory.MalformedRow, 0, 50, MetricPopulationKind.ExaminedRows, Score(100, 1))
            };
            var weighting = Weighting(Enumerable.Repeat<decimal?>(0.17m, 6));
            var dataset = DatasetScore.Available(
                Score(9840470, 100000),
                MetricPopulationMap.CanonicalOrder.ToList(),
                []);
            var report = new DatasetScoreReport(metrics, weighting, dataset);

            var text = Render(report);
            var lines = text.Split('\n');

            Assert.Equal(ScoringTextSectionWriter.Heading, lines[0]);
            Assert.StartsWith("- Missing candles: 98.81 (count=1; population=84 expected candles; weight=1; share=0.17)", lines[1]);
            Assert.StartsWith("- Duplicate records: 98.00 (count=1; population=50 accepted rows;", lines[2]);
            Assert.StartsWith("- Invalid OHLC: 96.00 (count=2; population=50 accepted rows;", lines[3]);
            Assert.StartsWith("- Closed-market records: 100.00 (count=0; population=50 accepted rows;", lines[4]);
            Assert.StartsWith("- Time gaps: 97.62 (count=2; population=84 expected candles;", lines[5]);
            Assert.StartsWith("- Malformed rows: 100.00 (count=0; population=50 examined rows;", lines[6]);
            Assert.Equal("Dataset average: 98.40 (covers 6 of 6 metrics)", lines[7]);
        }

        [Fact]
        public void Append_NotApplicableMetrics_StateTheirReasonAndTheAverageNarrows()
        {
            const string reason = "Fewer than two open-market timestamps bound an expected sequence.";
            var metrics = new List<MetricScore>
            {
                MetricScore.NotApplicable(FindingCategory.MissingCandle, MetricPopulationKind.ExpectedCandles, reason),
                MetricScore.Scored(FindingCategory.DuplicateRecord, 1, 50, MetricPopulationKind.AcceptedRows, Score(98, 1)),
                MetricScore.Scored(FindingCategory.InvalidOhlc, 2, 50, MetricPopulationKind.AcceptedRows, Score(96, 1)),
                MetricScore.Scored(FindingCategory.ClosedMarketRecord, 0, 50, MetricPopulationKind.AcceptedRows, Score(100, 1)),
                MetricScore.NotApplicable(FindingCategory.TimeGap, MetricPopulationKind.ExpectedCandles, reason),
                MetricScore.Scored(FindingCategory.MalformedRow, 0, 50, MetricPopulationKind.ExaminedRows, Score(100, 1))
            };
            var weighting = Weighting([null, 0.25m, 0.25m, 0.25m, null, 0.25m]);
            var dataset = DatasetScore.Available(
                Score(9850, 100),
                [FindingCategory.DuplicateRecord, FindingCategory.InvalidOhlc, FindingCategory.ClosedMarketRecord, FindingCategory.MalformedRow],
                [
                    new ExcludedMetric(FindingCategory.MissingCandle, MetricScoreState.NotApplicable, reason),
                    new ExcludedMetric(FindingCategory.TimeGap, MetricScoreState.NotApplicable, reason)
                ]);
            var report = new DatasetScoreReport(metrics, weighting, dataset);

            var text = Render(report);

            Assert.Contains($"- Missing candles: not applicable (reason: {reason})", text, System.StringComparison.Ordinal);
            Assert.Contains($"- Time gaps: not applicable (reason: {reason})", text, System.StringComparison.Ordinal);
            Assert.Contains("Dataset average: 98.50 (covers 4 of 6 metrics; excluded: Missing candles, Time gaps)", text, System.StringComparison.Ordinal);
        }

        [Fact]
        public void Append_UnavailableAverage_StatesTheReasonAndNoNumber()
        {
            const string zero = "The accepted-row population was zero, so the rate is undefined.";
            var metrics = MetricPopulationMap.CanonicalOrder
                .Select(category => MetricScore.NotScored(category, MetricPopulationMap.KindFor(category), zero))
                .ToList();
            var weighting = Weighting(Enumerable.Repeat<decimal?>(null, 6));
            var dataset = DatasetScore.Unavailable(
                DatasetAverageCalculator.NoScoredMetricReason,
                [],
                MetricPopulationMap.CanonicalOrder
                    .Select(category => new ExcludedMetric(category, MetricScoreState.NotScored, zero)).ToList());
            var report = new DatasetScoreReport(metrics, weighting, dataset);

            var text = Render(report);

            Assert.Contains("Dataset average: not available (reason: no metric could be scored)", text, System.StringComparison.Ordinal);
            Assert.DoesNotContain("covers", text, System.StringComparison.Ordinal);
        }
    }
}
