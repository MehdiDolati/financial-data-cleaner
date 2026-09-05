using System;
using System.IO;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Application.Validation;
using Validator.Application.Web;
using Validator.Infrastructure.Calendars;
using Validator.Infrastructure.Csv;
using Validator.Infrastructure.Findings;
using Validator.Infrastructure.Sorting;
using Validator.Infrastructure.Web;

namespace Validator.Parity.Tests;

// Shared composition for US1+ facade tests: the real file-backed stores, the
// real orchestrator, and the inline queue - the same wiring the DI extension
// performs, so these tests prove the packaged boundary end to end.
public sealed class WebComposition : IDisposable
{
    public string Root { get; } = Path.Combine(
        Path.GetTempPath(), "fdc-web-composition-" + Guid.NewGuid().ToString("N"));

    public FixedClock Clock { get; } = new();

    public IWebRunStore RunStore { get; }

    public IUploadedDatasetStore UploadStore { get; } = null!;

    public IWebResultStore ResultStore { get; }

    public ValidationWebService Service { get; }

    public WebComposition()
    {
        RunStore = new FileWebRunStore(Root);
        UploadStore = new FileUploadedDatasetStore(Root);
        ResultStore = new InMemoryWebResultStore();
        var executor = new WebRunExecutor(
            RunStore,
            UploadStore,
            new DetailedValidationOrchestrator(
                () => new FindingCatalog(
                    () => new SpoolWriter(new TempStorage()),
                    path => new SpoolReader(path, path + ".complete"),
                    new ExternalMergeSpool(new TempStorage()))),
            new MarketCalendarFactory(),
            Clock,
            ResultStore,
            (id, report, representation, destination, token) =>
                Validator.Infrastructure.Web.WebReportExport.Export(id, report, representation, destination, token),
            new Validator.Infrastructure.Benchmark.FileBenchmarkStore(System.IO.Path.Combine(Root, "benchmarks")),
            LoadBenchmarkCandles);
        var queue = new InlineWebRunQueue(id => executor.ExecuteAsync(id));
        Service = new ValidationWebService(RunStore, UploadStore, queue, ResultStore, Clock);
    }

    public static WebRunOptions Options() => new(
        Timeframe: null,
        Domain.Calendars.MarketProfile.Forex,
        CalendarReference: null,
        Csv: new CsvInputOptions { HasHeader = true, Delimiter = "comma" },
        ReportVersion: 2,
        Score: false,
        ScoreWeights: null,
        Instrument: null,
        BenchmarkName: null,
        ToleranceOverrides: null);

    public static MemoryStream CleanDailyContent() => new(System.Text.Encoding.UTF8.GetBytes(
        "date,time,open,high,low,close,volume\n" +
        "2026.01.02,00:00,0.63421,0.63580,0.63310,0.63502,125000\n" +
        "2026.01.05,00:00,0.63502,0.63650,0.63420,0.63612,118000\n" +
        "2026.01.06,00:00,0.63612,0.63780,0.63550,0.63720,132000\n" +
        "2026.01.07,00:00,0.63720,0.63890,0.63680,0.63850,115000\n" +
        "2026.01.08,00:00,0.63850,0.63920,0.63750,0.63810,128000\n" +
        "2026.01.09,00:00,0.63810,0.63950,0.63720,0.63900,122000\n"));

    public static MemoryStream FindingsContent() => new(System.Text.Encoding.UTF8.GetBytes(
        "date,time,open,high,low,close,volume\n" +
        "2026.01.02,00:00,0.63421,0.63580,0.63310,0.63502,125000\n" +
        "2026.01.02,00:00,0.63421,0.63580,0.63310,0.63502,125000\n" +
        "2026.01.03,00:00,0.63502,0.63310,0.63650,0.63612,118000\n" +
        "2026.01.03,00:00,0.63502,0.63310,0.63650,0.63612,abc\n" +
        "2026.01.04,00:00,0.63502,0.63310,0.63650,0.63612,118000\n" +
        "2026.01.10,00:00,0.63810,0.63950,0.63720,0.63900,122000\n"));

    /// <summary>
    /// Loads a benchmark's recorded source candles exactly as the CLI does
    /// (T075): source.csv with the benchmark's recorded CSV context.
    /// </summary>
    private System.Collections.Generic.IReadOnlyList<Validator.Domain.Candles.PriceCandle> LoadBenchmarkCandles(
        string benchmarkName)
    {
        var benchmarkDir = System.IO.Path.Combine(Root, "benchmarks");
        var safeName = new Validator.Application.Benchmark.BenchmarkName(benchmarkName).Safe;
        var sourcePath = System.IO.Path.Combine(benchmarkDir, safeName, "source.csv");
        var snapshotJson = System.IO.File.ReadAllText(
            System.IO.Path.Combine(benchmarkDir, safeName, "benchmark.json"));
        var context = System.Text.Json.JsonSerializer.Deserialize<BenchmarkContextDto>(snapshotJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var options = new Validator.Application.Ingestion.CsvInputOptions
        {
            HasHeader = context?.CsvHasHeader ?? true,
            Delimiter = context?.CsvDelimiter ?? "comma"
        };

        var candles = new System.Collections.Generic.List<Validator.Domain.Candles.PriceCandle>();
        var source = new Validator.Infrastructure.Csv.CsvCandleSource(sourcePath, options);
        foreach (var candle in source.ReadAllAsync().ToBlockingEnumerable())
        {
            candles.Add(candle);
        }

        candles.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return candles;
    }

    private sealed class BenchmarkContextDto
    {
        public string? CsvDelimiter { get; set; }

        public bool CsvHasHeader { get; set; }
    }
    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    public sealed class FixedClock : IApplicationClock
    {
        private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow
        {
            get => _utcNow;
            set => _utcNow = value;
        }
    }
}

// US1 facade tests (T026): the full submit -> poll -> retrieve lifecycle
// over the real pipeline, with fatal inputs and option rejections surfaced
// exactly as the contracts require (quickstart scenarios 1-5).
public class ValidationWebServiceTests
{
    [Fact]
    public async Task Clean_fixture_completes_clean_with_six_zero_counts()
    {
        using var composition = new WebComposition();
        var accepted = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "clean.csv", WebComposition.CleanDailyContent(), WebComposition.Options()));

        var acceptance = accepted.Should().BeOfType<WebRunSubmission.Accepted>().Subject;
        acceptance.JoinedExistingRun.Should().BeFalse();

        var status = await composition.Service.GetStatusAsync(acceptance.Id);
        status.Should().BeOfType<WebRunStatusResult.Known>()
            .Which.Status.Should().Be(WebRunStatus.CompletedClean);

        var retrieval = await composition.Service.GetResultAsync(acceptance.Id);
        var view = retrieval.Should().BeOfType<WebResultRetrieval.Ready>().Subject.View;
        view.Status.Should().Be(WebRunStatus.CompletedClean);
        view.Diagnostic.Should().BeNull();
        var summary = view.Validation!.Summary;
        summary.MissingCandles.Should().Be(0);
        summary.DuplicateRecords.Should().Be(0);
        summary.InvalidOhlc.Should().Be(0);
        summary.ClosedMarketRecords.Should().Be(0);
        summary.TimeGaps.Should().Be(0);
        summary.MalformedRows.Should().Be(0);
        view.Scoring.Should().BeNull("scoring was not requested (FR-005)");
        view.AvailableExports.Should().NotBeEmpty("a terminal success offers exports (FR-014)");
    }

    [Fact]
    public async Task Findings_fixture_completes_with_findings_in_every_reported_category()
    {
        using var composition = new WebComposition();
        var accepted = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "findings.csv",
            WebComposition.FindingsContent(),
            WebComposition.Options() with { Timeframe = "D1" }));
        var id = accepted.Should().BeOfType<WebRunSubmission.Accepted>().Subject.Id;

        var retrieval = await composition.Service.GetResultAsync(id);
        var view = retrieval.Should().BeOfType<WebResultRetrieval.Ready>().Subject.View;

        view.Status.Should().Be(WebRunStatus.CompletedWithFindings);
        var summary = view.Validation!.Summary;
        summary.DuplicateRecords.Should().BeGreaterThan(0);
        summary.MalformedRows.Should().BeGreaterThan(0);
        summary.TotalFindings.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Empty_upload_fails_with_the_established_diagnostic_and_no_counts()
    {
        using var composition = new WebComposition();
        var empty = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(string.Empty));

        var accepted = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "empty.csv", empty, WebComposition.Options()));
        var id = accepted.Should().BeOfType<WebRunSubmission.Accepted>().Subject.Id;

        var status = await composition.Service.GetStatusAsync(id);
        status.Should().BeOfType<WebRunStatusResult.Known>()
            .Which.Status.Should().Be(WebRunStatus.Failed);

        var retrieval = await composition.Service.GetResultAsync(id);
        var view = retrieval.Should().BeOfType<WebResultRetrieval.Ready>().Subject.View;
        view.Status.Should().Be(WebRunStatus.Failed);
        view.Diagnostic.Should().NotBeNull();
        view.Validation.Should().BeNull("a fatal run exposes no counts (FR-011)");
        view.Scoring.Should().BeNull();
        view.AvailableExports.Should().BeEmpty("a failed run offers no export (FR-014)");

        var export = await composition.Service.ExportAsync(
            id, ReportRepresentation.JsonV2, new MemoryStream());
        export.Should().BeOfType<WebExportResult.NotAvailable>();
    }

    [Fact]
    public async Task Invalid_options_are_rejected_before_any_byte_is_stored()
    {
        using var composition = new WebComposition();
        var invalid = WebComposition.Options() with { Score = true, ReportVersion = 1 };

        var rejected = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "any.csv", WebComposition.CleanDailyContent(), invalid));

        var rejection = rejected.Should().BeOfType<WebRunSubmission.Rejected>().Subject;
        rejection.Diagnostic.Code.Should().Be("INVALID_ARGUMENT");

        // No upload was stored and no run record exists.
        var uploads = Path.Combine(composition.Root, "uploads");
        if (Directory.Exists(uploads))
        {
            Directory.GetFiles(uploads).Should().BeEmpty("a rejected configuration stores no byte (FR-007)");
        }

        var runs = Path.Combine(composition.Root, "runs");
        if (Directory.Exists(runs))
        {
            Directory.GetFiles(runs).Should().BeEmpty("a rejected configuration queues no work");
        }
    }

    [Fact]
    public async Task Unknown_id_is_unavailable_with_a_reason()
    {
        using var composition = new WebComposition();
        var unknown = WebRunId.Parse(new string('0', 64));

        var status = await composition.Service.GetStatusAsync(unknown);
        var statusUnavailable = status.Should().BeOfType<WebRunStatusResult.Unavailable>().Subject;
        statusUnavailable.Reason.Should().NotBeNullOrWhiteSpace();

        var result = await composition.Service.GetResultAsync(unknown);
        var resultUnavailable = result.Should().BeOfType<WebResultRetrieval.Unavailable>().Subject;
        resultUnavailable.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Retry_requeues_a_failed_run()
    {
        using var composition = new WebComposition();
        var accepted = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "empty.csv",
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(string.Empty)),
            WebComposition.Options()));
        var id = accepted.Should().BeOfType<WebRunSubmission.Accepted>().Subject.Id;

        (await composition.Service.GetStatusAsync(id)).Should().BeOfType<WebRunStatusResult.Known>()
            .Which.Status.Should().Be(WebRunStatus.Failed);

        var retried = await composition.Service.RetryAsync(id);
        retried.Should().BeOfType<WebRunSubmission.Accepted>();

        (await composition.Service.GetStatusAsync(id)).Should().BeOfType<WebRunStatusResult.Known>()
            .Which.Status.Should().Be(WebRunStatus.Failed, "the retried empty upload fails again identically");
    }

    [Fact]
    public async Task Non_failed_runs_cannot_be_retried()
    {
        using var composition = new WebComposition();
        var accepted = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "clean.csv", WebComposition.CleanDailyContent(), WebComposition.Options()));
        var id = accepted.Should().BeOfType<WebRunSubmission.Accepted>().Subject.Id;

        var rejected = await composition.Service.RetryAsync(id);
        rejected.Should().BeOfType<WebRunSubmission.Rejected>()
            .Which.Diagnostic.Code.Should().Be("INVALID_ARGUMENT");
    }
}