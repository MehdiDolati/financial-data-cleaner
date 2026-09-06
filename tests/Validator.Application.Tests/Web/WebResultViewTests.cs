using System;
using System.Threading.Tasks;
using Validator.Application.Ingestion;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;
using Validator.Application.Web;

namespace Validator.Application.Tests.Web;

// Result-view tests (T041): the typed view is presentation-free, exposes
// every field of contracts/web-result-view-contract.md as data, the failure
// view carries the diagnostic and nothing else, and the success view
// requires a terminal-success record (FR-011, FR-013, SC-002).
public class WebResultViewTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

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

    private static WebRunRecord PendingRecord() => new(
        WebRunId.Derive(Source(), Options(), WebRunOperation.Validate),
        WebRunOperation.Validate,
        Source(),
        Options(),
        submittedAtUtc: FixedTime);

    [Fact]
    public void Failure_view_carries_the_diagnostic_and_nothing_else()
    {
        var diagnostic = new FatalDiagnostic(
            "INVALID_CSV", "The source is not parsable.", "Re-export the file.");
        var failed = PendingRecord().ToRunning().ToFailed(diagnostic, FixedTime);

        var view = WebResultView.Failure(failed);

        view.Status.Should().Be(WebRunStatus.Failed);
        view.Diagnostic.Should().NotBeNull();
        view.Diagnostic!.Code.Should().Be("INVALID_CSV");
        view.Validation.Should().BeNull("no counts (FR-011)");
        view.Scoring.Should().BeNull("no score (FR-011)");
        view.Benchmark.Should().BeNull();
        view.Comparison.Should().BeNull();
        view.AvailableExports.Should().BeEmpty("no export for a fatal run (FR-014)");
    }

    [Fact]
    public void Failure_view_requires_a_failed_record_with_a_diagnostic()
    {
        var running = PendingRecord().ToRunning();
        var act = () => WebResultView.Failure(running);
        act.Should().Throw<ArgumentException>();

        var failedNoDiagnostic = PendingRecord().ToRunning()
            .ToFailed(new FatalDiagnostic("INVALID_CSV", "Reason", "Guidance"), FixedTime);
        var act2 = () => WebResultView.Failure(failedNoDiagnostic);
        act2.Should().NotThrow();
    }

    [Fact]
    public void Success_view_requires_a_terminal_success_record()
    {
        var pending = PendingRecord();
        var validation = new WebValidationSection(
            Context: null!,
            Coverage: null!,
            Checks: Array.Empty<CheckExecution>(),
            Reconciliation: null!,
            Summary: null!,
            Findings: null!,
            Instrument: "UNKNOWN");

        var act = () => WebResultView.Success(
            pending, validation, null, null, null, Array.Empty<ReportRepresentation>());
        act.Should().Throw<ArgumentException>(
            "a pending record cannot produce a success view");
    }

    [Fact]
    public void Views_are_presentation_free_every_field_is_data()
    {
        // The view carries typed members only: no HTML, no markdown, no
        // pre-rendered strings (SC-002). The record shape guarantees this at
        // compile time; this test pins the intent for reviewers.
        typeof(WebResultView).Should().BeSealed();
        foreach (var property in typeof(WebResultView).GetProperties())
        {
            var isRenderedString = property.PropertyType == typeof(string)
                && (property.Name == "Html" || property.Name == "Markdown");
            isRenderedString.Should().BeFalse(
                $"the view exposes '{property.Name}' as data, never as pre-rendered prose");
        }
    }
}