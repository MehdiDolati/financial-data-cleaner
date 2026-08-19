using System.Globalization;

namespace Validator.Cli.Tests;

// End-to-end proof of the scoring feature through the CLI: per-metric scores are
// hand-recalculable (US1), the average is hand-recalculable and narrows its
// coverage on the single-row fixture (US2), scores are opt-in and additive, the
// v1 conflict is rejected, weights are validated before the source is read, and
// scored runs are deterministic and leave the source untouched.
public sealed class ScoringE2ETests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string Fixture(string name) => Path.Combine(Fixtures, name);

    // The known-populations fixture over forex with a UTC offset: five accepted
    // rows (one a duplicate at 00:00, one invalid OHLC at 02:00), one malformed
    // row, and expected candles 00:00..04:00 with 03:00 missing.
    private static string[] ScoredKnownPopulations(params string[] extra) =>
    [
        Fixture("scoring-known-populations.csv"),
        "--timeframe", "H1", "--tz-offset", "+00:00", "--score", .. extra
    ];

    // --- US1: per-metric scores are present, ordered, and hand-recalculable ---

    [Fact]
    public async Task Score_ListsSixMetricsAfterTheSummaryEachHandRecalculable()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(ScoredKnownPopulations());

        Assert.Equal(1, result.ExitCode);
        var text = CoreValidationE2ETests.Normalize(result.StdOut);

        // The six summary lines still lead the report, unchanged.
        var lines = text.Split('\n');
        Assert.Equal("Missing candles: 1", lines[0]);
        Assert.Equal("Duplicate records: 1", lines[1]);
        Assert.Equal("Invalid OHLC: 1", lines[2]);
        Assert.Equal("Closed-market records: 0", lines[3]);
        Assert.Equal("Time gaps: 1", lines[4]);
        Assert.Equal("Malformed rows: 1", lines[5]);

        Assert.Contains("Quality scores (0-100, higher is better):", text, StringComparison.Ordinal);

        // Each score equals 100 x (population - count) / population:
        //   missing/time gaps: 100 x 4/5 = 80.00 (5 expected candles, 1 defect)
        //   duplicates/invalid: 100 x 4/5 = 80.00 (5 accepted rows, 1 defect)
        //   closed-market:      100.00 (0 of 5 accepted rows)
        //   malformed:          100 x 5/6 = 83.33 (6 examined rows, 1 malformed row)
        Assert.Contains("- Missing candles: 80.00 (count=1; population=5 expected candles;", text, StringComparison.Ordinal);
        Assert.Contains("- Duplicate records: 80.00 (count=1; population=5 accepted rows;", text, StringComparison.Ordinal);
        Assert.Contains("- Invalid OHLC: 80.00 (count=1; population=5 accepted rows;", text, StringComparison.Ordinal);
        Assert.Contains("- Closed-market records: 100.00 (count=0; population=5 accepted rows;", text, StringComparison.Ordinal);
        Assert.Contains("- Time gaps: 80.00 (count=1; population=5 expected candles;", text, StringComparison.Ordinal);
        Assert.Contains("- Malformed rows: 83.33 (count=1; population=6 examined rows;", text, StringComparison.Ordinal);
    }


    // --- US2: the average is hand-recalculable and covers all six here ---

    [Fact]
    public async Task Score_AverageIsTheWeightedMeanOfTheUnroundedScores()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(ScoredKnownPopulations());

        var text = CoreValidationE2ETests.Normalize(result.StdOut);

        // (80 + 80 + 80 + 100 + 80 + 500/6) / 6 = (420 + 83.333...) / 6
        //   = 503.333.../6 = 83.888... -> 83.89
        Assert.Contains("Dataset average: 83.89 (covers 6 of 6 metrics)", text, StringComparison.Ordinal);
    }

    // --- US2: the single-row fixture narrows coverage; time metrics excluded ---

    [Fact]
    public async Task Score_SingleRowFixture_ExcludesTimeMetricsAndNarrowsTheAverage()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
        [
            Fixture("scoring-single-row.csv"),
            "--timeframe", "H1", "--tz-offset", "+00:00", "--score"
        ]);

        Assert.Equal(0, result.ExitCode);
        var text = CoreValidationE2ETests.Normalize(result.StdOut);

        // With one row the sequence checks cannot run, so both time metrics are
        // not applicable and the average covers only the four record metrics.
        Assert.Contains("- Missing candles: not applicable (reason:", text, StringComparison.Ordinal);
        Assert.Contains("- Time gaps: not applicable (reason:", text, StringComparison.Ordinal);
        Assert.Contains("Dataset average: 100.00 (covers 4 of 6 metrics; excluded: Missing candles, Time gaps)", text, StringComparison.Ordinal);
    }

    // --- Opt-in and additive (SC-006): unscored path unchanged, scored adds only ---

    [Fact]
    public async Task Unscored_TextIsExactlyTheSixSummaryLinesWithNoScoringSection()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [Fixture("scoring-known-populations.csv"), "--timeframe", "H1", "--tz-offset", "+00:00"]);

        var text = CoreValidationE2ETests.Normalize(result.StdOut);
        Assert.DoesNotContain("Quality scores", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Dataset average", text, StringComparison.Ordinal);
        Assert.Equal(
            "Missing candles: 1\n" +
            "Duplicate records: 1\n" +
            "Invalid OHLC: 1\n" +
            "Closed-market records: 0\n" +
            "Time gaps: 1\n" +
            "Malformed rows: 1",
            text);

    }

    [Fact]
    public async Task Scored_FirstSixLinesAndExitCodeMatchTheUnscoredRun()
    {
        string[] baseArgs = [Fixture("scoring-known-populations.csv"), "--timeframe", "H1", "--tz-offset", "+00:00"];

        var unscored = await CoreValidationE2ETests.InvokeAsync(baseArgs);
        var scored = await CoreValidationE2ETests.InvokeAsync([.. baseArgs, "--score"]);

        Assert.Equal(unscored.ExitCode, scored.ExitCode);

        var unscoredLines = CoreValidationE2ETests.Normalize(unscored.StdOut).Split('\n');
        var scoredLines = CoreValidationE2ETests.Normalize(scored.StdOut).Split('\n');
        Assert.Equal(unscoredLines[..6], scoredLines[..6]);
    }

    // --- V1 conflict (FR-031): rejected before the source is read ---

    [Fact]
    public async Task Score_WithImplicitVersion1Json_IsRejectedWithExitTwoAndEmptyStdout()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [Fixture("scoring-known-populations.csv"), "--timeframe", "H1", "--score", "--format", "json"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));
        Assert.Contains("--report-version 2", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Score_WithExplicitVersion1Json_IsRejectedWithExitTwoAndEmptyStdout()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [Fixture("scoring-known-populations.csv"), "--timeframe", "H1", "--score", "--format", "json", "--report-version", "1"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));
        Assert.Contains("--report-version 2", result.StdErr, StringComparison.Ordinal);
    }


    // --- Weights validated before reading data (FR-024) ---

    [Fact]
    public async Task Score_WeightsWithoutScore_AreRejected()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
        [
            Fixture("scoring-known-populations.csv"), "--timeframe", "H1",
            "--score-weights", "missingCandles=1,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,timeGaps=1,malformedRows=1"
        ]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--score", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Score_InvalidWeights_ExitTwoBeforeAnyReport()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
        [
            Fixture("scoring-known-populations.csv"), "--timeframe", "H1", "--score",
            "--score-weights", "missingCandles=1"
        ]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));
        Assert.Contains("missingCandles", result.StdErr, StringComparison.Ordinal);
    }

    // --- Determinism and source safety (SC-004) ---

    [Fact]
    public async Task Scored_RunsAreByteIdenticalAndLeaveTheSourceUnchanged()
    {
        var source = Fixture("scoring-known-populations.csv");
        var before = await File.ReadAllBytesAsync(source);

        var first = await CoreValidationE2ETests.InvokeAsync(ScoredKnownPopulations());
        var second = await CoreValidationE2ETests.InvokeAsync(ScoredKnownPopulations());

        Assert.Equal(first.StdOut, second.StdOut);
        Assert.Equal(before, await File.ReadAllBytesAsync(source));
    }
}
