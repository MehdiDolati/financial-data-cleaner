using System;
using Validator.Application.Ingestion;
using Validator.Application.Web;
using Validator.Application.Reporting;

namespace Validator.Application.Tests.Web;

// Audit aggregate tests. The record-level invariants — Diagnostic non-null
// exactly when Failed; ResultReference only for terminal success; timestamps
// from IApplicationClock; completed states immutable — are enforced at
// construction and transition (FR-026, FR-011, data-model.md).
public class WebRunRecordTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static SourceIdentity Source() => new("dataset.csv", 100, new string('a', 64));

    private static WebRunOptions Options() => new(
        Timeframe: null,
        Market: Domain.Calendars.MarketProfile.Forex,
        CalendarReference: null,
        Csv: new CsvInputOptions(),
        ReportVersion: 2,
        Score: false,
        ScoreWeights: null,
        Instrument: null,
        BenchmarkName: null,
        ToleranceOverrides: null);

    private static FatalDiagnostic Diagnostic() => new(
        "INVALID_ARGUMENT",
        "The supplied options could not be applied to this run.",
        "Correct the reported option and resubmit.");

    private static WebRunRecord PendingRecord() => new(
        WebRunId.Derive(Source(), Options()),
        WebRunOperation.Validate,
        Source(),
        Options(),
        SubmittedAtUtc: FixedTime);

    [Fact]
    public void Construction_of_a_pending_record_succeeds_with_defaults()
    {
        var record = PendingRecord();

        record.Status.Should().Be(WebRunStatus.Pending);
        record.Diagnostic.Should().BeNull();
        record.ResultReference.Should().BeNull();
        record.TerminalAtUtc.Should().BeNull();
        record.SubmittedBy.Should().BeNull();
        record.BenchmarkName.Should().BeNull();
    }

    [Fact]
    public void Diagnostic_is_non_null_exactly_when_status_is_failed()
    {
        // Failed => Diagnostic non-null is enforced by the transition data
        // carrier; non-Failed states must never carry one.
        var failed = PendingRecord().ToFailed(Diagnostic(), FixedTime);

        failed.Status.Should().Be(WebRunStatus.Failed);
        failed.Diagnostic.Should().NotBeNull();

        FluentActions.Invoking(() => PendingRecord().ToFailed(null!, FixedTime))
            .Should().Throw<ArgumentException>();

        FluentActions.Invoking(() => PendingRecord().ToRunning())
            .Should().NotThrow();
        var running = PendingRecord().ToRunning();
        running.Diagnostic.Should().BeNull();
    }

    [Fact]
    public void Transition_data_rejects_a_report_reference_and_no_diagnostic()
    {
        // A fatal transition carries the diagnostic and nothing else.
        FluentActions.Invoking(() => new WebRunTransitionData(ResultReference: null, FatalDiagnostic: null))
            .Should().Throw<ArgumentException>();

        // A success transition carries the reference and nothing else.
        FluentActions.Invoking(() => new WebRunTransitionData(ResultReference: "result.json"))
            .Should().NotThrow();

        // Both together is a partial-success representation — forbidden.
        FluentActions.Invoking(() =>
                new WebRunTransitionData(ResultReference: "result.json", FatalDiagnostic: Diagnostic()))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ResultReference_is_set_only_for_terminal_success()
    {
        var completed = PendingRecord().ToRunning()
            .ToCompleted("result.json", isClean: false, FixedTime);

        completed.Status.Should().Be(WebRunStatus.CompletedWithFindings);
        completed.ResultReference.Should().Be("result.json");
        completed.TerminalAtUtc.Should().Be(FixedTime);
        completed.Diagnostic.Should().BeNull();

        var clean = PendingRecord().ToRunning()
            .ToCompleted("result.json", isClean: true, FixedTime);
        clean.Status.Should().Be(WebRunStatus.CompletedClean);
    }

    [Fact]
    public void Completed_states_are_immutable()
    {
        var completed = PendingRecord().ToRunning().ToCompleted("result.json", isClean: true, FixedTime);

        FluentActions.Invoking(() => completed.ToRunning())
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => completed.ToFailed(Diagnostic(), FixedTime))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => completed.ToCompleted("other.json", isClean: false, FixedTime))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Retry_transitions_failed_back_to_pending_and_clears_terminal_data()
    {
        var failed = PendingRecord().ToFailed(Diagnostic(), FixedTime);

        var retried = failed.ToPendingRetry();

        retried.Status.Should().Be(WebRunStatus.Pending);
        retried.Diagnostic.Should().BeNull();
        retried.ResultReference.Should().BeNull();
        retried.TerminalAtUtc.Should().BeNull();
        retried.Id.Should().Be(failed.Id);
        retried.SubmittedAtUtc.Should().Be(failed.SubmittedAtUtc);
    }

    [Fact]
    public void Non_failed_states_reject_retry()
    {
        FluentActions.Invoking(() => PendingRecord().ToPendingRetry())
            .Should().Throw<InvalidOperationException>();

        var running = PendingRecord().ToRunning();
        FluentActions.Invoking(() => running.ToPendingRetry())
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Pending_record_cannot_complete_without_running_first()
    {
        FluentActions.Invoking(() => PendingRecord().ToCompleted("result.json", isClean: true, FixedTime))
            .Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(WebRunOperation.EstablishBenchmark)]
    [InlineData(WebRunOperation.Compare)]
    public void Benchmark_operations_require_a_benchmark_name(WebRunOperation operation)
    {
        FluentActions.Invoking(() => new WebRunRecord(
                WebRunId.Derive(Source(), Options() with { BenchmarkName = "audusd-d1" }),
                operation,
                Source(),
                Options() with { BenchmarkName = "audusd-d1" },
                SubmittedAtUtc: FixedTime))
            .Should().NotThrow();

        FluentActions.Invoking(() => new WebRunRecord(
                WebRunId.Derive(Source(), Options()),
                operation,
                Source(),
                Options(),
                SubmittedAtUtc: FixedTime))
            .Should().Throw<ArgumentException>()
            .WithMessage("*BenchmarkName*");
    }

    [Fact]
    public void Validate_operation_rejects_a_benchmark_name()
    {
        FluentActions.Invoking(() => new WebRunRecord(
                WebRunId.Derive(Source(), Options() with { BenchmarkName = "audusd-d1" }),
                WebRunOperation.Validate,
                Source(),
                Options() with { BenchmarkName = "audusd-d1" },
                SubmittedAtUtc: FixedTime))
            .Should().Throw<ArgumentException>()
            .WithMessage("*BenchmarkName*");
    }

    [Fact]
    public void SubmittedAt_must_be_utc()
    {
        var localTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(3));

        FluentActions.Invoking(() => new WebRunRecord(
                WebRunId.Derive(Source(), Options()),
                WebRunOperation.Validate,
                Source(),
                Options(),
                SubmittedAtUtc: localTime))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SubmittedBy_is_opaque_and_optional()
    {
        var record = new WebRunRecord(
            WebRunId.Derive(Source(), Options()),
            WebRunOperation.Validate,
            Source(),
            Options(),
            SubmittedAtUtc: FixedTime,
            SubmittedBy: "correlation-42");

        record.SubmittedBy.Should().Be("correlation-42");
    }

    [Fact]
    public void Resolved_options_are_captured_verbatim()
    {
        var options = Options() with { Timeframe = "H1" };
        var record = new WebRunRecord(
            WebRunId.Derive(Source(), options),
            WebRunOperation.Validate,
            Source(),
            options,
            SubmittedAtUtc: FixedTime);

        record.ResolvedOptions.Timeframe.Should().Be("H1");
        record.ResolvedOptions.Should().Be(options);
    }
}