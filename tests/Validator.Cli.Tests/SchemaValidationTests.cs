using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Validator.Cli.Tests;

// The published contracts are the promise a consumer builds against, so every
// document the CLI emits is validated against them as-is: successful v2 reports
// against detailed-report-v2.schema.json and fatal documents against
// fatal-diagnostic-v2.schema.json.
public sealed class SchemaValidationTests : IDisposable
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly string Contracts = Path.Combine(AppContext.BaseDirectory, "Contracts");

    private static readonly EvaluationOptions Options = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true
    };

    private static readonly object RegistryGate = new();
    private static bool _scoringSchemaRegistered;

    private readonly string _directory;

    public SchemaValidationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"validator-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        RegisterScoringSchema();
    }

    // The amended v2 success schema references the scoring section by its
    // published id. Registering the section schema up front lets that $ref
    // resolve entirely offline, so schema tests never attempt a network fetch.
    private static void RegisterScoringSchema()
    {
        lock (RegistryGate)
        {
            if (_scoringSchemaRegistered)
            {
                return;
            }

            var scoring = JsonSchema.FromFile(Path.Combine(Contracts, "scoring-v2.schema.json"));
            SchemaRegistry.Global.Register(
                new Uri("https://financial-data-cleaner.local/contracts/scoring-v2.schema.json"),
                scoring);
            _scoringSchemaRegistered = true;
        }
    }


    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static JsonSchema Load(string fileName) =>
        JsonSchema.FromFile(Path.Combine(Contracts, fileName));

    // Validation failures are reported with their schema locations so a contract
    // break names the offending member rather than only failing.
    private static void AssertValid(JsonSchema schema, string json)
    {
        var instance = JsonNode.Parse(json);
        var result = schema.Evaluate(instance, Options);
        if (result.IsValid)
        {
            return;
        }

        var errors = (result.Details ?? [])
            .Where(detail => detail.HasErrors)
            .SelectMany(detail => detail.Errors!.Select(error =>
                $"{detail.InstanceLocation}: {error.Key} {error.Value}"))
            .Distinct()
            .ToArray();

        Assert.Fail($"The document does not satisfy its contract:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }

    private static async Task<string> RunForV2JsonAsync(params string[] arguments)
    {
        var result = await CoreValidationE2ETests.InvokeAsync(arguments);
        Assert.False(string.IsNullOrWhiteSpace(result.StdOut), "The run produced no report on standard output.");
        return result.StdOut;
    }

    [Fact]
    public async Task CleanV2Report_SatisfiesTheDetailedReportContract()
    {
        var json = await RunForV2JsonAsync(
            Path.Combine(Fixtures, "clean-forex-h1.csv"), "--format", "json", "--report-version", "2");

        AssertValid(Load("detailed-report-v2.schema.json"), json);
    }

    [Fact]
    public async Task CrossCategoryV2Report_SatisfiesTheDetailedReportContract()
    {
        var json = await RunForV2JsonAsync(
            Path.Combine(Fixtures, "known-defects.csv"),
            "--timeframe",
            "H1",
            "--format",
            "json",
            "--report-version",
            "2");

        AssertValid(Load("detailed-report-v2.schema.json"), json);
    }

    // Every category must serialize into a contract-valid document, so a source
    // exercising duplicates, invalid OHLC, malformed rows, gaps, and a
    // closed-market record is validated as one report.
    [Fact]
    public async Task EveryCategoryTogether_SatisfiesTheDetailedReportContract()
    {
        var input = Path.Combine(_directory, "all-categories.csv");
        await File.WriteAllLinesAsync(input,
        [
            "2026.01.05,00:00,1.10,1.20,1.05,1.15,10",
            "2026.01.05,01:00,1.15,1.25,1.10,1.20,11",
            "2026.01.05,01:00,1.15,1.25,1.10,1.99,12",
            "2026.01.05,02:00,1.20,1.10,1.30,1.25,13",
            "2026.01.05,03:00,1.25,abc,1.20,1.30,14",
            "2026.01.05,06:00,1.30,1.40,1.25,1.35,15",
            "2026.01.10,12:00,1.35,1.45,1.30,1.40,16"
        ]);

        var json = await RunForV2JsonAsync(
            input, "--timeframe", "H1", "--tz-offset", "+00:00", "--format", "json", "--report-version", "2");

        AssertValid(Load("detailed-report-v2.schema.json"), json);
    }

    [Fact]
    public async Task FatalV2Diagnostic_SatisfiesTheFatalDiagnosticContract()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [Path.Combine(Fixtures, "missing-close-column.csv"), "--format", "json", "--report-version", "2"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, CoreValidationE2ETests.Normalize(result.StdOut));
        AssertValid(Load("fatal-diagnostic-v2.schema.json"), result.StdErr);
    }

    [Theory]
    [InlineData("missing.csv", new string[0])]
    [InlineData("invalid-encoding.bin", new string[0])]
    public async Task EveryFatalRoute_SatisfiesTheFatalDiagnosticContract(string fixture, string[] extra)
    {
        var path = fixture == "missing.csv"
            ? Path.Combine(_directory, fixture)
            : Path.Combine(Fixtures, fixture);
        if (fixture == "invalid-encoding.bin" && !File.Exists(path))
        {
            path = Path.Combine(_directory, fixture);
            await File.WriteAllBytesAsync(path, [0x41, 0x2c, 0xff, 0xfe, 0x0a]);
        }

        var result = await CoreValidationE2ETests.InvokeAsync(
            [path, "--format", "json", "--report-version", "2", .. extra]);

        Assert.Equal(2, result.ExitCode);
        AssertValid(Load("fatal-diagnostic-v2.schema.json"), result.StdErr);
    }

    // A rejected option is still answered in the contract shape, so a consumer
    // parses one document type regardless of why the run failed.
    [Fact]
    public async Task InvalidOptionUnderV2_SatisfiesTheFatalDiagnosticContract()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
        [
            Path.Combine(Fixtures, "clean-forex-h1.csv"),
            "--format",
            "json",
            "--report-version",
            "2",
            "--timeframe",
            "Q7"
        ]);

        Assert.Equal(2, result.ExitCode);
        AssertValid(Load("fatal-diagnostic-v2.schema.json"), result.StdErr);
    }

    // A v1 document must not accidentally satisfy the v2 contract; the two
    // contracts stay distinguishable.
    [Fact]
    public async Task VersionOneJson_DoesNotSatisfyTheDetailedReportContract()
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [Path.Combine(Fixtures, "known-defects.csv"), "--timeframe", "H1", "--format", "json"]);

        var evaluation = Load("detailed-report-v2.schema.json").Evaluate(JsonNode.Parse(result.StdOut), Options);
        Assert.False(evaluation.IsValid);
    }

    // The contracts themselves must remain parseable, self-describing schema
    // documents rather than arbitrary JSON.
    // A scored v2 document validates against the amended success schema, whose
    // scoring member validates against the separately published section schema,
    // and its contractVersion is still 2.
    [Fact]
    public async Task ScoredV2Report_SatisfiesTheAmendedDetailedReportContract()
    {
        var json = await RunForV2JsonAsync(
            Path.Combine(Fixtures, "known-defects.csv"),
            "--timeframe", "H1", "--score", "--format", "json", "--report-version", "2");

        AssertValid(Load("detailed-report-v2.schema.json"), json);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetProperty("contractVersion").GetInt32());
        Assert.True(document.RootElement.TryGetProperty("scoring", out var scoring));
        AssertValid(Load("scoring-v2.schema.json"), scoring.GetRawText());
    }

    [Theory]
    [InlineData("detailed-report-v2.schema.json")]
    [InlineData("fatal-diagnostic-v2.schema.json")]
    [InlineData("scoring-v2.schema.json")]
    public void PublishedContract_IsAParseableSchemaDocument(string fileName)

    {
        var path = Path.Combine(Contracts, fileName);
        Assert.True(File.Exists(path), $"The published contract '{fileName}' was not found next to the tests.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            document.RootElement.GetProperty("$schema").GetString());
        Assert.NotNull(JsonSchema.FromFile(path));
    }
}
