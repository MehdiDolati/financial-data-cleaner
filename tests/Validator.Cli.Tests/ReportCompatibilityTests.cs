using System.Text.Json;

namespace Validator.Cli.Tests;

// Feature 002 adds representations without changing the ones feature 001
// published: concise text stays exactly six lines, unversioned JSON stays v1,
// and v2 is reachable only by explicit opt-in. Verbose text is the detailed
// human-readable representation of the same facts as v2.
public sealed class ReportCompatibilityTests : IDisposable
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly string KnownDefects = Path.Combine(Fixtures, "known-defects.csv");

    private static readonly string[] SummaryLabels =
    [
        "Missing candles:",
        "Duplicate records:",
        "Invalid OHLC:",
        "Closed-market records:",
        "Time gaps:",
        "Malformed rows:"
    ];

    private readonly string _directory;

    public ReportCompatibilityTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"validator-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task DefaultText_RemainsTheConciseSixLineSummary()
    {
        var result = await CoreValidationE2ETests.InvokeAsync([KnownDefects, "--timeframe", "H1"]);

        var lines = CoreValidationE2ETests.Normalize(result.StdOut).Split('\n');
        Assert.Equal(6, lines.Length);
        for (var index = 0; index < SummaryLabels.Length; index++)
        {
            Assert.StartsWith(SummaryLabels[index], lines[index], StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UnversionedJson_RemainsIdenticalToExplicitVersionOne()
    {
        string[] baseArguments = [KnownDefects, "--timeframe", "H1", "--format", "json"];

        var unversioned = await CoreValidationE2ETests.InvokeAsync(baseArguments);
        var explicitV1 = await CoreValidationE2ETests.InvokeAsync([.. baseArguments, "--report-version", "1"]);

        Assert.Equal(unversioned.ExitCode, explicitV1.ExitCode);
        Assert.Equal(
            CoreValidationE2ETests.Normalize(unversioned.StdOut),
            CoreValidationE2ETests.Normalize(explicitV1.StdOut));
    }

    // v1 documents must not gain a v2 field, so a v1 consumer never has to
    // decide which contract it is looking at.
    [Fact]
    public async Task VersionOneJson_ExposesNoVersionTwoField()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [KnownDefects, "--timeframe", "H1", "--format", "json"]);

        using var document = JsonDocument.Parse(result.StdOut);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("contractVersion", names);
        Assert.DoesNotContain("findingSetComplete", names);
        Assert.DoesNotContain("checks", names);
        Assert.DoesNotContain("reconciliation", names);
        Assert.DoesNotContain("coverage", names);
        Assert.Contains("summary", names);
    }

    // --verbose is a text concern: it must not alter either JSON contract.
    [Fact]
    public async Task VerboseFlag_DoesNotChangeEitherJsonContract()
    {
        string[] v1 = [KnownDefects, "--timeframe", "H1", "--format", "json"];
        string[] v2 = [.. v1, "--report-version", "2"];

        var plainV1 = await CoreValidationE2ETests.InvokeAsync(v1);
        var verboseV1 = await CoreValidationE2ETests.InvokeAsync([.. v1, "--verbose"]);
        var plainV2 = await CoreValidationE2ETests.InvokeAsync(v2);
        var verboseV2 = await CoreValidationE2ETests.InvokeAsync([.. v2, "--verbose"]);

        Assert.Equal(
            CoreValidationE2ETests.Normalize(plainV1.StdOut),
            CoreValidationE2ETests.Normalize(verboseV1.StdOut));
        Assert.Equal(
            CoreValidationE2ETests.Normalize(plainV2.StdOut),
            CoreValidationE2ETests.Normalize(verboseV2.StdOut));
    }

    [Fact]
    public async Task VersionTwoWithoutJson_FailsArgumentValidation()
    {
        var result = await CoreValidationE2ETests.InvokeAsync([KnownDefects, "--report-version", "2"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));
        Assert.Contains("--report-version", result.StdErr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("v2")]
    [InlineData("")]
    public async Task UnsupportedReportVersion_FailsArgumentValidation(string version)
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [KnownDefects, "--format", "json", "--report-version", version]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));
    }

    [Fact]
    public async Task VerboseText_BeginsWithTheConciseSummaryThenTheLabeledSections()
    {
        var result = await CoreValidationE2ETests.InvokeAsync([KnownDefects, "--timeframe", "H1", "--verbose"]);

        var lines = CoreValidationE2ETests.Normalize(result.StdOut).Split('\n');
        for (var index = 0; index < SummaryLabels.Length; index++)
        {
            Assert.StartsWith(SummaryLabels[index], lines[index], StringComparison.Ordinal);
        }

        var text = CoreValidationE2ETests.Normalize(result.StdOut);
        var sections = new[]
        {
            "Report status",
            "Source identity",
            "Validation context",
            "Scan coverage",
            "Check execution",
            "Category reconciliation",
            "Findings"
        };

        var position = 0;
        foreach (var section in sections)
        {
            var found = text.IndexOf(section, position, StringComparison.Ordinal);
            Assert.True(found >= 0, $"Verbose text is missing the '{section}' section in the documented order.");
            position = found + section.Length;
        }
    }

    [Fact]
    public async Task VerboseText_StatesCompletenessAndBothCountsPerCategory()
    {
        var result = await CoreValidationE2ETests.InvokeAsync([KnownDefects, "--timeframe", "H1", "--verbose"]);
        var text = CoreValidationE2ETests.Normalize(result.StdOut);

        Assert.Contains("FindingsDetected", text, StringComparison.Ordinal);
        Assert.Contains("Sum of category counts (not unique root causes)", text, StringComparison.Ordinal);

        // Each reconciliation row states the summary count and the detailed
        // entry count, so a reader can see the two agree.
        foreach (var category in new[] { "MissingCandle", "DuplicateRecord", "InvalidOhlc", "ClosedMarketRecord", "TimeGap", "MalformedRow" })
        {
            Assert.Contains($"{category}: summaryCount=", text, StringComparison.Ordinal);
        }

        Assert.Contains("entryCount=", text, StringComparison.Ordinal);
        Assert.Contains("contributionSum=", text, StringComparison.Ordinal);
    }

    // Verbose text and v2 JSON describe the same run, so their substantive
    // facts must agree rather than drift apart per representation.
    [Fact]
    public async Task VerboseText_AgreesWithVersionTwoJsonOnSubstantiveFacts()
    {
        var verbose = await CoreValidationE2ETests.InvokeAsync([KnownDefects, "--timeframe", "H1", "--verbose"]);
        var json = await CoreValidationE2ETests.InvokeAsync(
            [KnownDefects, "--timeframe", "H1", "--format", "json", "--report-version", "2"]);

        Assert.Equal(verbose.ExitCode, json.ExitCode);

        using var document = JsonDocument.Parse(json.StdOut);
        var root = document.RootElement;
        var text = CoreValidationE2ETests.Normalize(verbose.StdOut);

        Assert.Contains(root.GetProperty("status").GetString()!, text, StringComparison.Ordinal);
        Assert.Contains(root.GetProperty("source").GetProperty("sha256").GetString()!, text, StringComparison.Ordinal);
        Assert.Contains(root.GetProperty("source").GetProperty("fileName").GetString()!, text, StringComparison.Ordinal);

        // Every finding in v2 appears in the verbose text by its reference.
        foreach (var finding in root.GetProperty("findings").EnumerateArray())
        {
            Assert.Contains(finding.GetProperty("reference").GetString()!, text, StringComparison.Ordinal);
        }
    }

    // Detailed text is never truncated, so the finding section lists every
    // finding the v2 document lists.
    [Fact]
    public async Task VerboseText_ListsEveryFindingWithoutTruncation()
    {
        var verbose = await CoreValidationE2ETests.InvokeAsync([KnownDefects, "--timeframe", "H1", "--verbose"]);
        var json = await CoreValidationE2ETests.InvokeAsync(
            [KnownDefects, "--timeframe", "H1", "--format", "json", "--report-version", "2"]);

        using var document = JsonDocument.Parse(json.StdOut);
        var expected = document.RootElement.GetProperty("findings").EnumerateArray().Count();
        var text = CoreValidationE2ETests.Normalize(verbose.StdOut);

        Assert.Equal(expected, text.Split('\n').Count(line => line.StartsWith("- reference=", StringComparison.Ordinal)));
    }

    // A missing candle has no physical line, so verbose text must label it
    // rather than invent a position.
    [Fact]
    public async Task VerboseText_LabelsInapplicableValuesInsteadOfInventingThem()
    {
        var input = Path.Combine(_directory, "gap.csv");
        await File.WriteAllLinesAsync(input,
        [
            "2026.01.05,00:00,1,2,0.5,1.5,10",
            "2026.01.05,03:00,1,2,0.5,1.5,10"
        ]);

        var result = await CoreValidationE2ETests.InvokeAsync(
            [input, "--timeframe", "H1", "--tz-offset", "+00:00", "--verbose"]);
        var text = CoreValidationE2ETests.Normalize(result.StdOut);

        Assert.Contains("missing-candle:", text, StringComparison.Ordinal);
        Assert.Contains("not applicable", text, StringComparison.Ordinal);
    }

    // Hostile source text cannot forge a heading or an extra finding line: the
    // value is quoted and its control characters are escaped.
    [Fact]
    public async Task VerboseText_EscapesSourceDerivedTextSoItCannotForgeStructure()
    {
        var input = Path.Combine(_directory, "hostile.csv");
        await File.WriteAllLinesAsync(input,
        [
            "2026.01.05,00:00,1,2,0.5,1.5,10",
            "\"2026.01.05\tFindings\",\"00:00\nReport status\",1,2,0.5,1.5,10"
        ]);

        var result = await CoreValidationE2ETests.InvokeAsync(
            [input, "--timeframe", "H1", "--tz-offset", "+00:00", "--verbose"]);
        var text = CoreValidationE2ETests.Normalize(result.StdOut);
        var lines = text.Split('\n');

        // A tab or newline inside a source value is escaped, so it can neither
        // start a new line nor forge one of the documented headings.
        Assert.DoesNotContain("\t", text, StringComparison.Ordinal);
        Assert.Equal(1, lines.Count(line => line == "Report status:"));
        Assert.Equal(1, lines.Count(line => line == "Findings:"));
    }

    [Fact]
    public async Task VerboseText_WrittenToAFileMatchesTheTextWrittenToStandardOutput()
    {
        var destination = Path.Combine(_directory, "verbose.txt");
        string[] arguments = [KnownDefects, "--timeframe", "H1", "--verbose"];

        var standardOutputRun = await CoreValidationE2ETests.InvokeAsync(arguments);
        var fileRun = await CoreValidationE2ETests.InvokeAsync([.. arguments, "--output", destination]);

        Assert.Equal(standardOutputRun.ExitCode, fileRun.ExitCode);
        Assert.Equal(
            CoreValidationE2ETests.Normalize(standardOutputRun.StdOut),
            CoreValidationE2ETests.Normalize(await File.ReadAllTextAsync(destination)));
    }

    [Fact]
    public async Task VerboseText_OnACleanSourceStatesCleanAndExitsZero()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [Path.Combine(Fixtures, "clean-forex-h1.csv"), "--verbose"]);

        Assert.Equal(0, result.ExitCode);
        var text = CoreValidationE2ETests.Normalize(result.StdOut);
        Assert.Contains("Clean", text, StringComparison.Ordinal);
        Assert.Contains("Findings", text, StringComparison.Ordinal);
    }

    // A fatal run keeps the actionable text diagnostic for text requests; it is
    // never presented as a successful report.
    [Fact]
    public async Task VerboseText_OnAFatalSourceKeepsTheActionableTextDiagnostic()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [Path.Combine(Fixtures, "missing-close-column.csv"), "--verbose"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
    }
}
