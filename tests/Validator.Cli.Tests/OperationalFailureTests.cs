using System.Text.Json;
using Validator.Application.Reporting;
using Validator.Infrastructure.Reporting;

namespace Validator.Cli.Tests;

// Every way a v2 run can fail before a complete report exists is reported as one
// structured fatal document whose code, failure class, and stage identify what
// went wrong and where, with guidance a caller can act on. Configuration
// mistakes, dataset defects, and operational faults are never conflated.
public sealed class OperationalFailureTests : IDisposable
{
    private readonly string _directory;

    public OperationalFailureTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"validator-failures-{Guid.NewGuid():N}");
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
    public async Task UnknownOption_IsAConfigurationFailureAtArgumentValidation()
    {
        var input = await CleanSourceAsync("clean.csv");

        var fatal = await FatalAsync([input, "--format", "json", "--report-version", "2", "--unknown"]);

        AssertFailure(fatal, "INVALID_ARGUMENT", "Configuration", "ArgumentValidation");
        Assert.Contains("--unknown", fatal.GetProperty("guidance").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OptionWithoutItsValue_IsAConfigurationFailureAtArgumentValidation()
    {
        var input = await CleanSourceAsync("clean.csv");

        var fatal = await FatalAsync([input, "--format", "json", "--report-version", "2", "--timeframe"]);

        AssertFailure(fatal, "INVALID_ARGUMENT", "Configuration", "ArgumentValidation");
    }

    [Fact]
    public async Task ContradictoryVersionAndFormat_IsAConfigurationFailureAtArgumentValidation()
    {
        var input = await CleanSourceAsync("clean.csv");

        var fatal = await FatalAsync([input, "--format", "text", "--report-version", "2"]);

        AssertFailure(fatal, "INVALID_ARGUMENT", "Configuration", "ArgumentValidation");
    }

    [Fact]
    public async Task UnparsableTimeframeOption_IsAConfigurationFailureAtArgumentValidation()
    {
        var input = await CleanSourceAsync("clean.csv");

        var fatal = await FatalAsync(
            [input, "--format", "json", "--report-version", "2", "--timeframe", "X9"]);

        AssertFailure(fatal, "INVALID_ARGUMENT", "Configuration", "ArgumentValidation");
    }

    [Fact]
    public async Task UnresolvableCalendar_IsAConfigurationFailureAtArgumentValidation()
    {
        var input = await CleanSourceAsync("clean.csv");

        var fatal = await FatalAsync(
        [
            input,
            "--market", "custom",
            "--calendar", Path.Combine(_directory, "absent-calendar.json"),
            "--format", "json",
            "--report-version", "2"
        ]);

        AssertFailure(fatal, "INVALID_CALENDAR", "Configuration", "ArgumentValidation");
    }

    [Fact]
    public async Task UnresolvableTimeframe_IsAConfigurationFailureAtTimeframeResolution()
    {
        var input = await WriteLinesAsync("single-row.csv", "2026.01.01,00:00,1,2,0.5,1.5,10");

        var fatal = await FatalAsync([input, "--format", "json", "--report-version", "2"]);

        AssertFailure(fatal, "AMBIGUOUS_TIMEFRAME", "Configuration", "TimeframeResolution");
    }

    [Fact]
    public async Task MissingInput_IsAnOperationalFailureAtSourceIdentity()
    {
        var fatal = await FatalAsync(
            [Path.Combine(_directory, "absent.csv"), "--format", "json", "--report-version", "2"]);

        AssertFailure(fatal, "SOURCE_UNAVAILABLE", "Operational", "SourceIdentity");
    }

    [Fact]
    public async Task UnreadableTextInSource_IsADatasetFailureAtIngestion()
    {
        var path = Path.Combine(_directory, "invalid-encoding.csv");
        await File.WriteAllBytesAsync(path, [0x32, 0x30, 0x32, 0x36, 0xFF, 0xFE, 0x0A]);

        var fatal = await FatalAsync([path, "--format", "json", "--report-version", "2"]);

        AssertFailure(fatal, "INVALID_ENCODING", "Dataset", "Ingestion");
    }

    [Fact]
    public async Task DestinationThatCannotBePrepared_IsAnOperationalFailureAtReportCommit()
    {
        var input = await CleanSourceAsync("clean.csv");
        var blocking = Path.Combine(_directory, "not-a-directory");
        await File.WriteAllTextAsync(blocking, "blocking-file");

        var fatal = await FatalAsync(
        [
            input,
            "--timeframe", "H1",
            "--format", "json",
            "--report-version", "2",
            "--output", Path.Combine(blocking, "report.json")
        ]);

        AssertFailure(fatal, "REPORT_COMMIT_FAILED", "Operational", "ReportCommit");
        Assert.Equal("blocking-file", await File.ReadAllTextAsync(blocking));
    }

    [Fact]
    public async Task DestinationOccupiedByADirectory_IsAnOperationalFailureAtReportCommit()
    {
        var input = await CleanSourceAsync("clean.csv");
        var destination = Path.Combine(_directory, "occupied.json");
        Directory.CreateDirectory(destination);

        var fatal = await FatalAsync(
        [
            input,
            "--timeframe", "H1",
            "--format", "json",
            "--report-version", "2",
            "--output", destination
        ]);

        AssertFailure(fatal, "REPORT_COMMIT_FAILED", "Operational", "ReportCommit");
        Assert.True(Directory.Exists(destination));
        Assert.Empty(Directory.GetFiles(_directory, ".validator-report-*.staged"));
    }

    [Fact]
    public async Task DestinationThatAliasesTheInput_IsAConfigurationFailureAndLeavesTheInputIntact()
    {
        var input = await CleanSourceAsync("clean.csv");
        var before = await File.ReadAllTextAsync(input);

        var fatal = await FatalAsync(
        [
            input,
            "--timeframe", "H1",
            "--format", "json",
            "--report-version", "2",
            "--output", input
        ]);

        AssertFailure(fatal, "INVALID_ARGUMENT", "Configuration", "ArgumentValidation");
        Assert.Equal(before, await File.ReadAllTextAsync(input));
    }

    // A render that stops part-way is reported at the rendering stage and never
    // publishes anything, so no consumer can read a truncated report.
    [Fact]
    public async Task InterruptedRender_IsAnOperationalFailureAtReportRendering()
    {
        var destination = Path.Combine(_directory, "render.json");
        await File.WriteAllTextAsync(destination, "previous-report");
        using var standardOutput = new StringWriter();

        var result = await new StageAndCommitWriter(destination).PublishAsync(
            async (staged, token) =>
            {
                await staged.WriteAsync("{\"contractVersion\":2,\"findings\":[");
                throw new InvalidOperationException("The finding catalog could not be replayed.");
            },
            standardOutput);

        var failed = Assert.IsType<ReportCommitResult.Failed>(result);
        var fatal = JsonDocument.Parse(new FatalDiagnosticV2Writer().Render(failed.Diagnostic)).RootElement;

        AssertFailure(fatal, "REPORT_RENDER_FAILED", "Operational", "ReportRendering");
        Assert.Equal("previous-report", await File.ReadAllTextAsync(destination));
        Assert.Empty(standardOutput.ToString());
    }

    [Fact]
    public async Task EveryFailureCarriesDistinctReasonAndActionableGuidance()
    {
        var input = await CleanSourceAsync("clean.csv");
        var codes = new List<string>();

        foreach (var args in new[]
        {
            new[] { input, "--format", "json", "--report-version", "2", "--unknown" },
            [Path.Combine(_directory, "absent.csv"), "--format", "json", "--report-version", "2"],
            [await WriteLinesAsync("one.csv", "2026.01.01,00:00,1,2,0.5,1.5,10"), "--format", "json", "--report-version", "2"]
        })
        {
            var fatal = await FatalAsync(args);
            codes.Add(fatal.GetProperty("code").GetString()!);
            Assert.False(string.IsNullOrWhiteSpace(fatal.GetProperty("reason").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(fatal.GetProperty("guidance").GetString()));
            Assert.NotEqual(
                fatal.GetProperty("reason").GetString(),
                fatal.GetProperty("guidance").GetString());
        }

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    private static void AssertFailure(
        JsonElement fatal,
        string code,
        string failureClass,
        string stage)
    {
        Assert.Equal(2, fatal.GetProperty("contractVersion").GetInt32());
        Assert.Equal("Fatal", fatal.GetProperty("status").GetString());
        Assert.False(fatal.GetProperty("findingSetComplete").GetBoolean());
        Assert.Equal(code, fatal.GetProperty("code").GetString());
        Assert.Equal(failureClass, fatal.GetProperty("failureClass").GetString());
        Assert.Equal(stage, fatal.GetProperty("stage").GetString());
    }

    private static async Task<JsonElement> FatalAsync(string[] args)
    {
        var result = await CoreValidationE2ETests.InvokeAsync(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));
        return JsonDocument.Parse(result.StdErr.Trim()).RootElement;
    }

    private Task<string> CleanSourceAsync(string name) => WriteLinesAsync(
        name,
        "2026.01.01,00:00,1.1,1.2,1.0,1.15,10",
        "2026.01.01,01:00,1.15,1.25,1.05,1.2,10",
        "2026.01.01,02:00,1.2,1.3,1.1,1.25,10");

    private async Task<string> WriteLinesAsync(string name, params string[] lines)
    {
        var path = Path.Combine(_directory, name);
        await File.WriteAllLinesAsync(path, lines);
        return path;
    }
}
