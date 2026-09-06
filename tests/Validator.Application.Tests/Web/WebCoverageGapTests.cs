using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Benchmark;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Application.Web;
using Validator.Domain.Candles;
using Validator.Domain.Findings;

namespace Validator.Application.Tests.Web;

// Residual line/branch gaps in the web surface (spec 006), closed per
// docs/coverage-exclusion-policy.md: every reachable defensive arm is
// exercised through a legal call - constructor guards, null guards,
// out-of-range enum casts, the exception classification table, the
// executor's establish/compare/failure/cancellation paths over faked
// ports, and every view-section projection.
public class WebCoverageGapTests
{
    private static readonly DateTimeOffset Utc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static SourceIdentity Source() => new("dataset.csv", 100, new string('a', 64));

    private static WebRunOptions Options() => new(
        Timeframe: null,
        Domain.Calendars.MarketProfile.Forex,
        CalendarReference: null,
        Csv: new CsvInputOptions(),
        ReportVersion: 2,
        Score: false,
        ScoreWeights: null,
        Instrument: null,
        BenchmarkName: null,
        ToleranceOverrides: null);

    private static FatalDiagnostic Diagnostic() => new(
        "INVALID_ARGUMENT", "The supplied options could not be applied.", "Correct the reported option.");

    private static ValidationContextSnapshot Context() => new(
        "D1",
        new CalendarContext("forex", "Forex"),
        TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+00:00"),
        "comma", false, null);

    private static CheckExecution[] SixChecks() =>
    [
        new CheckExecution(CheckName.MissingCandles, CheckStatus.Completed),
        new CheckExecution(CheckName.DuplicateRecords, CheckStatus.Completed),
        new CheckExecution(CheckName.InvalidOhlc, CheckStatus.Completed),
        new CheckExecution(CheckName.ClosedMarketRecords, CheckStatus.Completed),
        new CheckExecution(CheckName.TimeGaps, CheckStatus.Completed),
        new CheckExecution(CheckName.MalformedRows, CheckStatus.Completed)
    ];

    private static DetailedValidationReport CleanReport(bool withScore = false)
    {
        var coverage = new ScanCoverage(1, 1, 0);
        var categories = new[]
        {
            new CategoryReconciliation(FindingCategory.MissingCandle, 0, 0, 0),
            new CategoryReconciliation(FindingCategory.DuplicateRecord, 0, 0, 0),
            new CategoryReconciliation(FindingCategory.InvalidOhlc, 0, 0, 0),
            new CategoryReconciliation(FindingCategory.ClosedMarketRecord, 0, 0, 0),
            new CategoryReconciliation(FindingCategory.TimeGap, 0, 0, 0),
            new CategoryReconciliation(FindingCategory.MalformedRow, 0, 0, 0)
        };
        var report = new DetailedValidationReport(
            Source(),
            Context(),
            coverage,
            SixChecks(),
            new DetailedSummary(0, 0, 0, 0, 0, 0),
            new ReportReconciliation(categories, coverage),
            new EmptyCatalog());
        return withScore ? report with { Score = ReportScore() } : report;
    }

    private static DatasetScoreReport ReportScore()
    {
        var metrics = MetricPopulationMap.CanonicalOrder
            .Select(cat => MetricScoreCalculator.ScoreMetric(cat, 0, 10, MetricPopulationMap.KindFor(cat)))
            .ToList();
        return new DatasetScoreReport(
            metrics,
            ScoreWeightResolver.Default(),
            DatasetScore.Available(
                new Domain.Scoring.ScoreValue(new Domain.Scoring.ExactRatio(100, 1)),
                MetricPopulationMap.CanonicalOrder.ToList(),
                []));
    }

    // ------------------------------------------------------------- SafeName

    [Theory]
    [InlineData("clean.csv", "clean.csv")]
    [InlineData("  padded.csv  ", "padded.csv")]
    [InlineData(@"C:\uploads\clean.csv", "clean.csv")]
    [InlineData("/tmp/upload/clean.csv", "clean.csv")]
    [InlineData(@"..\..\evil.csv", "evil.csv")]
    [InlineData("folder/", "upload.csv")]
    [InlineData(@"folder\", "upload.csv")]
    public async Task Submitted_file_names_reduce_to_a_safe_base_name(string submitted, string expected)
    {
        // SafeName is private on the facade; it is exercised through
        // SubmitAsync, which stores the upload under the safe name.
        using var composition = new FakeWebComposition();
        await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, submitted,
            new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2")), Options()));

        composition.StoredNames.Should().Contain(expected);
    }

    // ------------------------------------------------- facade null guards

    [Fact]
    public void Facade_constructor_rejects_null_ports()
    {
        var clock = new FixedClock();
        using var composition = new FakeWebComposition();

        var act = () => new ValidationWebService(null!, composition.UploadStore, composition.Queue, composition.ResultStore, clock);
        act.Should().Throw<ArgumentNullException>();

        var act2 = () => new ValidationWebService(composition.RunStore, null!, composition.Queue, composition.ResultStore, clock);
        act2.Should().Throw<ArgumentNullException>();

        var act3 = () => new ValidationWebService(composition.RunStore, composition.UploadStore, null!, composition.ResultStore, clock);
        act3.Should().Throw<ArgumentNullException>();

        var act4 = () => new ValidationWebService(composition.RunStore, composition.UploadStore, composition.Queue, null!, clock);
        act4.Should().Throw<ArgumentNullException>();

        var act5 = () => new ValidationWebService(composition.RunStore, composition.UploadStore, composition.Queue, composition.ResultStore, null!);
        act5.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Facade_methods_reject_null_arguments()
    {
        using var composition = new FakeWebComposition();

        await FluentActions.Awaiting(() => composition.Service.SubmitAsync(null!).AsTask())
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");

        await FluentActions.Awaiting(() => composition.Service.GetStatusAsync(null!).AsTask())
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("id");

        await FluentActions.Awaiting(() => composition.Service.GetResultAsync(null!).AsTask())
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("id");

        await FluentActions.Awaiting(() => composition.Service.ExportAsync(null!, ReportRepresentation.JsonV2, new MemoryStream()).AsTask())
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("id");

        await FluentActions.Awaiting(() => composition.Service.ExportAsync(
                WebRunId.Parse(new string('0', 64)), ReportRepresentation.JsonV2, null!).AsTask())
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("destination");

        await FluentActions.Awaiting(() => composition.Service.RetryAsync(null!).AsTask())
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("id");
    }

    // ------------------------------------------------------- retrieval gaps

    [Fact]
    public async Task A_terminal_success_run_without_a_stored_result_is_an_invariant_failure()
    {
        using var composition = new FakeWebComposition(useCase: new SucceedingUseCase());
        var id = await composition.SubmitAsync(Options());

        // The result artifact is the executor's responsibility; a terminal
        // record without one is a broken invariant, not an empty success.
        composition.ResultStore.Clear();

        var act = () => composition.Service.GetResultAsync(id).AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_failed_run_without_a_stored_result_is_still_retrievable()
    {
        // The failure view reads only the record; the result store is never
        // consulted for a Failed run.
        using var composition = new FakeWebComposition();
        var id = await composition.SubmitAsync(Options());

        composition.ResultStore.Clear();
        var record = await composition.RunStore.FindAsync(id);
        record!.Status.Should().Be(WebRunStatus.Failed);

        var retrieval = await composition.Service.GetResultAsync(id);
        retrieval.Should().BeOfType<WebResultRetrieval.Ready>()
            .Which.View.Status.Should().Be(WebRunStatus.Failed);
    }

    [Fact]
    public async Task Export_of_an_unavailable_representation_is_not_available()
    {
        using var composition = new FakeWebComposition(useCase: new SucceedingUseCase());
        var id = await composition.SubmitAsync(Options() with { Score = true });

        // The available-exports list never contains (ReportRepresentation)99.
        var result = await composition.Service.ExportAsync(
            id, (ReportRepresentation)99, new MemoryStream());
        result.Should().BeOfType<WebExportResult.NotAvailable>();
    }

    // ------------------------------------------------------ id utilities

    [Fact]
    public void Parse_rejects_null_and_the_full_value_is_the_to_string()
    {
        var act = () => WebRunId.Parse(null!);
        act.Should().Throw<ArgumentException>();

        var id = WebRunId.Derive(Source(), Options(), WebRunOperation.Validate);
        id.ToString().Should().Be(id.Value);
    }

    // ------------------------------------------------------ status guard

    [Fact]
    public void IsAllowed_is_false_for_an_unknown_source_state()
    {
        WebRunStatusGuard.IsAllowed((WebRunStatus)99, WebRunStatus.Running).Should().BeFalse();
        WebRunStatusGuard.IsAllowed((WebRunStatus)99, (WebRunStatus)98).Should().BeFalse();
    }

    // ------------------------------------------------ transition payload

    [Fact]
    public void Transition_payloads_expose_is_retry()
    {
        WebRunTransitionData.ForRunning().IsRetry.Should().BeTrue();
        WebRunTransitionData.ForRetry().IsRetry.Should().BeTrue();
        WebRunTransitionData.ForSuccess("result.json", Utc).IsRetry.Should().BeFalse();
        WebRunTransitionData.ForFailure(Diagnostic(), Utc).IsRetry.Should().BeFalse();
    }

    // ----------------------------------------------------- record guards

    [Fact]
    public void Record_constructor_rejects_null_aggregates()
    {
        var act = () => new WebRunRecord(
            null!, WebRunOperation.Validate, Source(), Options(), submittedAtUtc: Utc);
        act.Should().Throw<ArgumentNullException>();

        var act2 = () => new WebRunRecord(
            WebRunId.Derive(Source(), Options(), WebRunOperation.Validate),
            WebRunOperation.Validate, null!, Options(), submittedAtUtc: Utc);
        act2.Should().Throw<ArgumentNullException>();

        var act3 = () => new WebRunRecord(
            WebRunId.Derive(Source(), Options(), WebRunOperation.Validate),
            WebRunOperation.Validate, Source(), null!, submittedAtUtc: Utc);
        act3.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(WebRunStatus.Pending)]
    [InlineData(WebRunStatus.Running)]
    public void Apply_rejects_a_null_payload(WebRunStatus status)
    {
        var record = PendingRecord();
        var act = () => record.Apply(status, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Apply_into_Failed_requires_a_diagnostic()
    {
        var record = PendingRecord().ToRunning();
        var act = () => record.Apply(WebRunStatus.Failed, WebRunTransitionData.ForRunning());
        act.Should().Throw<ArgumentException>().WithMessage("*diagnostic*");
    }

    [Fact]
    public void Apply_into_completed_requires_a_reference()
    {
        var record = PendingRecord().ToRunning();
        var act = () => record.Apply(WebRunStatus.CompletedClean, WebRunTransitionData.ForRunning());
        act.Should().Throw<ArgumentException>().WithMessage("*result reference*");
    }

    [Fact]
    public void Apply_into_Running_rejects_a_terminal_payload()
    {
        var record = PendingRecord();
        var act = () => record.Apply(
            WebRunStatus.Running, WebRunTransitionData.ForSuccess("result.json", Utc));
        act.Should().Throw<ArgumentException>().WithMessage("*no terminal payload*");
    }

    [Fact]
    public void Apply_dispatches_every_legal_transition_kind()
    {
        var running = PendingRecord().Apply(
            WebRunStatus.Running, WebRunTransitionData.ForRunning());
        running.Status.Should().Be(WebRunStatus.Running);

        var failed = running.Apply(WebRunStatus.Failed, WebRunTransitionData.ForFailure(Diagnostic(), Utc));
        failed.Status.Should().Be(WebRunStatus.Failed);

        var retry = failed.Apply(WebRunStatus.Pending, WebRunTransitionData.ForRetry());
        retry.Status.Should().Be(WebRunStatus.Pending);

        var completed = PendingRecord().ToRunning().Apply(
            WebRunStatus.CompletedClean, WebRunTransitionData.ForSuccess("result.json", Utc));
        completed.Status.Should().Be(WebRunStatus.CompletedClean);
        completed.ResultReference.Should().Be("result.json");
    }

    [Fact]
    public void Apply_rejects_an_unknown_target_state()
    {
        var record = PendingRecord();
        var act = () => record.Apply((WebRunStatus)99, WebRunTransitionData.ForRetry());
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToCompleted_rejects_a_blank_reference()
    {
        var running = PendingRecord().ToRunning();
        var act = () => running.ToCompleted("   ", isClean: true, terminalAtUtc: Utc);
        act.Should().Throw<ArgumentException>().WithMessage("*result reference*");
    }

    [Fact]
    public void A_completed_record_rejects_further_transitions()
    {
        var completed = PendingRecord().ToRunning()
            .ToCompleted("result.json", isClean: true, terminalAtUtc: Utc);

        var act = () => completed.ToRunning();
        act.Should().Throw<InvalidOperationException>();
    }

    // ------------------------------------------------- request guards

    [Fact]
    public void Request_constructor_rejects_invalid_arguments()
    {
        var options = Options();
        var stream = new MemoryStream();

        var act = () => new WebRunRequest(WebRunOperation.Validate, "", stream, options);
        act.Should().Throw<ArgumentException>();

        var act2 = () => new WebRunRequest(WebRunOperation.Validate, "  ", stream, options);
        act2.Should().Throw<ArgumentException>();

        var act3 = () => new WebRunRequest(WebRunOperation.Validate, "name.csv", null!, options);
        act3.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("content");

        var act4 = () => new WebRunRequest(WebRunOperation.Validate, "name.csv", stream, null!);
        act4.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("options");
    }

    [Fact]
    public void Canonical_options_string_rejects_an_unknown_operation()
    {
        var act = () => Options().ToCanonicalOptionsString((WebRunOperation)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Canonical_options_string_handles_a_null_csv_record()
    {
        // Csv is non-null through every factory, but the record shape allows
        // null; the canonicalization must not throw for the id derivation.
        var options = Options() with { Csv = null! };
        var canonical = options.ToCanonicalOptionsString(WebRunOperation.Validate);
        canonical.Should().NotBeNullOrWhiteSpace();

        // The default options are used, so the id equals the one derived from
        // an explicitly default CsvInputOptions.
        options.ToCanonicalOptionsString(WebRunOperation.Validate)
            .Should().Be(Options().ToCanonicalOptionsString(WebRunOperation.Validate));
    }

    [Fact]
    public void Validator_rejects_null_options()
    {
        var act = () => WebRunOptionsValidator.Validate(WebRunOperation.Validate, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // --------------------------------------------------- executor gaps

    [Fact]
    public async Task Executor_rejects_unknown_and_non_pending_runs()
    {
        using var composition = new FakeWebComposition(useCase: new SucceedingUseCase());
        var unknown = WebRunId.Parse(new string('0', 64));

        var act = () => composition.Executor.ExecuteAsync(unknown);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // A terminal record is never re-executed (idempotency).
        var id = await composition.SubmitAsync(Options());
        var record = await composition.RunStore.FindAsync(id);
        record!.Status.Should().Be(WebRunStatus.CompletedClean);
        await composition.Executor.ExecuteAsync(id);
        (await composition.RunStore.FindAsync(id)).Should().Be(record);
    }

    [Fact]
    public void Executor_maps_every_exception_family_to_its_diagnostic_code()
    {
        // The classification table mirrors the CLI's established mapping;
        // every arm is exercised with its own exception family.
        WebRunExecutor.ToFatalDiagnostic(
                new System.Text.DecoderFallbackException(), "file.csv")
            .Code.Should().Be("INVALID_ENCODING");

        WebRunExecutor.ToFatalDiagnostic(
                new InvalidDataException("Invalid CSV: the header is missing."), "file.csv")
            .Code.Should().Be("INVALID_CSV");

        WebRunExecutor.ToFatalDiagnostic(
                new InvalidDataException("The active layout requires a timestamp column."), "file.csv")
            .Code.Should().Be("INVALID_STRUCTURE");

        WebRunExecutor.ToFatalDiagnostic(
                new InvalidOperationException("The source could not be prepared."), "file.csv")
            .Code.Should().Be("INVALID_STRUCTURE");

        WebRunExecutor.ToFatalDiagnostic(
                new IOException("disk failure"), "file.csv")
            .Code.Should().Be("SOURCE_UNAVAILABLE");

        WebRunExecutor.ToFatalDiagnostic(
                new UnauthorizedAccessException("denied"), "file.csv")
            .Code.Should().Be("SOURCE_UNAVAILABLE");

        WebRunExecutor.ToFatalDiagnostic(
                new ArgumentException("bad option"), "file.csv")
            .Code.Should().Be("INVALID_ARGUMENT");
    }

    [Fact]
    public async Task A_cancelled_run_reraises_the_cancellation_without_failing_the_run()
    {
        // OperationCanceledException is deliberately rethrown - never
        // converted into a fatal diagnostic - so the host can stop cleanly.
        using var composition = new FakeWebComposition(
            uploadStore: new CancellingUploadStore(),
            useCase: new CancellingValidationUseCase());
        var id = await composition.SubmitPendingAsync();

        var act = () => composition.Executor.ExecuteAsync(id, new CancellationToken(canceled: true));
        await act.Should().ThrowAsync<OperationCanceledException>();

        var record = await composition.RunStore.FindAsync(id);
        record!.Status.Should().Be(WebRunStatus.Running,
            "cancellation rethrows before any terminal transition");
    }

    [Fact]
    public async Task An_ingestion_failure_completes_the_run_as_failed()
    {
        // The upload store throws an IOException; the executor's catch maps
        // it to SOURCE_UNAVAILABLE and the run ends Failed, never partial.
        using var composition = new FakeWebComposition(
            uploadStore: new ThrowingUploadStore(new IOException("read failure")),
            useCase: new SucceedingUseCase());
        var id = await composition.SubmitPendingAsync();

        await composition.Executor.ExecuteAsync(id);

        var record = await composition.RunStore.FindAsync(id);
        record!.Status.Should().Be(WebRunStatus.Failed);
        record.Diagnostic!.Code.Should().Be("SOURCE_UNAVAILABLE");
    }

    [Fact]
    public async Task A_failed_validation_outcome_carries_the_source_name()
    {
        // The use case returns a Failed outcome; WithSourceName attaches the
        // partial source identity when the diagnostic carries none.
        using var composition = new FakeWebComposition(useCase: new FailingUseCase());
        var id = await composition.SubmitPendingAsync();

        await composition.Executor.ExecuteAsync(id);

        var record = await composition.RunStore.FindAsync(id);
        record!.Status.Should().Be(WebRunStatus.Failed);
        record.Diagnostic!.Source.Should().NotBeNull("the source name is attached when absent");
        record.Diagnostic.Source!.FileName.Should().Be("dataset.csv");
    }

    [Fact]
    public async Task A_scored_run_persists_the_scoring_section_and_completed_state()
    {
        using var composition = new FakeWebComposition(useCase: new SucceedingUseCase());
        var options = Options() with { Score = true };
        var id = await composition.SubmitPendingAsync(options);

        await composition.Executor.ExecuteAsync(id);

        var record = await composition.RunStore.FindAsync(id);
        record!.Status.Should().Be(WebRunStatus.CompletedClean);

        var result = await composition.ResultStore.FindAsync(id);
        result!.Scoring.Should().NotBeNull("scoring was requested");
        result.Scoring!.Score.Dataset.Should().NotBeNull();
    }

    [Fact]
    public async Task A_custom_score_weight_string_is_parsed_into_the_request()
    {
        // ScoreWeights non-null selects the weighted ScoreRequest arm.
        using var composition = new FakeWebComposition(useCase: new WeightedUseCase());
        var options = Options() with
        {
            Score = true,
            ScoreWeights = "missingCandles=1,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,timeGaps=1,malformedRows=1"
        };
        var id = await composition.SubmitPendingAsync(options);

        await composition.Executor.ExecuteAsync(id);

        var record = await composition.RunStore.FindAsync(id);
        record!.Status.Should().Be(WebRunStatus.CompletedClean, "the weights are legal six-metric weights");
        WeightedUseCase.LastRequest!.Options.Score.Should().NotBeNull();
        WeightedUseCase.LastRequest.Options.Score!.Weighting.Weights.Should().HaveCount(6);
    }

    [Fact]
    public async Task An_instrument_option_flows_into_the_report_section()
    {
        // A supplied instrument replaces the UNKNOWN default and is trimmed.
        using var composition = new FakeWebComposition(useCase: new SucceedingUseCase());
        var options = Options() with { Score = true, Instrument = "  AUDUSD  " };
        var id = await composition.SubmitPendingAsync(options);

        await composition.Executor.ExecuteAsync(id);

        var result = await composition.ResultStore.FindAsync(id);
        result!.Validation.Instrument.Should().Be("AUDUSD");
    }

    [Fact]
    public async Task Establish_persists_a_benchmark_section_and_requires_a_store()
    {
        using var composition = new FakeWebComposition(
            useCase: new SucceedingUseCase(),
            benchmarkStore: new FakeBenchmarkStore());
        var options = Options() with { Score = true, Instrument = "AUDUSD", BenchmarkName = "audusd-d1" };
        var id = await composition.SubmitPendingAsync(options, WebRunOperation.EstablishBenchmark);

        await composition.Executor.ExecuteAsync(id);

        var record = await composition.RunStore.FindAsync(id);
        record!.Status.Should().Be(WebRunStatus.CompletedClean);

        var result = await composition.ResultStore.FindAsync(id);
        result!.Benchmark.Should().NotBeNull("establishment records its snapshot (FR-016)");
        result.Benchmark!.Name.Should().Be("audusd-d1");
        result.Comparison.Should().BeNull("an establish run carries no comparison");
    }

    [Fact]
    public async Task Establish_without_a_benchmark_store_fails_as_invalid_structure()
    {
        // The composition omits the store; the executor throws the explicit
        // InvalidOperationException, which the catch maps to a diagnostic.
        using var composition = new FakeWebComposition(useCase: new SucceedingUseCase());
        var options = Options() with { Score = true, Instrument = "AUDUSD", BenchmarkName = "audusd-d1" };
        var id = await composition.SubmitPendingAsync(options, WebRunOperation.EstablishBenchmark);

        await composition.Executor.ExecuteAsync(id);

        var record = await composition.RunStore.FindAsync(id);
        record!.Status.Should().Be(WebRunStatus.Failed);
        record.Diagnostic!.Code.Should().Be("INVALID_STRUCTURE");
    }

    [Fact]
    public async Task Compare_builds_a_comparison_section_through_the_loaded_benchmark()
    {
        var candles = new[] { Candle(Utc) };
        using var composition = new FakeWebComposition(
            useCase: new SucceedingUseCase(),
            benchmarkStore: PreloadedBenchmarkStore(),
            loadBenchmarkCandles: _ => candles,
            candidateCandles: candles);
        var options = Options() with { Score = true, Instrument = "AUDUSD", BenchmarkName = "audusd-d1" };
        var id = await composition.SubmitPendingAsync(options, WebRunOperation.Compare);

        await composition.Executor.ExecuteAsync(id);

        var record = await composition.RunStore.FindAsync(id);
        record!.Status.Should().Be(WebRunStatus.CompletedClean);

        var result = await composition.ResultStore.FindAsync(id);
        result!.Comparison.Should().NotBeNull("a compare run records its evidence (FR-017)");
        result.Comparison!.Comparison.Coverage.MatchedCount.Should().Be(1);
    }

    [Fact]
    public async Task Compare_with_tolerance_overrides_parses_them_into_the_configuration()
    {
        var candles = new[] { Candle(Utc) };
        using var composition = new FakeWebComposition(
            useCase: new SucceedingUseCase(),
            benchmarkStore: PreloadedBenchmarkStore(),
            loadBenchmarkCandles: _ => candles,
            candidateCandles: candles);
        var options = Options() with
        {
            Score = true,
            Instrument = "AUDUSD",
            BenchmarkName = "audusd-d1",
            ToleranceOverrides = "{\"Close\": {\"absolute\": 0.00005}}"
        };
        var id = await composition.SubmitPendingAsync(options, WebRunOperation.Compare);

        await composition.Executor.ExecuteAsync(id);

        var record = await composition.RunStore.FindAsync(id);
        record!.Status.Should().Be(WebRunStatus.CompletedClean);
        var result = await composition.ResultStore.FindAsync(id);
        result!.Comparison.Should().NotBeNull();
    }

    [Fact]
    public async Task Compare_without_a_benchmark_store_fails_as_invalid_structure()
    {
        // The composition omits the store; the executor throws the explicit
        // InvalidOperationException, which the catch maps to a diagnostic.
        using var composition = new FakeWebComposition(useCase: new SucceedingUseCase());
        var options = Options() with { Score = true, Instrument = "AUDUSD", BenchmarkName = "audusd-d1" };
        var id = await composition.SubmitPendingAsync(options, WebRunOperation.Compare);

        await composition.Executor.ExecuteAsync(id);

        var record = await composition.RunStore.FindAsync(id);
        record!.Status.Should().Be(WebRunStatus.Failed);
        record.Diagnostic!.Code.Should().Be("INVALID_STRUCTURE");
    }

    [Fact]
    public async Task Export_of_a_completed_run_whose_result_was_removed_is_not_available()
    {
        // A terminal-success record without its stored artifact is not
        // downloadable; export never fabricates a report (FR-014).
        using var composition = new FakeWebComposition(useCase: new SucceedingUseCase());
        var id = await composition.SubmitAsync(Options());

        composition.ResultStore.Clear();

        var result = await composition.Service.ExportAsync(
            id, ReportRepresentation.JsonV2, new MemoryStream());
        result.Should().BeOfType<WebExportResult.NotAvailable>()
            .Which.Reason.Should().Contain("result artifact");
    }

    [Fact]
    public async Task Compare_without_a_benchmark_candle_loader_fails_as_invalid_structure()
    {
        using var composition = new FakeWebComposition(
            useCase: new SucceedingUseCase(),
            benchmarkStore: PreloadedBenchmarkStore(),
            loadBenchmarkCandles: null);
        var options = Options() with { Score = true, Instrument = "AUDUSD", BenchmarkName = "audusd-d1" };
        var id = await composition.SubmitPendingAsync(options, WebRunOperation.Compare);

        await composition.Executor.ExecuteAsync(id);

        var record = await composition.RunStore.FindAsync(id);
        record!.Status.Should().Be(WebRunStatus.Failed);
        record.Diagnostic!.Code.Should().Be("INVALID_STRUCTURE");
    }

    private static FakeBenchmarkStore PreloadedBenchmarkStore()
    {
        var store = new FakeBenchmarkStore();
        store.Snapshots["audusd-d1"] = new BenchmarkSnapshot(
            "audusd-d1",
            Utc,
            Source(),
            Context(),
            new ScanCoverage(1, 1, 0),
            SixChecks(),
            ReportScore().Metrics,
            ReportScore().Dataset,
            ReportScore().Weighting,
            instrument: "AUDUSD");
        return store;
    }

    [Fact]
    public async Task A_succeeded_run_projects_every_view_member()
    {
        using var composition = new FakeWebComposition(useCase: new SucceedingUseCase());
        var options = Options() with { Score = true };
        var id = await composition.SubmitAsync(options);

        var retrieval = await composition.Service.GetResultAsync(id);
        var view = retrieval.Should().BeOfType<WebResultRetrieval.Ready>().Subject.View;

        // Every projected member of the typed view is reachable as data
        // (FR-013): identity, operation, status, source, sections, exports.
        view.Id.Should().Be(id);
        view.Operation.Should().Be(WebRunOperation.Validate);
        view.Status.Should().Be(WebRunStatus.CompletedClean);
        view.Source.FileName.Should().Be("clean.csv");
        view.Source.Sha256.Should().Be(new string('b', 64));
        view.Diagnostic.Should().BeNull();

        var validation = view.Validation!;
        validation.Context.Should().Be(Context());
        validation.Coverage.Should().NotBeNull();
        validation.Checks.Should().HaveCount(6);
        validation.Reconciliation.Should().NotBeNull();
        validation.Summary.IsClean.Should().BeTrue();
        validation.Findings.Should().NotBeNull();
        validation.Instrument.Should().Be("UNKNOWN", "no instrument was supplied");

        view.Scoring!.Score.Should().NotBeNull();
        view.Benchmark.Should().BeNull("a plain validation carries no benchmark section");
        view.Comparison.Should().BeNull("a plain validation carries no comparison section");
        view.AvailableExports.Should().NotBeEmpty();
    }

    [Fact]
    public async Task An_establish_run_projects_its_benchmark_section_members()
    {
        using var composition = new FakeWebComposition(
            useCase: new SucceedingUseCase(),
            benchmarkStore: new FakeBenchmarkStore());
        var options = Options() with { Score = true, Instrument = "AUDUSD", BenchmarkName = "audusd-d1" };
        var id = await composition.SubmitAsync(options, WebRunOperation.EstablishBenchmark);

        var retrieval = await composition.Service.GetResultAsync(id);
        var benchmark = retrieval.Should().BeOfType<WebResultRetrieval.Ready>().Subject.View.Benchmark!;

        // The benchmark section exposes the recorded snapshot members as
        // data (FR-016).
        benchmark.Name.Should().Be("audusd-d1");
        benchmark.Source.Should().NotBeNull();
        benchmark.Context.Should().Be(Context());
        benchmark.RecordedMetrics.Should().HaveCount(6);
        benchmark.RecordedDatasetScore.Should().NotBeNull();
        benchmark.Instrument.Should().Be("AUDUSD");
    }

    [Fact]
    public async Task A_succeeded_export_streams_through_the_result_callback()
    {
        // The persisted result carries the export delegate; exporting an
        // available representation of a terminal success returns Written
        // (FR-014).
        using var composition = new FakeWebComposition(useCase: new SucceedingUseCase());
        var id = await composition.SubmitAsync(Options());

        var result = await composition.Service.ExportAsync(
            id, ReportRepresentation.JsonV2, new MemoryStream());

        result.Should().BeOfType<WebExportResult.Written>()
            .Which.Representation.Should().Be(ReportRepresentation.JsonV2);
    }

    [Fact]
    public async Task Retry_of_an_unknown_id_is_rejected_with_a_diagnostic()
    {
        // The retry surface rejects an unknown run before any transition;
        // the store lookup returns null and the rejection explains why.
        using var composition = new FakeWebComposition();
        var unknown = WebRunId.Parse(new string('0', 64));

        var submission = await composition.Service.RetryAsync(unknown);

        var rejected = submission.Should().BeOfType<WebRunSubmission.Rejected>().Subject;
        rejected.Diagnostic.Code.Should().Be("INVALID_ARGUMENT");
        rejected.Diagnostic.Reason.Should().Contain("does not exist");
    }

    [Fact]
    public void A_record_exposes_its_retry_flag()
    {
        var failed = PendingRecord().ToRunning().ToFailed(Diagnostic(), Utc);
        failed.IsRetry.Should().BeFalse("only the retry transition carries the flag");

        var completed = PendingRecord().ToRunning().ToCompleted("result.json", isClean: true, terminalAtUtc: Utc);
        completed.IsRetry.Should().BeFalse();
    }

    [Fact]
    public void Executor_public_constructor_validates_its_ports()
    {
        // The public five-argument constructor delegates to the internal
        // composition constructor and validates every port.
        var act = () => new WebRunExecutor(null!, null!, null!, null!, null!);
        act.Should().Throw<ArgumentNullException>();
    }


    private static PriceCandle Candle(DateTimeOffset timestamp) => new(
        timestamp, 1m, 2m, 0.5m, 1.5m, 100, 1);

    private static WebRunRecord PendingRecord() => new(
        WebRunId.Derive(Source(), Options(), WebRunOperation.Validate),
        WebRunOperation.Validate,
        Source(),
        Options(),
        submittedAtUtc: Utc);

    // ------------------------------------------------------- composition

    private sealed class FixedClock : IApplicationClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class OpenCalendar : IMarketCalendar
    {
        public Domain.Calendars.MarketProfile Profile => Domain.Calendars.MarketProfile.Forex;
        public bool IsOpen(DateTimeOffset timestamp) => true;
    }

    private sealed class OpenCalendarFactory : IMarketCalendarFactory
    {
        public IMarketCalendar Create(LocalCalendarRequest request) => new OpenCalendar();
    }

    /// <summary>
    /// An Application-level composition with faked ports; the validation use
    /// case returns a real minimal report, so the executor's success, score,
    /// establish, and compare paths run end to end.
    /// </summary>
    private sealed class FakeWebComposition : IDisposable
    {
        private readonly Dictionary<WebRunId, WebRunRecord> _records = new();
        private readonly string _contentPath;

        public FixedClock Clock { get; } = new();

        public FakeRunStore RunStore { get; }

        public IUploadedDatasetStore UploadStore { get; }

        public IWebRunQueue Queue { get; }

        public FakeResultStore ResultStore { get; }

        public WebRunExecutor Executor { get; }

        public ValidationWebService Service { get; }

        public List<string> StoredNames { get; } = new();

        public FakeWebComposition(
            IUploadedDatasetStore? uploadStore = null,
            IDetailedValidationUseCase? useCase = null,
            IBenchmarkStore? benchmarkStore = null,
            Func<string, IReadOnlyList<PriceCandle>>? loadBenchmarkCandles = null,
            IReadOnlyList<PriceCandle>? candidateCandles = null)
        {
            // Establish copies the source bytes from the resolved path, so
            // the fake upload store must point at a real file.
            _contentPath = Path.Combine(Path.GetTempPath(), "fdc-web-gap-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(_contentPath, "a,b" + (char)10 + "1,2");

            RunStore = new FakeRunStore(_records);
            UploadStore = uploadStore ?? new FakeUploadStore(StoredNames, _contentPath, candidateCandles);
            ResultStore = new FakeResultStore();
            Executor = new WebRunExecutor(
                RunStore,
                UploadStore,
                useCase ?? new FailingUseCase(),
                new OpenCalendarFactory(),
                Clock,
                ResultStore,
                exportThrough: null,
                benchmarkStore,
                loadBenchmarkCandles);
            Queue = new InlineQueue(id => Executor.ExecuteAsync(id).AsValueTask());
            Service = new ValidationWebService(RunStore, UploadStore, Queue, ResultStore, Clock);
        }

        public async Task<WebRunId> SubmitAsync(WebRunOptions options, WebRunOperation operation = WebRunOperation.Validate)
        {
            var accepted = await Service.SubmitAsync(new WebRunRequest(
                operation, "clean.csv",
                new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2")), options));
            return ((WebRunSubmission.Accepted)accepted).Id;
        }

        /// <summary>Creates the record directly so the run stays Pending.</summary>
        public async Task<WebRunId> SubmitPendingAsync(
            WebRunOptions? options = null, WebRunOperation operation = WebRunOperation.Validate)
        {
            options ??= Options();
            var id = WebRunId.Derive(Source(), options, operation);
            await RunStore.TryCreateAsync(new WebRunRecord(
                id, operation, Source(), options, submittedAtUtc: Utc,
                benchmarkName: options.BenchmarkName));
            return id;
        }

        public void Dispose()
        {
            if (File.Exists(_contentPath))
            {
                File.Delete(_contentPath);
            }
        }
    }

    private sealed class FakeRunStore(Dictionary<WebRunId, WebRunRecord> records) : IWebRunStore
    {
        public ValueTask<WebRunRecord?> FindAsync(WebRunId id, CancellationToken ct = default) =>
            new(records.TryGetValue(id, out var record) ? record : null);

        public ValueTask<bool> TryCreateAsync(WebRunRecord record, CancellationToken ct = default)
        {
            if (records.ContainsKey(record.Id))
            {
                return new(false);
            }

            records[record.Id] = record;
            return new(true);
        }

        public ValueTask TransitionAsync(WebRunId id, WebRunStatus target, WebRunTransitionData data, CancellationToken ct = default)
        {
            var current = records[id];
            records[id] = current.Apply(target, data);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeUploadStore(List<string> storedNames, string contentPath, IReadOnlyList<PriceCandle>? candles = null) : IUploadedDatasetStore
    {
        public ValueTask<UploadedDataset> StoreAsync(string safeFileName, Stream content, CancellationToken ct = default)
        {
            storedNames.Add(safeFileName);
            return new(new UploadedDataset(
                new SourceIdentity(safeFileName, content.Length, new string('b', 64)),
                safeFileName + ".csv"));
        }

        public ValueTask<IPreparedCandleSource> OpenAsync(UploadedDataset dataset, CsvInputOptions options, CancellationToken ct = default) =>
            new(new CannedSource(candles ?? []));

        public string ResolveContentPath(UploadedDataset dataset) => contentPath;
    }

    private sealed class ThrowingUploadStore(Exception exception) : IUploadedDatasetStore
    {
        public ValueTask<UploadedDataset> StoreAsync(string safeFileName, Stream content, CancellationToken ct = default) =>
            new(new UploadedDataset(new SourceIdentity(safeFileName, 1, new string('d', 64)), safeFileName));

        public ValueTask<IPreparedCandleSource> OpenAsync(UploadedDataset dataset, CsvInputOptions options, CancellationToken ct = default) =>
            throw exception;

        public string ResolveContentPath(UploadedDataset dataset) => "uploads/" + dataset.Identity.Sha256;
    }

    private sealed class CancellingUploadStore : IUploadedDatasetStore
    {
        public ValueTask<UploadedDataset> StoreAsync(string safeFileName, Stream content, CancellationToken ct = default) =>
            new(new UploadedDataset(new SourceIdentity(safeFileName, 1, new string('d', 64)), safeFileName));

        public ValueTask<IPreparedCandleSource> OpenAsync(UploadedDataset dataset, CsvInputOptions options, CancellationToken ct = default) =>
            throw new OperationCanceledException();

        public string ResolveContentPath(UploadedDataset dataset) => "uploads/" + dataset.Identity.Sha256;
    }

    /// <summary>A canned source that replays the given candles.</summary>
    private sealed class CannedSource(IReadOnlyList<PriceCandle> candles) : IPreparedCandleSource
    {
        public ValueTask<PreparedCandleDataResult> PrepareAsync(CsvInputOptions options, CancellationToken cancellationToken = default) =>
            new(new PreparedCandleDataResult.Succeeded(
                new ReplayStub(),
                new SourceIdentity("clean.csv", 2, new string('b', 64)),
                new ResolvedCsvContext(',', true, TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+00:00"), null),
                new ScanCoverage(0, 0, 0)));

        public async IAsyncEnumerable<PriceCandle> ReadAllAsync()
        {
            await Task.Yield();
            foreach (var candle in candles)
            {
                yield return candle;
            }
        }
    }

    private sealed class ReplayStub : IReplayableCandleData
    {
        public IAsyncEnumerable<PriceCandle> ReplayAsync() => AsyncEmpty();

        private static async IAsyncEnumerable<PriceCandle> AsyncEmpty()
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class CancellingValidationUseCase : IDetailedValidationUseCase
    {
        public ValueTask<DetailedValidationOutcome> ExecuteAsync(DetailedValidationRequest request, CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException();
    }

    /// <summary>Completes with a clean report; the source diagnostics carry none.</summary>
    private sealed class SucceedingUseCase : IDetailedValidationUseCase
    {
        public ValueTask<DetailedValidationOutcome> ExecuteAsync(DetailedValidationRequest request, CancellationToken cancellationToken = default)
        {
            var scored = request.Options.Score is not null;
            return new(new DetailedValidationOutcome.Succeeded(CleanReport(withScore: scored)));
        }
    }

    private sealed class WeightedUseCase : IDetailedValidationUseCase
    {
        public static DetailedValidationRequest? LastRequest { get; private set; }

        public ValueTask<DetailedValidationOutcome> ExecuteAsync(DetailedValidationRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return new(new DetailedValidationOutcome.Succeeded(CleanReport(withScore: true)));
        }
    }

    /// <summary>Fails with a diagnostic that carries no source identity.</summary>
    private sealed class FailingUseCase : IDetailedValidationUseCase
    {
        public ValueTask<DetailedValidationOutcome> ExecuteAsync(DetailedValidationRequest request, CancellationToken cancellationToken = default) =>
            new(new DetailedValidationOutcome.Failed(new FatalDiagnostic(
                "VALIDATION_INCOMPLETE", "Validation did not run.", "Re-submit the dataset.")));
    }

    private sealed class FakeBenchmarkStore : IBenchmarkStore
    {
        public Dictionary<string, BenchmarkSnapshot> Snapshots { get; } = new();

        public ValueTask SaveAsync(BenchmarkSnapshot snapshot, string sourceFilePath, CancellationToken ct = default)
        {
            Snapshots[snapshot.Name] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<BenchmarkSnapshot> LoadAsync(string name, CancellationToken ct = default)
        {
            if (!Snapshots.TryGetValue(name, out var snapshot))
            {
                throw new KeyNotFoundException($"No benchmark named '{name}'.");
            }

            return new(snapshot);
        }

        public ValueTask<bool> DeleteAsync(string name, CancellationToken ct = default) => new(false);

        public ValueTask<bool> ExistsAsync(string name, CancellationToken ct = default) =>
            new(Snapshots.ContainsKey(name));

        public ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken ct = default) =>
            new(Array.Empty<string>());
    }

    private sealed class FakeResultStore : IWebResultStore
    {
        private readonly Dictionary<WebRunId, WebRunResult> _results = new();

        public Task SaveAsync(WebRunId id, WebRunResult result, CancellationToken ct = default)
        {
            _results[id] = result;
            return Task.CompletedTask;
        }

        public Task<WebRunResult?> FindAsync(WebRunId id, CancellationToken ct = default) =>
            Task.FromResult(_results.TryGetValue(id, out var result) ? result : null);

        public void Clear() => _results.Clear();
    }

    private sealed class InlineQueue(Func<WebRunId, ValueTask> executor) : IWebRunQueue
    {
        public ValueTask EnqueueAsync(WebRunId id, CancellationToken ct = default) => executor(id);
    }

    private sealed class EmptyCatalog : ICompletedFindingCatalog
    {
        private static readonly CategoryStatistics Zero = new(0, 0);

        public FindingCatalogStatistics Statistics => new(Zero, Zero, Zero, Zero, Zero, Zero);

        public IAsyncEnumerable<IDetailedFindingCursor> ReadCanonicalAsync(CancellationToken ct = default)
        {
            return AsyncEmpty();
        }

        private static async IAsyncEnumerable<IDetailedFindingCursor> AsyncEmpty()
        {
            await Task.Yield();
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal static class ValueTaskExtensions
{
    public static ValueTask AsValueTask(this Task task) => new(task);
}
