using System.Text.Json;

namespace Validator.Cli.Tests;

public sealed class OutputFormatE2ETests
{
    [Fact]
    public async Task JsonStdout_IsExactlyOneSchemaShapedDocument()
    {
        var input = Path.Combine(AppContext.BaseDirectory, "Fixtures", "known-defects.csv");

        var result = await CoreValidationE2ETests.InvokeAsync(
            [input, "--timeframe", "H1", "--format", "json"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdErr));
        using var document = JsonDocument.Parse(result.StdOut);
        Assert.Equal(2, document.RootElement.GetProperty("summary").GetProperty("missingCandles").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("summary").GetProperty("duplicateRecords").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("summary").GetProperty("invalidOhlc").GetInt32());
    }

    [Fact]
    public async Task OutputFile_WritesReportAtomicallyAndPrintsOneLineSummaryWithoutMutatingSource()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"validator-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourceFixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "clean-forex-h1.csv");
        var input = Path.Combine(directory, "input.csv");
        var output = Path.Combine(directory, "report.json");
        File.Copy(sourceFixture, input);
        var before = File.ReadAllBytes(input);

        var result = await CoreValidationE2ETests.InvokeAsync(
            [input, "--format", "json", "--output", output]);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(output));
        JsonDocument.Parse(File.ReadAllText(output)).Dispose();
        Assert.Equal(before, File.ReadAllBytes(input));
        Assert.Equal(
            $"Validation complete: findings=0; clean=true; report={output}",
            CoreValidationE2ETests.Normalize(result.StdOut));
    }
}
