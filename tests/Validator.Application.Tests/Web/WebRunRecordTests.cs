using System;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Web;

namespace Validator.Application.Tests.Web;

// Audit aggregate tests. The record-level invariants - Diagnostic non-null
// exactly when Failed; ResultReference only for terminal success; timestamps
// from IApplicationClock; completed states immutable - are enforced at
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

    private static WebRunId NewId(WebRunOptions? options = null) =>
        WebRunId.Derive(Source(), options ?? Options(), WebRunOperation.Validate);

    private static WebRunRecord PendingRecord() => new(
        NewId(),
        WebRunOperation.Validate,
        Source(),
        Options(),
        submittedAtUtc: FixedTime);

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
        var failed = PendingRecord().ToRunning().ToFailed(Diagnostic(), FixedTime);

        failed.Status.Should().Be(WebRunStatus.Failed);
        failed.Diagnostic.Should().NotBeNull();

        var act = () => PendingRecord().ToFailed(null!, FixedTime);
        act.Should().Throw<ArgumentException>();

        var running = PendingRecord().ToRunning();
        running.Diagnostic.Should().BeNull();
    }

    [Fact]
    public void Transition_payloads_carry_exactly_one_terminal_fact()
    {
        // A success transition carries the reference and nothing else.
        var success = WebRunTransitionData.ForSuccess("result.json", FixedTime);
        success.ResultReference.Should().Be("result.json");
        success.FatalDiagnostic.Should().BeNull();

        // A failure transition carries the diagnostic and nothing else.
        var failure = WebRunTransitionData.ForFailure(Diagnostic(), FixedTime);
        failure.FatalDiagnostic.Should().NotBeNull();
        failure.ResultReference.Should().BeNull();

        // A running transition and a retry carry no terminal payload at all.
        WebRunTransitionData.ForRunning().ResultReference.Should().BeNull();
        WebRunTransitionData.ForRunning().FatalDiagnostic.Should().BeNull();
        WebRunTransitionData.ForRetry().ResultReference.Should().BeNull();
        WebRunTransitionData.ForRetry().FatalDiagnostic.Should().BeNull();
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

        var act = () => completed.ToRunning();
        act.Should().Throw<InvalidOperationException>();

        var act2 = () => completed.ToFailed(Diagnostic(), FixedTime);
        act2.Should().Throw<InvalidOperationException>();

        var act3 = () => completed.ToCompleted("other.json", isClean: false, FixedTime);
        act3.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Retry_transitions_failed_back_to_pending_and_clears_terminal_data()
    {
        var failed = PendingRecord().ToFailed(Diagnostic(), FixedTime) is { } direct ? direct : PendingRecord().ToRunning().ToFailed(Diagnostic(), FixedTime);

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
        var act = () => PendingRecord().ToPendingRetry();
        act.Should().Throw<InvalidOperationException>();

        var running = PendingRecord().ToRunning();
        var act2 = () => running.ToPendingRetry();
        act2.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Pending_record_cannot_complete_without_running_first()
    {
        var act = () => PendingRecord().ToCompleted("result.json", isClean: true, FixedTime);
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(WebRunOperation.EstablishBenchmark)]
    [InlineData(WebRunOperation.Compare)]
    public void Benchmark_operations_require_a_benchmark_name(WebRunOperation operation)
    {
        var options = Options() with { BenchmarkName = "audusd-d1" };

        var act = () => new WebRunRecord(
            WebRunId.Derive(Source(), options, operation),
            operation,
            Source(),
            options,
            submittedAtUtc: FixedTime,
            benchmarkName: "audusd-d1");
        act.Should().NotThrow();

        var act2 = () => new WebRunRecord(
            NewId(),
            operation,
            Source(),
            Options(),
            submittedAtUtc: FixedTime);
        act2.Should().Throw<ArgumentException>()
            .WithMessage("*benchmark name*");
    }

    [Fact]
    public void Validate_operation_rejects_a_benchmark_name()
    {
        var act = () => new WebRunRecord(
            NewId(),
            WebRunOperation.Validate,
            Source(),
            Options(),
            submittedAtUtc: FixedTime,
            benchmarkName: "audusd-d1");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*benchmark name*");
    }

    [Fact]
    public void SubmittedAt_must_be_utc()
    {
        var localTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(3));

        var act = () => new WebRunRecord(
            NewId(),
            WebRunOperation.Validate,
            Source(),
            Options(),
            submittedAtUtc: localTime);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SubmittedBy_is_opaque_and_optional()
    {
        var record = new WebRunRecord(
            NewId(),
            WebRunOperation.Validate,
            Source(),
            Options(),
            submittedAtUtc: FixedTime,
            submittedBy: "correlation-42");

        record.SubmittedBy.Should().Be("correlation-42");
    }

    [Fact]
    public void Resolved_options_are_captured_verbatim()
    {
        var options = Options() with { Timeframe = "H1" };
        var record = new WebRunRecord(
            WebRunId.Derive(Source(), options, WebRunOperation.Validate),
            WebRunOperation.Validate,
            Source(),
            options,
            submittedAtUtc: FixedTime);

        record.ResolvedOptions.Timeframe.Should().Be("H1");
        record.ResolvedOptions.Should().Be(options);
    }
}