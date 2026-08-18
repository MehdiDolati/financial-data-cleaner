using System.Text;
using System.Text.Json;

namespace Validator.Cli.Tests;

// A fatal v2 run publishes exactly one fatal document on standard error, never
// writes a report payload to standard output, exits with the reserved fatal
// code, and leaves the report destination byte-for-byte unchanged.
public sealed class FatalV2RoutingTests : IDisposable
{
    private readonly string _directory;

    public FatalV2RoutingTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"validator-fatal-{Guid.NewGuid():N}");
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
    public async Task UnreadableSource_EmitsExactlyOneFatalDocumentOnStandardError()
    {
        var missing = Path.Combine(_directory, "absent.csv");

        var result = await CoreValidationE2ETests.InvokeAsync(
            [missing, "--format", "json", "--report-version", "2"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));

        var fatal = AssertSingleFatalDocument(result.StdErr);
        Assert.Equal("SOURCE_UNAVAILABLE", fatal.GetProperty("code").GetString());
        Assert.Equal("Operational", fatal.GetProperty("failureClass").GetString());
        Assert.Equal("SourceIdentity", fatal.GetProperty("stage").GetString());
        Assert.Equal("absent.csv", fatal.GetProperty("source").GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task FatalDocument_NeverClaimsACompleteOrSuccessfulReport()
    {
        var input = await WriteAsync("bad-encoding.csv", raw: [0x32, 0x30, 0x32, 0x36, 0xFF, 0xFE, 0x0A]);

        var result = await CoreValidationE2ETests.InvokeAsync(
            [input, "--format", "json", "--report-version", "2"]);

        Assert.Equal(2, result.ExitCode);
        var fatal = AssertSingleFatalDocument(result.StdErr);
        Assert.Equal(2, fatal.GetProperty("contractVersion").GetInt32());
        Assert.Equal("Fatal", fatal.GetProperty("status").GetString());
        Assert.False(fatal.GetProperty("findingSetComplete").GetBoolean());
        Assert.False(fatal.TryGetProperty("summary", out _));
        Assert.False(fatal.TryGetProperty("reconciliation", out _));
        Assert.False(fatal.TryGetProperty("isClean", out _));
        Assert.False(fatal.TryGetProperty("findings", out _));
    }

    [Fact]
    public async Task FatalDocument_MarksEveryCheckThatDidNotRun()
    {
        var input = await WriteAsync("single-row.csv", "2026.01.01,00:00,1,2,0.5,1.5,10");

        var result = await CoreValidationE2ETests.InvokeAsync(
            [input, "--format", "json", "--report-version", "2"]);

        Assert.Equal(2, result.ExitCode);
        var checks = AssertSingleFatalDocument(result.StdErr).GetProperty("checks").EnumerateArray().ToArray();

        Assert.Equal(6, checks.Length);
        Assert.Equal(
            ["MissingCandles", "DuplicateRecords", "InvalidOhlc", "ClosedMarketRecords", "TimeGaps", "MalformedRows"],
            checks.Select(check => check.GetProperty("check").GetString()));
        Assert.All(checks, check =>
        {
            Assert.Equal("NotCompleted", check.GetProperty("status").GetString());
            Assert.False(string.IsNullOrWhiteSpace(check.GetProperty("reason").GetString()));
        });
    }

    [Fact]
    public async Task FatalRun_LeavesAnExistingDestinationByteForByteUnchanged()
    {
        var missing = Path.Combine(_directory, "absent.csv");
        var destination = Path.Combine(_directory, "report.json");
        await File.WriteAllTextAsync(destination, "previous-report");

        var result = await CoreValidationE2ETests.InvokeAsync(
            [missing, "--format", "json", "--report-version", "2", "--output", destination]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("previous-report", await File.ReadAllTextAsync(destination));
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));
        Assert.Empty(Directory.GetFiles(_directory, ".validator-report-*.staged"));
    }

    [Fact]
    public async Task FatalRun_NeverCreatesAnAbsentDestination()
    {
        var missing = Path.Combine(_directory, "absent.csv");
        var destination = Path.Combine(_directory, "never-created.json");

        var result = await CoreValidationE2ETests.InvokeAsync(
            [missing, "--format", "json", "--report-version", "2", "--output", destination]);

        Assert.Equal(2, result.ExitCode);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task SuccessfulRun_WithDestination_KeepsTheReportPayloadOffStandardOutput()
    {
        var input = await WriteAsync(
            "clean.csv",
            "2026.01.01,00:00,1.1,1.2,1.0,1.15,10",
            "2026.01.01,01:00,1.15,1.25,1.05,1.2,10",
            "2026.01.01,02:00,1.2,1.3,1.1,1.25,10");
        var destination = Path.Combine(_directory, "report.json");

        var result = await CoreValidationE2ETests.InvokeAsync(
            [input, "--timeframe", "H1", "--format", "json", "--report-version", "2", "--output", destination]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdErr));
        Assert.DoesNotContain("contractVersion", result.StdOut, StringComparison.Ordinal);
        Assert.Equal(
            2,
            JsonDocument.Parse(await File.ReadAllTextAsync(destination))
                .RootElement.GetProperty("contractVersion").GetInt32());
    }

    // Exactly one JSON value, with nothing before or after it, so a consumer can
    // parse standard error without scanning for delimiters.
    private static JsonElement AssertSingleFatalDocument(string standardError)
    {
        var text = standardError.Trim();
        Assert.False(string.IsNullOrEmpty(text));

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text));
        Assert.True(reader.Read());
        Assert.Equal(JsonTokenType.StartObject, reader.TokenType);
        reader.Skip();
        Assert.False(reader.Read());

        return JsonDocument.Parse(text).RootElement;
    }

    private async Task<string> WriteAsync(string name, params string[] lines)
    {
        var path = Path.Combine(_directory, name);
        await File.WriteAllLinesAsync(path, lines);
        return path;
    }

    private async Task<string> WriteAsync(string name, byte[] raw)
    {
        var path = Path.Combine(_directory, name);
        await File.WriteAllBytesAsync(path, raw);
        return path;
    }
}
