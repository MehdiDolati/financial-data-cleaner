using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Validator.Application.Web;

namespace Validator.Parity.Tests;

// US4 benchmark/comparison tests (T055-T060): establishing a validated
// dataset as a named immutable benchmark, retrieving its recorded metrics,
// comparing a candidate with matched/missing/extra and material
// discrepancies, name-collision rejection, unknown-benchmark failure, and
// benchmark-agreement kept separate from the candidate score (FR-016,
// FR-017, SC-006).
public class ValidationWebServiceBenchmarkTests
{
    private const string BaseCsv =
        "date,time,open,high,low,close,volume\n" +
        "2026.01.05,00:00,0.63502,0.63650,0.63420,0.63612,118000\n" +
        "2026.01.06,00:00,0.63612,0.63780,0.63550,0.63720,132000\n" +
        "2026.01.07,00:00,0.63720,0.63890,0.63680,0.63850,115000\n" +
        "2026.01.08,00:00,0.63850,0.63920,0.63750,0.63810,128000\n" +
        "2026.01.09,00:00,0.63810,0.63950,0.63720,0.63900,122000\n";

    // One close value differs far beyond tolerance from the benchmark.
    private const string DriftedCsv =
        "date,time,open,high,low,close,volume\n" +
        "2026.01.05,00:00,0.63502,0.63650,0.63420,0.63612,118000\n" +
        "2026.01.06,00:00,0.63650,0.63950,0.63560,0.63890,132000\n" +
        "2026.01.07,00:00,0.63720,0.63890,0.63680,0.63850,115000\n" +
        "2026.01.08,00:00,0.63850,0.63920,0.63750,0.63810,128000\n" +
        "2026.01.09,00:00,0.63810,0.63950,0.63720,0.63900,122000\n";

    private static async Task<WebRunId> SubmitAsync(
        WebComposition composition,
        WebRunOperation operation,
        string name,
        string content,
        string? benchmarkName = null,
        bool score = true)
    {
        var options = WebComposition.Options() with
        {
            Timeframe = "D1",
            Score = score,
            BenchmarkName = benchmarkName,
            Instrument = benchmarkName is null ? null : "AUDUSD"
        };
        var accepted = await composition.Service.SubmitAsync(new WebRunRequest(
            operation, name, new MemoryStream(Encoding.UTF8.GetBytes(content)), options));

        return ((WebRunSubmission.Accepted)accepted).Id;
    }

    [Fact]
    public async Task Establish_persists_a_named_benchmark_with_recorded_metrics()
    {
        using var composition = new WebComposition();
        var id = await SubmitAsync(
            composition, WebRunOperation.EstablishBenchmark, "base.csv", BaseCsv, "audusd-d1");

        var retrieval = await composition.Service.GetResultAsync(id);
        var view = ((WebResultRetrieval.Ready)retrieval).View;

        view.Status.Should().Be(WebRunStatus.CompletedClean);
        view.Benchmark.Should().NotBeNull("the establish operation records its snapshot (FR-016)");
        view.Benchmark!.Name.Should().Be("audusd-d1");
        view.Benchmark.RecordedMetrics.Should().NotBeEmpty("the recorded scores are part of the snapshot");
        view.Comparison.Should().BeNull("an establish run carries no comparison");
    }

    [Fact]
    public async Task Establish_is_immutable_a_second_run_with_the_same_name_fails()
    {
        using var composition = new WebComposition();
        await SubmitAsync(composition, WebRunOperation.EstablishBenchmark, "base.csv", BaseCsv, "audusd-d1");

        // A second establishment with different bytes under the same name is
        // a genuinely new run, rejected by the store's name-collision rule
        // (FR-003, SC-006). Identical bytes would deterministically join the
        // existing run instead (FR-010).
        var drifted = BaseCsv.Replace("0.63900", "0.63999");
        var secondId = await SubmitAsync(composition, WebRunOperation.EstablishBenchmark, "base.csv", drifted, "audusd-d1");

        var status = await composition.Service.GetStatusAsync(secondId);
        status.Should().BeOfType<WebRunStatusResult.Known>()
            .Which.Status.Should().Be(WebRunStatus.Failed);

        var retrieval = await composition.Service.GetResultAsync(secondId);
        var view = ((WebResultRetrieval.Ready)retrieval).View;
        view.Diagnostic.Should().NotBeNull();
    }

    [Fact]
    public async Task Compare_reports_matched_and_material_discrepancies()
    {
        using var composition = new WebComposition();
        await SubmitAsync(composition, WebRunOperation.EstablishBenchmark, "base.csv", BaseCsv, "audusd-d1");

        var compareId = await SubmitAsync(
            composition, WebRunOperation.Compare, "drifted.csv", DriftedCsv, "audusd-d1");

        var retrieval = await composition.Service.GetResultAsync(compareId);
        var view = ((WebResultRetrieval.Ready)retrieval).View;

        view.Status.Should().Be(WebRunStatus.CompletedClean,
            "comparison discrepancies do not fail the candidate (FR-026)");
        view.Comparison.Should().NotBeNull("the compare operation records its evidence (FR-017)");

        var comparison = view.Comparison!.Comparison;
        comparison.Coverage.MatchedCount.Should().BeGreaterThan(0,
            "five timestamps align between benchmark and candidate");
        comparison.MaterialDiscrepancies.Should().NotBeEmpty("one close value differs materially");
        comparison.MissingFromCandidateTimestamps.Should().BeEmpty();
        comparison.ExtraInCandidateTimestamps.Should().BeEmpty();
        comparison.AgreementScore.Should().NotBeNull("the agreement score is its own member (FR-016)");
    }

    [Fact]
    public async Task Compare_against_an_unknown_benchmark_fails_with_a_diagnostic()
    {
        using var composition = new WebComposition();
        var compareId = await SubmitAsync(
            composition, WebRunOperation.Compare, "base.csv", BaseCsv, "missing-benchmark");

        var status = await composition.Service.GetStatusAsync(compareId);
        status.Should().BeOfType<WebRunStatusResult.Known>()
            .Which.Status.Should().Be(WebRunStatus.Failed);

        var retrieval = await composition.Service.GetResultAsync(compareId);
        var view = ((WebResultRetrieval.Ready)retrieval).View;
        view.Diagnostic.Should().NotBeNull();
        view.Comparison.Should().BeNull("a failed compare carries no evidence (FR-011)");
    }

    [Fact]
    public async Task Validate_operation_never_touches_the_benchmark_store()
    {
        using var composition = new WebComposition();
        await SubmitAsync(composition, WebRunOperation.Validate, "base.csv", BaseCsv);

        var benchmarkDir = Path.Combine(composition.Root, "benchmarks");
        if (Directory.Exists(benchmarkDir))
        {
            Directory.GetDirectories(benchmarkDir).Should().BeEmpty(
                "a plain validate run never creates or reads benchmarks");
        }
    }
}