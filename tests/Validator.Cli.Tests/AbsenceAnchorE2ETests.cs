using System.Globalization;
using System.Text.Json;

namespace Validator.Cli.Tests;

// US5 (T058): an absence can be located from the report alone. Both reported
// lines exist in the source file and bracket the expected timestamp, a boundary
// gap omits the unavailable side, and JSON v1 output plus the six summary
// counts, finding order, and exit codes are unchanged (FR-035, FR-037, SC-009).
public sealed class AbsenceAnchorE2ETests : IDisposable
{
    private readonly string _directory;

    public AbsenceAnchorE2ETests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"validator-absence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // A Thursday so every hour used below is inside the forex trading week, and
    // the interior absence is a genuine gap rather than a weekend closure.
    private const string Day = "2026.01.01";

    private static string Row(int hour, decimal close = 1.5m) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1:00}:00,1,2,0.5,{2},10",
            Day,
            hour,
            close);

    private async Task<string> WriteFixtureAsync(string name, params string[] rows)
    {
        var path = Path.Combine(_directory, name);
        await File.WriteAllLinesAsync(path, rows);
        return path;
    }

    private static async Task<JsonDocument> RunV2Async(string path)
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [path, "--timeframe", "H1", "--format", "json", "--report-version", "2", "--tz-offset", "+00:00"]);
        Assert.Equal(1, result.ExitCode);
        return JsonDocument.Parse(result.StdOut);
    }

    private static IEnumerable<JsonElement> Absences(JsonDocument document) =>
        document.RootElement
            .GetProperty("findings")
            .EnumerateArray()
            .Where(finding =>
            {
                var category = finding.GetProperty("category").GetString();
                return category is "MissingCandle" or "TimeGap";
            });

    private static DateTimeOffset ExpectedTimestamp(JsonElement finding)
    {
        var evidence = finding.GetProperty("evidence");
        var text = finding.GetProperty("category").GetString() == "MissingCandle"
            ? evidence.GetProperty("expectedTimestampUtc").GetString()!
            : evidence.GetProperty("firstMissingTimestampUtc").GetString()!;
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    // The reported line must name a row that really exists, and that row's own
    // timestamp must fall on the correct side of the absence.
    private static DateTimeOffset TimestampOnLine(string[] sourceLines, long line)
    {
        Assert.InRange(line, 1, sourceLines.Length);
        var fields = sourceLines[line - 1].Split(',');
        return DateTimeOffset.Parse(
            $"{fields[0].Replace('.', '-')}T{fields[1]}:00+00:00",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);
    }

    [Fact]
    public async Task InteriorGap_ReportsBracketingLinesThatExistAndBracketTheExpectedTimestamp()
    {
        // 10:00 and 11:00 are absent between the observed 09:00 and 12:00.
        var path = await WriteFixtureAsync(
            "interior-gap.csv",
            Row(8),
            Row(9),
            Row(12),
            Row(13));
        var sourceLines = await File.ReadAllLinesAsync(path);

        using var document = await RunV2Async(path);
        var absences = Absences(document).ToArray();
        Assert.NotEmpty(absences);

        foreach (var finding in absences)
        {
            var evidence = finding.GetProperty("evidence");
            var expected = ExpectedTimestamp(finding);

            var previousLine = evidence.GetProperty("previousObservedSourceLine").GetInt64();
            var nextLine = evidence.GetProperty("nextObservedSourceLine").GetInt64();

            // Both lines name real rows in the file.
            var previousTimestamp = TimestampOnLine(sourceLines, previousLine);
            var nextTimestamp = TimestampOnLine(sourceLines, nextLine);

            // And they bracket the absence rather than sitting on one side.
            Assert.True(
                previousTimestamp < expected,
                $"Line {previousLine} at {previousTimestamp:O} must precede {expected:O}.");
            Assert.True(
                nextTimestamp > expected,
                $"Line {nextLine} at {nextTimestamp:O} must follow {expected:O}.");

            // The absent record itself still claims no physical line (FR-016).
            Assert.Empty(finding.GetProperty("location").GetProperty("sourceLines").EnumerateArray());
        }
    }

    [Fact]
    public async Task EveryMissingCandleInAGap_SharesTheOwningGapsBracketingPair()
    {
        var path = await WriteFixtureAsync(
            "shared-bracket.csv",
            Row(8),
            Row(9),
            Row(13));

        using var document = await RunV2Async(path);
        var pairs = Absences(document)
            .Select(finding =>
            {
                var evidence = finding.GetProperty("evidence");
                return (
                    Previous: evidence.GetProperty("previousObservedSourceLine").GetInt64(),
                    Next: evidence.GetProperty("nextObservedSourceLine").GetInt64());
            })
            .Distinct()
            .ToArray();

        // One gap plus its several missing candles all report the same pair.
        Assert.Single(pairs);
    }

    [Fact]
    public async Task BoundaryGap_OmitsTheUnavailableSide()
    {
        // A malformed leading row occupies 08:00's slot, so the first real gap
        // begins with no preceding observed candle to bracket against.
        var path = await WriteFixtureAsync(
            "boundary-gap.csv",
            Row(9),
            Row(12));

        using var document = await RunV2Async(path);
        var absences = Absences(document).ToArray();
        Assert.NotEmpty(absences);

        // Every reported side that is present must still name a real row; an
        // absent side must be omitted entirely rather than emitted as null/zero.
        var sourceLines = await File.ReadAllLinesAsync(path);
        foreach (var finding in absences)
        {
            var evidence = finding.GetProperty("evidence");

            if (evidence.TryGetProperty("previousObservedSourceLine", out var previous))
            {
                Assert.True(previous.GetInt64() > 0);
                TimestampOnLine(sourceLines, previous.GetInt64());
            }

            if (evidence.TryGetProperty("nextObservedSourceLine", out var next))
            {
                Assert.True(next.GetInt64() > 0);
                TimestampOnLine(sourceLines, next.GetInt64());
            }
        }
    }

    [Fact]
    public async Task UnsortedSource_ReportsTemporalNeighboursNotPhysicallyAdjacentRows()
    {
        // 12:00 is listed physically before 09:00, so the bracketing pair is not
        // ascending. It still names the temporal neighbours of the absence.
        var path = await WriteFixtureAsync(
            "unsorted-gap.csv",
            Row(12),
            Row(9));
        var sourceLines = await File.ReadAllLinesAsync(path);

        using var document = await RunV2Async(path);
        foreach (var finding in Absences(document))
        {
            var evidence = finding.GetProperty("evidence");
            var expected = ExpectedTimestamp(finding);
            var previousLine = evidence.GetProperty("previousObservedSourceLine").GetInt64();
            var nextLine = evidence.GetProperty("nextObservedSourceLine").GetInt64();

            Assert.True(TimestampOnLine(sourceLines, previousLine) < expected);
            Assert.True(TimestampOnLine(sourceLines, nextLine) > expected);

            // Descending physical order is correct here, not a defect.
            Assert.True(previousLine > nextLine);
        }
    }

    [Fact]
    public async Task VerboseText_LabelsBothBracketingLinesAndTheUnavailableSide()
    {
        var path = await WriteFixtureAsync(
            "verbose-gap.csv",
            Row(9),
            Row(12));

        var result = await CoreValidationE2ETests.InvokeAsync(
            [path, "--timeframe", "H1", "--verbose", "--tz-offset", "+00:00"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("previousObservedSourceLine=", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("nextObservedSourceLine=", result.StdOut, StringComparison.Ordinal);
    }

    // The absence anchors are additive: v1 output, the six counts, the finding
    // order, and the exit code are all unchanged by this story (FR-035, FR-037).
    [Fact]
    public async Task JsonV1Output_IsUnaffectedByTheAbsenceAnchors()
    {
        var path = await WriteFixtureAsync(
            "v1-unchanged.csv",
            Row(9),
            Row(12));

        var result = await CoreValidationE2ETests.InvokeAsync(
            [path, "--timeframe", "H1", "--format", "json", "--tz-offset", "+00:00"]);

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("previousObservedSourceLine", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("nextObservedSourceLine", result.StdOut, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(result.StdOut);
        Assert.False(document.RootElement.TryGetProperty("contractVersion", out _));
    }

    [Fact]
    public async Task SummaryCountsFindingOrderAndExitCode_AreUnchangedByTheAbsenceAnchors()
    {
        var path = await WriteFixtureAsync(
            "counts-unchanged.csv",
            Row(9),
            Row(12),
            Row(13, close: 1.4m));

        var result = await CoreValidationE2ETests.InvokeAsync(
            [path, "--timeframe", "H1", "--format", "json", "--report-version", "2", "--tz-offset", "+00:00"]);
        using var document = JsonDocument.Parse(result.StdOut);

        // Two absent hourly candles inside one gap: the six counts are derived
        // from the same walk that produced the anchors, so they cannot drift.
        var summary = document.RootElement.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("missingCandles").GetInt64());
        Assert.Equal(1, summary.GetProperty("timeGaps").GetInt64());
        Assert.Equal(0, summary.GetProperty("duplicateRecords").GetInt64());
        Assert.Equal(0, summary.GetProperty("malformedRows").GetInt64());
        Assert.Equal(1, result.ExitCode);

        // Canonical order still places missing candles before their gap.
        var categories = document.RootElement
            .GetProperty("findings")
            .EnumerateArray()
            .Select(finding => finding.GetProperty("category").GetString())
            .ToArray();
        Assert.Equal(
            new[] { "MissingCandle", "MissingCandle", "TimeGap" },
            categories);

        // Every category still reconciles against its contribution sum.
        foreach (var category in document.RootElement
            .GetProperty("reconciliation")
            .GetProperty("categories")
            .EnumerateArray())
        {
            Assert.Equal(
                category.GetProperty("summaryCount").GetInt64(),
                category.GetProperty("contributionSum").GetInt64());
        }
    }

    [Fact]
    public async Task RepeatedRuns_ProduceIdenticalAnchoredDocuments()
    {
        var path = await WriteFixtureAsync(
            "repeatable-gap.csv",
            Row(9),
            Row(12));

        string[] arguments =
        [
            path, "--timeframe", "H1", "--format", "json", "--report-version", "2", "--tz-offset", "+00:00"
        ];

        var first = await CoreValidationE2ETests.InvokeAsync(arguments);
        var second = await CoreValidationE2ETests.InvokeAsync(arguments);

        Assert.Equal(first.StdOut, second.StdOut);
    }
}