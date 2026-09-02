using System;
using System.IO;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Web;
using Validator.Infrastructure.Web;

namespace Validator.Infrastructure.Tests.Web;

// File-backed run-store tests: durable persistence under a configurable
// root, create-if-absent with the deterministic id as the duplicate guard,
// guarded transitions that reject rather than coerce, atomic writes, and a
// consistently valid observed status during concurrent transitions.
public class FileWebRunStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fdc-web-run-store-" + Guid.NewGuid().ToString("N"));

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

    private WebRunRecord NewPendingRecord() => new(
        WebRunId.Derive(Source(), Options()),
        WebRunOperation.Validate,
        Source(),
        Options(),
        SubmittedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private FileWebRunStore NewStore() => new(_root);

    [Fact]
    public async Task Create_persists_a_retrievable_pending_record()
    {
        var store = NewStore();
        var record = NewPendingRecord();

        (await store.TryCreateAsync(record)).Should().BeTrue();
        _root.Should().EndWith(Path.DirectorySeparatorChar.ToString()).And.NotBe(Path.GetTempPath());

        var loaded = await store.FindAsync(record.Id);
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(record.Id);
        loaded.Status.Should().Be(WebRunStatus.Pending);
        loaded.Operation.Should().Be(WebRunOperation.Validate);
        loaded.Source.FileName.Should().Be("dataset.csv");
        loaded.SubmittedAtUtc.Should().Be(record.SubmittedAtUtc);
    }

    [Fact]
    public async Task TryCreate_returns_false_when_the_deterministic_id_already_exists()
    {
        var store = NewStore();
        var record = NewPendingRecord();

        (await store.TryCreateAsync(record)).Should().BeTrue();
        (await store.TryCreateAsync(record)).Should().BeFalse();
        (await store.FindAsync(record.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Find_of_an_unknown_id_returns_null()
    {
        var store = NewStore();

        var loaded = await store.FindAsync(WebRunId.Parse(new string('f', 64)));

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Transition_applies_valid_transitions_and_persists_them()
    {
        var store = NewStore();
        var record = NewPendingRecord();
        await store.TryCreateAsync(record);

        await store.TransitionAsync(record.Id, WebRunStatus.Running, WebRunTransitionData.ForRunning());

        var running = await store.FindAsync(record.Id);
        running!.Status.Should().Be(WebRunStatus.Running);

        var terminalTime = new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero);
        await store.TransitionAsync(
            record.Id,
            WebRunStatus.CompletedWithFindings,
            WebRunTransitionData.ForSuccess("runs/" + record.Id.Value + ".json"),
            terminalTime);

        var completed = await store.FindAsync(record.Id);
        completed!.Status.Should().Be(WebRunStatus.CompletedWithFindings);
        completed.ResultReference.Should().Be("runs/" + record.Id.Value + ".json");
        completed.TerminalAtUtc.Should().Be(terminalTime);
    }

    [Fact]
    public async Task Transition_rejects_invalid_transitions_instead_of_coercing()
    {
        var store = NewStore();
        var record = NewPendingRecord();
        await store.TryCreateAsync(record);

        // Pending -> CompletedClean is forbidden: a run that never executed
        // cannot be clean (SC-003).
        var act = () => store.TransitionAsync(
            record.Id,
            WebRunStatus.CompletedClean,
            WebRunTransitionData.ForSuccess("result.json"));
        await act.Should().ThrowAsync<InvalidOperationException>();

        // The stored record is untouched by the rejected transition.
        var untouched = await store.FindAsync(record.Id);
        untouched!.Status.Should().Be(WebRunStatus.Pending);
        untouched.ResultReference.Should().BeNull();
    }

    [Fact]
    public async Task Transition_rejects_completed_immutability_violations()
    {
        var store = NewStore();
        var record = NewPendingRecord();
        await store.TryCreateAsync(record);
        await store.TransitionAsync(record.Id, WebRunStatus.Running, WebRunTransitionData.ForRunning());
        await store.TransitionAsync(
            record.Id,
            WebRunStatus.CompletedClean,
            WebRunTransitionData.ForSuccess("result.json"));

        var act = () => store.TransitionAsync(record.Id, WebRunStatus.Running, WebRunTransitionData.ForRunning());
        await act.Should().ThrowAsync<InvalidOperationException>();

        var act2 = () => store.TransitionAsync(
            record.Id,
            WebRunStatus.Failed,
            WebRunTransitionData.ForFailure(new FatalDiagnostic(
                "VALIDATION_INCOMPLETE",
                "A late failure cannot replace a completed report.",
                "This is a programming fault; report it.")));
        await act2.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Retry_transitions_failed_back_to_pending()
    {
        var store = NewStore();
        var record = NewPendingRecord();
        await store.TryCreateAsync(record);

        await store.TransitionAsync(
            record.Id,
            WebRunStatus.Failed,
            WebRunTransitionData.ForFailure(new FatalDiagnostic(
                "INVALID_CSV",
                "The source is not parsable as delimited text.",
                "Re-export the dataset as valid CSV.")));

        await store.TransitionAsync(record.Id, WebRunStatus.Pending, WebRunTransitionData.ForRetry());

        var pending = await store.FindAsync(record.Id);
        pending!.Status.Should().Be(WebRunStatus.Pending);
        pending.Diagnostic.Should().BeNull();
        pending.ResultReference.Should().BeNull();
        pending.TerminalAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Writes_are_atomic_no_partial_file_is_observable()
    {
        var store = NewStore();
        var record = NewPendingRecord();

        await store.TryCreateAsync(record);

        // Exactly one record file exists; no temporary artifacts remain.
        var runDirectory = Path.Combine(_root, "runs");
        Directory.Exists(runDirectory).Should().BeTrue();
        Directory.GetFiles(runDirectory, "*.tmp").Should().BeEmpty();
        Directory.GetFiles(runDirectory, "*.json").Should().ContainSingle();
    }

    [Fact]
    public async Task A_new_store_instance_reads_records_written_by_a_previous_one()
    {
        var record = NewPendingRecord();
        var first = NewStore();
        await first.TryCreateAsync(record);

        // Simulates a process restart: durability across instances (SC-007).
        var second = new FileWebRunStore(_root);
        var loaded = await second.FindAsync(record.Id);

        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(WebRunStatus.Pending);
    }

    [Fact]
    public async Task Concurrent_creates_of_the_same_id_yield_exactly_one_success()
    {
        var store = NewStore();
        var record = NewPendingRecord();

        var first = Task.Run(() => store.TryCreateAsync(record));
        var second = Task.Run(() => store.TryCreateAsync(record));
        var results = await Task.WhenAll(first, second);

        results.Should().ContainSingle(success => success);
    }

    [Fact]
    public async Task Observed_status_is_always_a_valid_state_during_concurrent_transitions()
    {
        var store = NewStore();
        var record = NewPendingRecord();
        await store.TryCreateAsync(record);

        var transition = Task.Run(async () =>
        {
            await store.TransitionAsync(record.Id, WebRunStatus.Running, WebRunTransitionData.ForRunning());
            await store.TransitionAsync(
                record.Id,
                WebRunStatus.CompletedClean,
                WebRunTransitionData.ForSuccess("result.json"));
        });

        while (!transition.IsCompleted)
        {
            var observed = await store.FindAsync(record.Id);
            observed.Should().NotBeNull();
            Enum.IsDefined(observed!.Status).Should().BeTrue(
                "the observed status must always be one of the valid states");
        }

        await transition;
        var final = await store.FindAsync(record.Id);
        final!.Status.Should().Be(WebRunStatus.CompletedClean);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}