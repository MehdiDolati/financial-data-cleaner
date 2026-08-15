namespace Validator.Cli.Tests;

public sealed class HelpE2ETests
{
    private static readonly string[] RequiredOptions =
    [
        "--timeframe",
        "--market",
        "--calendar",
        "--date-format",
        "--time-format",
        "--timestamp-format",
        "--timestamp-column",
        "--tz-offset",
        "--delimiter",
        "--header",
        "--format",
        "--output",
        "--verbose",
        "--help"
    ];

    private static readonly string[] RequiredExamples =
    [
        "validator EURUSD_H1.csv",
        "validator EURUSD_M15.csv --header --format json",
        "validator prices.csv --timestamp-format \"yyyy-MM-dd HH:mm:ss\" --timestamp-column 1 --tz-offset +00:00",
        "validator equities.csv --market equities --timeframe M30 --verbose",
        "validator custom.csv --market custom --calendar market-hours.json --output report.json --format json"
    ];

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task Help_ListsEveryOptionAndRequiredExample(string helpOption)
    {
        var result = await CoreValidationE2ETests.InvokeAsync([helpOption]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdErr));
        Assert.All(RequiredOptions, option => Assert.Contains(option, result.StdOut, StringComparison.Ordinal));
        Assert.All(RequiredExamples, example => Assert.Contains(example, result.StdOut, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingInput_IsAUsageFailureRatherThanAHelpRequest()
    {
        var result = await CoreValidationE2ETests.InvokeAsync([]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));
        Assert.Contains("input", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--help", result.StdErr, StringComparison.Ordinal);
    }
}