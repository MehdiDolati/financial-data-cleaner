using System.Linq;
using Validator.Application.Scoring;
using Validator.Domain.Findings;
using Xunit;

namespace Validator.Application.Tests.Scoring
{
    // Every invalid weight input is rejected with a message that states both the
    // specific problem and the accepted form. A valid list resolves to a
    // caller-supplied weighting over all six metrics in canonical order.
    public sealed class ScoreWeightParsingTests
    {
        private const string Valid =
            "missingCandles=2,duplicateRecords=1,invalidOhlc=3,closedMarketRecords=1,timeGaps=2,malformedRows=1";

        [Fact]
        public void Parse_ValidSixPairs_ResolvesCallerSuppliedWeighting()
        {
            var weighting = ScoreWeightParser.Parse(Valid);

            Assert.Equal(ScoreWeightingSource.CallerSupplied, weighting.Source);
            Assert.Equal(2m, weighting.For(FindingCategory.MissingCandle).Weight);
            Assert.Equal(3m, weighting.For(FindingCategory.InvalidOhlc).Weight);
            Assert.Equal(
                MetricPopulationMap.CanonicalOrder.ToArray(),
                weighting.Weights.Select(weight => weight.Category).ToArray());
        }

        [Fact]
        public void Parse_ToleratesSurroundingWhitespace()
        {
            var weighting = ScoreWeightParser.Parse(
                " missingCandles = 2 , duplicateRecords=1, invalidOhlc=3 ,closedMarketRecords=1,timeGaps=2,malformedRows=1");

            Assert.Equal(2m, weighting.For(FindingCategory.MissingCandle).Weight);
        }

        [Fact]
        public void Parse_AllowsAZeroWeightWhenAnotherIsPositive()
        {
            var weighting = ScoreWeightParser.Parse(
                "missingCandles=0,duplicateRecords=1,invalidOhlc=0,closedMarketRecords=0,timeGaps=0,malformedRows=0");

            Assert.Equal(0m, weighting.For(FindingCategory.MissingCandle).Weight);
            Assert.Equal(1m, weighting.For(FindingCategory.DuplicateRecord).Weight);
        }

        [Theory]
        // Omits a metric (five pairs)
        [InlineData("missingCandles=1,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,timeGaps=1")]
        // Unknown metric name
        [InlineData("missingCandles=1,duplicateRecords=1,invalidOHLC=1,closedMarketRecords=1,timeGaps=1,malformedRows=1")]
        // Duplicate metric name (and thus also a missing one)
        [InlineData("timeGaps=1,timeGaps=2,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,malformedRows=1")]
        // Negative weight
        [InlineData("missingCandles=-1,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,timeGaps=1,malformedRows=1")]
        // Non-numeric weight
        [InlineData("missingCandles=high,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,timeGaps=1,malformedRows=1")]
        // Missing value
        [InlineData("missingCandles=,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,timeGaps=1,malformedRows=1")]
        // Wrong separator
        [InlineData("timeGaps=1;malformedRows=1")]
        // All weights zero
        [InlineData("missingCandles=0,duplicateRecords=0,invalidOhlc=0,closedMarketRecords=0,timeGaps=0,malformedRows=0")]
        // Exponent notation is outside the accepted narrow form
        [InlineData("missingCandles=1e2,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,timeGaps=1,malformedRows=1")]
        // Leading plus is rejected
        [InlineData("missingCandles=+1,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,timeGaps=1,malformedRows=1")]
        public void Parse_RejectedInput_ThrowsWithProblemAndAcceptedForm(string input)
        {
            var error = Assert.Throws<ScoreWeightFormatException>(() => ScoreWeightParser.Parse(input));

            // The accepted form is always echoed so the caller can self-correct.
            Assert.Contains("missingCandles", error.Message, System.StringComparison.Ordinal);
            Assert.Contains("non-negative", error.Message, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
