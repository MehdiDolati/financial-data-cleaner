using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Validator.Cli.Tests;

// Identical source bytes and identical resolved options must produce identical
// v2 documents. The comparison is a hash of the whole published document, so a
// wall-clock field, a random identifier, or an unstable finding order would all
// break these tests rather than hide inside an ignored field.
public sealed class RepeatabilityTests : IDisposable
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly string _directory;

    public RepeatabilityTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"validator-repeat-{Guid.NewGuid():N}");
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
    public async Task TwoRunsOverTheSameSource_ProduceIdenticalDocumentHashes()
    {
        string[] arguments =
        [
            Path.Combine(Fixtures, "known-defects.csv"),
            "--timeframe", "H1",
            "--format", "json",
            "--report-version", "2"
        ];

        var first = await CoreValidationE2ETests.InvokeAsync(arguments);
        var second = await CoreValidationE2ETests.InvokeAsync(arguments);

        Assert.Equal(first.ExitCode, second.ExitCode);
        Assert.Equal(Hash(first.StdOut), Hash(second.StdOut));
    }

    [Fact]
    public async Task TwoRunsOverACleanSource_ProduceIdenticalDocumentHashes()
    {
        string[] arguments =
        [
            Path.Combine(Fixtures, "clean-forex-h1.csv"),
            "--format", "json",
            "--report-version", "2"
        ];

        var first = await CoreValidationE2ETests.InvokeAsync(arguments);
        var second = await CoreValidationE2ETests.InvokeAsync(arguments);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(Hash(first.StdOut), Hash(second.StdOut));
    }

    // Identical bytes at a different path and file name are the same dataset,
    // so everything except the reported file name must hash identically.
    [Fact]
    public async Task IdenticalBytesUnderADifferentName_ProduceIdenticalFindingsAndFingerprint()
    {
        var original = Path.Combine(Fixtures, "known-defects.csv");
        var copy = Path.Combine(_directory, "renamed-defects.csv");
        File.Copy(original, copy);

        var first = await RunAsync(original);
        var second = await RunAsync(copy);

        Assert.Equal(
            first.RootElement.GetProperty("source").GetProperty("sha256").GetString(),
            second.RootElement.GetProperty("source").GetProperty("sha256").GetString());
        Assert.Equal(Hash(Findings(first)), Hash(Findings(second)));
        Assert.Equal(Hash(Summary(first)), Hash(Summary(second)));
    }

    // A report published to a file and a report written to standard output are
    // the same document, so staging must not alter a single byte. With
    // --output, stdout carries only the documented one-line confirmation.
    [Fact]
    public async Task ReportWrittenToAFile_MatchesTheReportWrittenToStandardOutput()
    {
        var destination = Path.Combine(_directory, "report.json");
        string[] arguments =
        [
            Path.Combine(Fixtures, "known-defects.csv"),
            "--timeframe", "H1",
            "--format", "json",
            "--report-version", "2"
        ];

        var standardOutputRun = await CoreValidationE2ETests.InvokeAsync(arguments);
        var fileRun = await CoreValidationE2ETests.InvokeAsync([.. arguments, "--output", destination]);

        Assert.Equal(standardOutputRun.ExitCode, fileRun.ExitCode);
        Assert.StartsWith("Validation complete:", fileRun.StdOut, StringComparison.Ordinal);
        Assert.Equal(Hash(standardOutputRun.StdOut), Hash(await File.ReadAllTextAsync(destination)));
    }

    // A single changed byte must change the fingerprint and the findings; the
    // report describes the bytes it was given, not a cached earlier run.
    [Fact]
    public async Task ChangingOneSourceByte_ChangesTheFingerprintAndTheDocument()
    {
        var first = Path.Combine(_directory, "first.csv");
        var second = Path.Combine(_directory, "second.csv");
        await File.WriteAllLinesAsync(first, ["2026.01.01,00:00,1,2,0.5,1.5,10", "2026.01.01,01:00,1,2,0.5,1.5,10"]);
        await File.WriteAllLinesAsync(second, ["2026.01.01,00:00,1,2,0.5,1.5,10", "2026.01.01,01:00,1,2,0.5,1.6,10"]);

        var firstDocument = await RunAsync(first);
        var secondDocument = await RunAsync(second);

        Assert.NotEqual(
            firstDocument.RootElement.GetProperty("source").GetProperty("sha256").GetString(),
            secondDocument.RootElement.GetProperty("source").GetProperty("sha256").GetString());
    }

    // Reordered equivalent rows describe the same facts, so the canonical order
    // has to erase the order the rows arrived in.
    [Fact]
    public async Task ReorderedSourceRows_ProduceIdenticalFindingOrder()
    {
        var rows = new[]
        {
            "2026.01.01,00:00,1,2,0.5,1.5,10",
            "2026.01.01,00:00,1,2,0.5,1.4,10",
            "2026.01.01,02:00,1,2,0.5,1.5,10",
            "2026.01.01,04:00,1,0.4,0.5,1.5,10"
        };
        var ascending = Path.Combine(_directory, "ascending.csv");
        var descending = Path.Combine(_directory, "descending.csv");
        await File.WriteAllLinesAsync(ascending, rows);
        await File.WriteAllLinesAsync(descending, rows.Reverse());

        var first = await RunAsync(ascending);
        var second = await RunAsync(descending);

        Assert.Equal(Categories(first), Categories(second));
        Assert.Equal(Hash(Summary(first)), Hash(Summary(second)));
    }

    // Nothing in a published document may vary between runs, so no field may
    // carry a generated time, a random value, or an absolute source path.
    [Fact]
    public async Task PublishedDocument_ContainsNoWallClockOrPathDependentField()
    {
        var input = Path.Combine(_directory, "known-defects.csv");
        File.Copy(Path.Combine(Fixtures, "known-defects.csv"), input);

        var document = await RunAsync(input);
        var text = document.RootElement.GetRawText();

        Assert.DoesNotContain("generatedAt", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("timestampUtcNow", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_directory, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(input, text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("known-defects.csv", document.RootElement.GetProperty("source").GetProperty("fileName").GetString());
    }

    private static async Task<JsonDocument> RunAsync(string path)
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [path, "--timeframe", "H1", "--format", "json", "--report-version", "2", "--tz-offset", "+00:00"]);
        return JsonDocument.Parse(result.StdOut);
    }

    private static string Findings(JsonDocument document) =>
        document.RootElement.GetProperty("findings").GetRawText();

    private static string Summary(JsonDocument document) =>
        document.RootElement.GetProperty("summary").GetRawText();

    private static string[] Categories(JsonDocument document) =>
    [
        .. document.RootElement.GetProperty("findings")
            .EnumerateArray()
            .Select(finding => finding.GetProperty("category").GetString()!)
    ];

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
