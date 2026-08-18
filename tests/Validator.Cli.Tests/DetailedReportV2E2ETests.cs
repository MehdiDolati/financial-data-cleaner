using System.Text.Json;

namespace Validator.Cli.Tests;

// End-to-end invariants for the explicitly selected JSON v2 contract: stdout
// carries exactly one complete detailed document, the six established category
// meanings are preserved from v1, a fatal run emits exactly one fatal document
// on stderr with empty stdout, and v1 remains the default.
public sealed class DetailedReportV2E2ETests : IDisposable
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly string _directory;

    public DetailedReportV2E2ETests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"validator-v2-{Guid.NewGuid():N}");
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
    public async Task ReportVersion2_CleanSource_EmitsOneCompleteDocumentAndExitsZero()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [Path.Combine(Fixtures, "clean-forex-h1.csv"), "--format", "json", "--report-version", "2"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdErr));

        using var document = JsonDocument.Parse(result.StdOut);
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("contractVersion").GetInt32());
        Assert.Equal("Clean", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("findingSetComplete").GetBoolean());
        Assert.Empty(root.GetProperty("findings").EnumerateArray());

        // Source identity fingerprints the exact validated bytes without leaking a path.
        var source = root.GetProperty("source");
        Assert.Equal("clean-forex-h1.csv", source.GetProperty("fileName").GetString());
        Assert.Equal(64, source.GetProperty("sha256").GetString()!.Length);
        Assert.True(source.GetProperty("byteSize").GetInt64() > 0);

        // Every applicable check completed and the counts reconcile.
        var checks = root.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Equal(6, checks.Length);
        Assert.All(checks, check => Assert.NotEqual("NotCompleted", check.GetProperty("status").GetString()));
        Assert.True(root.GetProperty("reconciliation").GetProperty("coverageReconciled").GetBoolean());

        var coverage = root.GetProperty("coverage");
        Assert.Equal(
            coverage.GetProperty("physicalRowsExamined").GetInt64(),
            coverage.GetProperty("acceptedRows").GetInt64() + coverage.GetProperty("malformedRows").GetInt64());
    }

    [Fact]
    public async Task ReportVersion2_PreservesTheSixCategoryCountsFromVersion1()
    {
        string[] baseArguments = [Path.Combine(Fixtures, "known-defects.csv"), "--timeframe", "H1", "--format", "json"];

        var v1 = await CoreValidationE2ETests.InvokeAsync(baseArguments);
        var v2 = await CoreValidationE2ETests.InvokeAsync([.. baseArguments, "--report-version", "2"]);

        // Version selection never changes exit behavior or category meaning.
        Assert.Equal(1, v1.ExitCode);
        Assert.Equal(1, v2.ExitCode);

        using var v1Document = JsonDocument.Parse(v1.StdOut);
        using var v2Document = JsonDocument.Parse(v2.StdOut);
        var v1Summary = v1Document.RootElement.GetProperty("summary");
        var v2Summary = v2Document.RootElement.GetProperty("summary");

        foreach (var category in new[]
                 {
                     "missingCandles",
                     "duplicateRecords",
                     "invalidOhlc",
                     "closedMarketRecords",
                     "timeGaps",
                     "malformedRows"
                 })
        {
            Assert.Equal(
                v1Summary.GetProperty(category).GetInt64(),
                v2Summary.GetProperty(category).GetInt64());
        }

        Assert.Equal("FindingsDetected", v2Document.RootElement.GetProperty("status").GetString());
        Assert.NotEmpty(v2Document.RootElement.GetProperty("findings").EnumerateArray());
    }

    [Fact]
    public async Task ReportVersion2_EveryFindingCarriesActionableDetail()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [Path.Combine(Fixtures, "known-defects.csv"), "--timeframe", "H1", "--format", "json", "--report-version", "2"]);

        using var document = JsonDocument.Parse(result.StdOut);
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();

        Assert.NotEmpty(findings);
        Assert.All(findings, finding =>
        {
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("reference").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("category").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("title").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("explanation").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("suggestedAction").GetString()));
            Assert.True(finding.GetProperty("countContribution").GetInt64() > 0);
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("evidence").GetProperty("kind").GetString()));
        });

        // Deterministic references are unique across the whole report.
        var references = findings.Select(finding => finding.GetProperty("reference").GetString()).ToArray();
        Assert.Equal(references.Length, references.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ReportVersion2_IsByteForByteDeterministicAcrossRuns()
    {
        string[] arguments =
            [Path.Combine(Fixtures, "known-defects.csv"), "--timeframe", "H1", "--format", "json", "--report-version", "2"];

        var first = await CoreValidationE2ETests.InvokeAsync(arguments);
        var second = await CoreValidationE2ETests.InvokeAsync(arguments);

        Assert.Equal(first.StdOut, second.StdOut);
    }

    [Fact]
    public async Task ReportVersion2_FatalRun_WritesExactlyOneFatalDocumentToStandardError()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [Path.Combine(Fixtures, "missing-close-column.csv"), "--header", "--format", "json", "--report-version", "2"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));

        using var document = JsonDocument.Parse(result.StdErr);
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("contractVersion").GetInt32());
        Assert.Equal("Fatal", root.GetProperty("status").GetString());

        // A fatal document can never be mistaken for a successful report.
        Assert.False(root.GetProperty("findingSetComplete").GetBoolean());
        Assert.False(root.TryGetProperty("summary", out _));
        Assert.False(root.TryGetProperty("reconciliation", out _));
        Assert.False(root.TryGetProperty("isClean", out _));

        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("code").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("failureClass").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("stage").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("reason").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("guidance").GetString()));
    }

    [Fact]
    public async Task ReportVersion2_MissingInputFile_ReportsSourceUnavailableOnStandardError()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [Path.Combine(_directory, "absent.csv"), "--format", "json", "--report-version", "2"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));

        using var document = JsonDocument.Parse(result.StdErr);
        Assert.Equal("SOURCE_UNAVAILABLE", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("Operational", document.RootElement.GetProperty("failureClass").GetString());
    }

    [Fact]
    public async Task ReportVersion2_WithOutput_CommitsReportAtomicallyAndLeavesSourceUnchanged()
    {
        var input = Path.Combine(_directory, "input.csv");
        var output = Path.Combine(_directory, "report-v2.json");
        File.Copy(Path.Combine(Fixtures, "clean-forex-h1.csv"), input);
        var before = await File.ReadAllBytesAsync(input);

        var result = await CoreValidationE2ETests.InvokeAsync(
            [input, "--format", "json", "--report-version", "2", "--output", output]);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(output));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Equal(2, document.RootElement.GetProperty("contractVersion").GetInt32());

        // The validated source is never modified and no staged artifact survives.
        Assert.Equal(before, await File.ReadAllBytesAsync(input));
        Assert.Equal(
            $"Validation complete: findings=0; clean=true; report={output}",
            CoreValidationE2ETests.Normalize(result.StdOut));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task ReportVersion2_OutputAliasingInput_IsRejectedBeforeAnyBytesAreWritten()
    {
        var input = Path.Combine(_directory, "alias.csv");
        File.Copy(Path.Combine(Fixtures, "clean-forex-h1.csv"), input);
        var before = await File.ReadAllBytesAsync(input);

        var result = await CoreValidationE2ETests.InvokeAsync(
            [input, "--format", "json", "--report-version", "2", "--output", input]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(before, await File.ReadAllBytesAsync(input));
    }

    [Fact]
    public async Task DefaultAndExplicitVersion1_RemainTheUnversionedContract()
    {
        string[] arguments = [Path.Combine(Fixtures, "known-defects.csv"), "--timeframe", "H1", "--format", "json"];

        var unversioned = await CoreValidationE2ETests.InvokeAsync(arguments);
        var explicitV1 = await CoreValidationE2ETests.InvokeAsync([.. arguments, "--report-version", "1"]);

        Assert.Equal(unversioned.ExitCode, explicitV1.ExitCode);
        Assert.Equal(unversioned.StdOut, explicitV1.StdOut);
        using var document = JsonDocument.Parse(explicitV1.StdOut);
        Assert.False(document.RootElement.TryGetProperty("contractVersion", out _));
    }

    [Theory]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("two")]
    public async Task ReportVersion_RejectsUnsupportedValuesWithTextDiagnostic(string value)
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [Path.Combine(Fixtures, "clean-forex-h1.csv"), "--format", "json", "--report-version", value]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));
        // Contradictory representation options cannot use structured v2 stderr.
        Assert.Contains("report-version", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportVersion2_RequiresJsonFormat()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [Path.Combine(Fixtures, "clean-forex-h1.csv"), "--report-version", "2"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));
        Assert.Contains("json", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }
}
