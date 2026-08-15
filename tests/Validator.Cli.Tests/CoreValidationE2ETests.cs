using Validator.Cli.Commands;

namespace Validator.Cli.Tests;

public sealed class CoreValidationE2ETests
{
    private static readonly string Fixtures = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures");

    [Fact]
    public async Task CleanDefaultMt4File_WritesExactSixLineReportAndReturnsZero()
    {
        var result = await InvokeAsync([Path.Combine(Fixtures, "clean-forex-h1.csv")]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "Missing candles: 0\n" +
            "Duplicate records: 0\n" +
            "Invalid OHLC: 0\n" +
            "Closed-market records: 0\n" +
            "Time gaps: 0\n" +
            "Malformed rows: 0",
            Normalize(result.StdOut));
        Assert.Equal(string.Empty, Normalize(result.StdErr));
    }

    [Fact]
    public async Task KnownDefects_WritesManifestCountsAndReturnsOne()
    {
        var result = await InvokeAsync(
            [Path.Combine(Fixtures, "known-defects.csv"), "--timeframe", "H1", "--verbose"]);

        Assert.Equal(1, result.ExitCode);
        var lines = Normalize(result.StdOut).Split('\n');
        Assert.Equal("Missing candles: 2", lines[0]);
        Assert.Equal("Duplicate records: 1", lines[1]);
        Assert.Equal("Invalid OHLC: 1", lines[2]);
        Assert.Equal("Closed-market records: 0", lines[3]);
        Assert.Equal("Time gaps: 2", lines[4]);
        Assert.Equal("Malformed rows: 0", lines[5]);
        Assert.Contains("Findings:", lines);
    }

    [Fact]
    public async Task MissingRequiredColumn_WritesFatalDiagnosticOnlyAndReturnsTwo()
    {
        var result = await InvokeAsync(
            [Path.Combine(Fixtures, "missing-close-column.csv"), "--header"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, Normalize(result.StdOut));
        Assert.Contains("close", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<CommandResult> InvokeAsync(string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var exitCode = await ValidateCommand.RunAsync(args);
            return new CommandResult(exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    internal static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    internal sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}