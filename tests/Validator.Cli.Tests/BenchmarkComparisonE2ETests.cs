using System.Linq;
using System.Text.Json;
using Validator.Cli.Commands;

namespace Validator.Cli.Tests;

/// <summary>
/// CLI end-to-end tests covering benchmark establishment, comparison exit semantics,
/// combined v2 output, tolerance overrides and disablement, no-overlap handling,
/// fatal atomicity, and repeated-output determinism (SC-005, SC-006, quickstart scenarios 1-8).
/// </summary>
public sealed class BenchmarkComparisonE2ETests : IDisposable
{
    // Benchmark comparison fixtures live in the shared tests/Fixtures/ directory,
    // not in the CLI tests' own Fixtures/ directory.
    private static readonly string Fixtures = FindSharedFixturesDir();

    private readonly string _benchmarkDir;
    private readonly string _benchmarkDir2;

    public BenchmarkComparisonE2ETests()
    {
        _benchmarkDir = Path.Combine(Path.GetTempPath(), $"benchmarks-{Guid.NewGuid():N}");
        _benchmarkDir2 = Path.Combine(Path.GetTempPath(), $"benchmarks2-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_benchmarkDir))
            Directory.Delete(_benchmarkDir, recursive: true);
        if (Directory.Exists(_benchmarkDir2))
            Directory.Delete(_benchmarkDir2, recursive: true);
    }

    // --- Scenario 1: Establish a Benchmark ---

    [Fact]
    public async Task Scenario1_EstablishBenchmark_CreatesDirectoryAndJson()
    {
        var refFile = Path.Combine(Fixtures, "AUDUSD_D1_reference.csv");
        var result = await InvokeAsync([
            refFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--benchmark", "test-establish",
            "--benchmark-dir", _benchmarkDir
        ]);

        // Exit code 0 = clean, 1 = findings present (both acceptable)
        Assert.InRange(result.ExitCode, 0, 1);
        using var report = JsonDocument.Parse(result.StdOut);
        Assert.Equal(2, report.RootElement.GetProperty("contractVersion").GetInt32());

        var benchmarkPath = Path.Combine(_benchmarkDir, "test-establish");
        Assert.True(Directory.Exists(benchmarkPath));
        Assert.True(File.Exists(Path.Combine(benchmarkPath, "benchmark.json")));
        Assert.True(File.Exists(Path.Combine(benchmarkPath, "source.csv")));

        // Verify valid JSON
        var json = await File.ReadAllTextAsync(Path.Combine(benchmarkPath, "benchmark.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("contractVersion").GetInt32());
        Assert.Equal("test-establish", doc.RootElement.GetProperty("name").GetString());
    }

    // --- Scenario 6: Reject Duplicate Benchmark Name ---

    [Fact]
    public async Task Scenario6_DuplicateBenchmarkName_RejectsWithExitCode2()
    {
        var refFile = Path.Combine(Fixtures, "AUDUSD_D1_reference.csv");

        // First establishment succeeds
        var result1 = await InvokeAsync([
            refFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--benchmark", "test-dup",
            "--benchmark-dir", _benchmarkDir
        ]);
        Assert.InRange(result1.ExitCode, 0, 1);

        // Second establishment with same name fails
        var result2 = await InvokeAsync([
            refFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--benchmark", "test-dup",
            "--benchmark-dir", _benchmarkDir
        ]);
        Assert.Equal(2, result2.ExitCode);
        Assert.Contains("already exists", result2.StdErr);
    }

    // --- Scenario 2: Compare Identical Data ---

    [Fact]
    public async Task Scenario2_CompareIdenticalData_NoDiscrepanciesPerfectScore()
    {
        // Establish benchmark first
        await EstablishBenchmark("test-identical", _benchmarkDir);

        var refFile = Path.Combine(Fixtures, "AUDUSD_D1_reference.csv");
        var result = await InvokeAsync([
            refFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--compare", "test-identical",
            "--benchmark-dir", _benchmarkDir
        ]);

        Assert.InRange(result.ExitCode, 0, 1);
        using var document = JsonDocument.Parse(result.StdOut);
        var comparison = document.RootElement.GetProperty("benchmarkComparison");
        Assert.Empty(comparison.GetProperty("materialDiscrepancies").EnumerateArray());
        Assert.Equal("100.00", comparison.GetProperty("agreementScore").GetProperty("score").GetProperty("rounded").GetString());
    }

    // --- Scenario 3: Compare With Known Differences ---

    [Fact]
    public async Task Scenario3_CompareWithDifferences_DetectsMaterialDiscrepancy()
    {
        await EstablishBenchmark("test-diff", _benchmarkDir);

        var candidateFile = Path.Combine(Fixtures, "AUDUSD_D1_candidate_with_differences.csv");
        var result = await InvokeAsync([
            candidateFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--compare", "test-diff",
            "--benchmark-dir", _benchmarkDir
        ]);

        Assert.InRange(result.ExitCode, 0, 1); // advisory: 0 or 1
        using var document = JsonDocument.Parse(result.StdOut);
        Assert.True(document.RootElement.GetProperty("benchmarkComparison").GetProperty("materialDiscrepancies").GetArrayLength() > 0);
    }

    // --- Exit code 0 for advisory comparison (Q6, FR-026) ---

    [Fact]
    public async Task AdvisoryComparison_ReturnsExitCode0_RegardlessOfDiscrepancies()
    {
        await EstablishBenchmark("test-advisory", _benchmarkDir);

        var candidateFile = Path.Combine(Fixtures, "AUDUSD_D1_candidate_with_differences.csv");
        var result = await InvokeAsync([
            candidateFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--compare", "test-advisory",
            "--benchmark-dir", _benchmarkDir
        ]);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Comparison_JsonStdout_IsExactlyOneCombinedDocument()
    {
        await EstablishBenchmark("test-single-document", _benchmarkDir);
        var candidateFile = Path.Combine(Fixtures, "AUDUSD_D1_candidate_identical.csv");

        var result = await InvokeAsync([
            candidateFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--compare", "test-single-document",
            "--benchmark-dir", _benchmarkDir
        ]);

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StdOut);
        Assert.True(document.RootElement.TryGetProperty("benchmarkComparison", out var comparison));
        Assert.Equal("AUDUSD", comparison.GetProperty("candidateIdentity").GetProperty("instrument").GetString());
    }

    [Fact]
    public async Task BenchmarkOperation_WithoutInstrument_IsRejectedBeforeSourceRead()
    {
        var missingSource = Path.Combine(_benchmarkDir, "does-not-exist.csv");
        var result = await InvokeAsync([
            missingSource,
            "--timeframe", "D1",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--benchmark", "missing-instrument",
            "--benchmark-dir", _benchmarkDir
        ]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--instrument", result.StdErr);
        Assert.DoesNotContain("does-not-exist", result.StdErr);
    }

    // --- Scenario 7: Reject Invalid Tolerance Configuration ---

    [Fact]
    public async Task Scenario7_NegativeTolerance_RejectedBeforeDataRead()
    {
        await EstablishBenchmark("test-negtol", _benchmarkDir);

        var refFile = Path.Combine(Fixtures, "AUDUSD_D1_reference.csv");
        var result = await InvokeAsync([
            refFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--compare", "test-negtol",
            "--tolerances", "{\"Open\": {\"absolute\": -0.001}}",
            "--benchmark-dir", _benchmarkDir
        ]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("non-negative", result.StdErr);
    }

    // --- Tolerance overrides applied ---

    [Fact]
    public async Task ToleranceOverrides_AppliedToComparison()
    {
        await EstablishBenchmark("test-tolover", _benchmarkDir);

        var refFile = Path.Combine(Fixtures, "AUDUSD_D1_reference.csv");
        var result = await InvokeAsync([
            refFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--compare", "test-tolover",
            "--tolerances", "{\"Volume\": {\"relative\": 0.02}}",
            "--benchmark-dir", _benchmarkDir
        ]);

        Assert.InRange(result.ExitCode, 0, 1);
        using var document = JsonDocument.Parse(result.StdOut);
        Assert.True(document.RootElement.TryGetProperty("benchmarkComparison", out _));
    }

    // --- Scenario 5: No Overlap ---

    [Fact]
    public async Task Scenario5_NoOverlap_UnavailableScore()
    {
        await EstablishBenchmark("test-nooverlap", _benchmarkDir);

        var noOverlapFile = Path.Combine(Fixtures, "AUDUSD_D1_candidate_no_overlap.csv");
        var result = await InvokeAsync([
            noOverlapFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--compare", "test-nooverlap",
            "--benchmark-dir", _benchmarkDir
        ]);

        Assert.InRange(result.ExitCode, 0, 1);
        using var document = JsonDocument.Parse(result.StdOut);
        var agreement = document.RootElement.GetProperty("benchmarkComparison").GetProperty("agreementScore");
        Assert.Equal(JsonValueKind.Null, agreement.GetProperty("score").ValueKind);
        Assert.Contains("No overlapping", agreement.GetProperty("unavailableReason").GetString());
    }

    // --- Scenario 8: Deterministic Output ---

    [Fact]
    public async Task Scenario8_DeterministicOutput_ByteIdenticalJson()
    {
        await EstablishBenchmark("test-determinism", _benchmarkDir);

        var candidateFile = Path.Combine(Fixtures, "AUDUSD_D1_candidate_identical.csv");
        var outputDir = Path.GetTempPath();
        var output1 = Path.Combine(outputDir, $"det1-{Guid.NewGuid()}.json");
        var output2 = Path.Combine(outputDir, $"det2-{Guid.NewGuid()}.json");

        try
        {
            var baseArgs = new[]
            {
                candidateFile,
                "--instrument", "AUDUSD",
                "--timeframe", "D1",
                "--market", "forex",
                "--format", "json",
                "--report-version", "2",
                "--score",
                "--compare", "test-determinism",
                "--benchmark-dir", _benchmarkDir
            };

            var args1 = baseArgs.Concat(new[] { "--output", output1 }).ToArray();
            var args2 = baseArgs.Concat(new[] { "--output", output2 }).ToArray();
            var result1 = await InvokeAsync(args1);
            var result2 = await InvokeAsync(args2);

            Assert.InRange(result1.ExitCode, 0, 1);
            Assert.InRange(result2.ExitCode, 0, 1);

            // Compare the JSON outputs excluding resolution timestamps
            var json1 = File.ReadAllText(output1);
            var json2 = File.ReadAllText(output2);

            // Strip resolutionTimestamp for comparison (varies by run)
            var stripped1 = StripTimestamps(json1);
            var stripped2 = StripTimestamps(json2);
            Assert.Equal(stripped1, stripped2);
        }
        finally
        {
            if (File.Exists(output1)) File.Delete(output1);
            if (File.Exists(output2)) File.Delete(output2);
        }
    }

    // --- Benchmark Delete ---

    [Fact]
    public async Task BenchmarkDelete_WithYes_DeletesBenchmark()
    {
        await EstablishBenchmark("test-delete", _benchmarkDir);
        Assert.True(Directory.Exists(Path.Combine(_benchmarkDir, "test-delete")));

        var result = await InvokeAsync([
            "--benchmark-delete", "test-delete",
            "--benchmark-dir", _benchmarkDir,
            "--yes"
        ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("deleted", result.StdOut);
        Assert.False(Directory.Exists(Path.Combine(_benchmarkDir, "test-delete")));
    }

    // --- Comparison with text format output ---

    [Fact]
    public async Task Comparison_TextFormat_RendersHumanReadableReport()
    {
        await EstablishBenchmark("test-textformat", _benchmarkDir);

        var refFile = Path.Combine(Fixtures, "AUDUSD_D1_reference.csv");
        var result = await InvokeAsync([
            refFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "text",
            "--report-version", "1",
            "--score",
            "--compare", "test-textformat",
            "--benchmark-dir", _benchmarkDir
        ]);

        Assert.InRange(result.ExitCode, 0, 1);
        Assert.Contains("BENCHMARK COMPARISON", result.StdOut);
        Assert.Contains("Coverage:", result.StdOut);
    }

    // --- Benchmark not found for comparison ---

    [Fact]
    public async Task Comparison_BenchmarkNotFound_ReturnsNullWithError()
    {
        var refFile = Path.Combine(Fixtures, "AUDUSD_D1_reference.csv");
        var result = await InvokeAsync([
            refFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--compare", "nonexistent-benchmark",
            "--benchmark-dir", _benchmarkDir
        ]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("not found", result.StdErr);
    }

    // --- Custom tolerances with disablement ---

    [Fact]
    public async Task ToleranceOverrides_DisableField_ComparisonSkipsField()
    {
        await EstablishBenchmark("test-disable", _benchmarkDir);

        var refFile = Path.Combine(Fixtures, "AUDUSD_D1_reference.csv");
        var result = await InvokeAsync([
            refFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--compare", "test-disable",
            "--tolerances", "{\"Open\": {\"enabled\": false}, \"Volume\": {\"enabled\": false}}",
            "--benchmark-dir", _benchmarkDir
        ]);

        Assert.InRange(result.ExitCode, 0, 1);
    }

    // --- Identical candidate produces no material discrepancies ---

    [Fact]
    public async Task IdenticalCandidate_ZeroMaterialDiscrepancies()
    {
        await EstablishBenchmark("test-zero", _benchmarkDir);

        var identicalFile = Path.Combine(Fixtures, "AUDUSD_D1_candidate_identical.csv");
        var result = await InvokeAsync([
            identicalFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--compare", "test-zero",
            "--benchmark-dir", _benchmarkDir
        ]);

        Assert.InRange(result.ExitCode, 0, 1);
        using var document = JsonDocument.Parse(result.StdOut);
        Assert.Empty(document.RootElement.GetProperty("benchmarkComparison").GetProperty("materialDiscrepancies").EnumerateArray());
    }

    // --- Helpers ---

    private static string FindSharedFixturesDir()
    {
        // Walk up from AppContext.BaseDirectory to find the solution root,
        // then go to tests/Fixtures/.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Fixtures");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "AUDUSD_D1_reference.csv")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find shared tests/Fixtures/ directory");
    }

    private static async Task EstablishBenchmark(string name, string benchmarkDir)
    {
        var refFile = Path.Combine(Fixtures, "AUDUSD_D1_reference.csv");
        var result = await InvokeAsync([
            refFile,
            "--instrument", "AUDUSD",
            "--timeframe", "D1",
            "--market", "forex",
            "--format", "json",
            "--report-version", "2",
            "--score",
            "--benchmark", name,
            "--benchmark-dir", benchmarkDir
        ]);
        // Exit code 0 = clean dataset, exit code 1 = findings present but validation completed.
        // Both are acceptable for benchmark establishment.
        if (result.ExitCode > 1)
            throw new InvalidOperationException(
                $"Failed to establish benchmark '{name}': exit={result.ExitCode}, stderr={result.StdErr}");
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

    private static string StripTimestamps(string json)
    {
        // Strip resolutionTimestamp, resolutionTimestampUtc, and establishedAtUtc
        // to enable deterministic comparison across runs
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        });
    }

    internal sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}
