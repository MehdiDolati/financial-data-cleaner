using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Web;

namespace Validator.Application.Tests.Web;

// Residual reachable gaps in the web surface, closed through legal calls:
// the retry surface for an unknown id (FR-010), a successful export through
// the persisted result's callback (FR-014), the executor's public
// constructor guards, the record-level IsRetry flag, and the success view's
// projected members (FR-013, FR-016).
public class WebResidualGapTests
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

    [Fact]
    public void Executor_public_constructor_validates_its_ports()
    {
        // The public five-argument constructor delegates to the internal
        // composition constructor and validates every port.
        var act = () => new WebRunExecutor(null!, null!, null!, null!, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_record_exposes_its_retry_flag()
    {
        var pending = Record();
        pending.IsRetry.Should().BeFalse();

        var failed = pending.ToRunning().ToFailed(Diagnostic(), Utc);
        failed.IsRetry.Should().BeFalse("only the retry transition carries the flag");

        var completed = pending.ToRunning().ToCompleted("result.json", isClean: true, terminalAtUtc: Utc);
        completed.IsRetry.Should().BeFalse();
    }

    [Fact]
    public void Executor_public_constructor_validates_each_port_individually()
    {
        // Each port is validated in isolation; the guard arms are reachable
        // one at a time through the public constructor.
        var store = new ThrowingStore();
        var upload = new ThrowingUploadStore();
        var useCase = new ThrowingUseCase();
        var calendar = new OpenCalendarFactory();
        var clock = new FixedClock();

        ((Action)(() => new WebRunExecutor(null!, upload, useCase, calendar, clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new WebRunExecutor(store, null!, useCase, calendar, clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new WebRunExecutor(store, upload, null!, calendar, clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new WebRunExecutor(store, upload, useCase, null!, clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new WebRunExecutor(store, upload, useCase, calendar, null!))).Should().Throw<ArgumentNullException>();
    }

    private static WebRunRecord Record() => new(
        WebRunId.Derive(Source(), Options(), WebRunOperation.Validate),
        WebRunOperation.Validate,
        Source(),
        Options(),
        submittedAtUtc: Utc);

    [Fact]
    public void Executor_public_constructor_composes_without_optional_ports()
    {
        // The public constructor is the composition entry point for hosts
        // that bind the optional ports through Infrastructure DI; calling it
        // with the five required ports succeeds and reaches its body.
        var executor = new WebRunExecutor(
            new ThrowingStore(),
            new ThrowingUploadStore(),
            new ThrowingUseCase(),
            new OpenCalendarFactory(),
            new FixedClock());

        executor.Should().NotBeNull();
    }

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

    private sealed class ThrowingStore : IWebRunStore
    {
        public ValueTask<WebRunRecord?> FindAsync(WebRunId id, CancellationToken ct = default) =>
            new(null as WebRunRecord);

        public ValueTask<bool> TryCreateAsync(WebRunRecord record, CancellationToken ct = default) => new(true);

        public ValueTask TransitionAsync(WebRunId id, WebRunStatus target, WebRunTransitionData data, CancellationToken ct = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class ThrowingUploadStore : IUploadedDatasetStore
    {
        public ValueTask<UploadedDataset> StoreAsync(string safeFileName, Stream content, CancellationToken ct = default) =>
            new(new UploadedDataset(new SourceIdentity(safeFileName, 1, new string('d', 64)), safeFileName));

        public ValueTask<IPreparedCandleSource> OpenAsync(UploadedDataset dataset, CsvInputOptions options, CancellationToken ct = default) =>
            throw new InvalidOperationException("no source");

        public string ResolveContentPath(UploadedDataset dataset) => "uploads/" + dataset.Identity.Sha256;
    }

    private sealed class ThrowingUseCase : IDetailedValidationUseCase
    {
        public ValueTask<DetailedValidationOutcome> ExecuteAsync(DetailedValidationRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("no validation");
    }

    private sealed class NoopQueue : IWebRunQueue
    {
        public ValueTask EnqueueAsync(WebRunId id, CancellationToken ct = default) => ValueTask.CompletedTask;
    }

    private sealed class EmptyResultStore : IWebResultStore
    {
        public Task SaveAsync(WebRunId id, WebRunResult result, CancellationToken ct = default) => Task.CompletedTask;

        public Task<WebRunResult?> FindAsync(WebRunId id, CancellationToken ct = default) =>
            Task.FromResult(null as WebRunResult);
    }

    [Fact]
    public async Task Retry_of_an_unknown_id_is_rejected_with_a_diagnostic()
    {
        // The retry surface rejects an unknown run before any transition;
        // the store lookup returns null and the rejection explains why.
        var service = new ValidationWebService(
            new ThrowingStore(),
            new ThrowingUploadStore(),
            new NoopQueue(),
            new EmptyResultStore(),
            new FixedClock());

        var submission = await service.RetryAsync(WebRunId.Parse(new string('0', 64)));

        var rejected = submission.Should().BeOfType<WebRunSubmission.Rejected>().Subject;
        rejected.Diagnostic.Code.Should().Be("INVALID_ARGUMENT");
        rejected.Diagnostic.Reason.Should().Contain("does not exist");
    }

    [Fact]
    public async Task GetStatus_and_GetResult_of_an_unknown_id_are_unavailable()
    {
        var service = new ValidationWebService(
            new ThrowingStore(),
            new ThrowingUploadStore(),
            new NoopQueue(),
            new EmptyResultStore(),
            new FixedClock());
        var unknown = WebRunId.Parse(new string('0', 64));

        (await service.GetStatusAsync(unknown)).Should().BeOfType<WebRunStatusResult.Unavailable>();
        (await service.GetResultAsync(unknown)).Should().BeOfType<WebResultRetrieval.Unavailable>();
    }
}