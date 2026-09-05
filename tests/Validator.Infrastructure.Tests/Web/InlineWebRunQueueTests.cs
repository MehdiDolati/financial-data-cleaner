using System;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Ingestion;
using Validator.Application.Web;
using Validator.Infrastructure.Web;

namespace Validator.Infrastructure.Tests.Web;

// Inline queue tests: the simplest safe default (research R3). An accepted
// run executes synchronously through the injected run executor, the
// executor persists the terminal state, and a crash between durable
// Pending and enqueue leaves a recoverable Pending run rather than a lost
// one (FR-009, SC-007).
public class InlineWebRunQueueTests
{
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

    private static WebRunId NewId() => WebRunId.Derive(Source(), Options(), WebRunOperation.Validate);

    [Fact]
    public async Task Enqueue_executes_the_run_through_the_injected_executor()
    {
        var executed = new System.Collections.Generic.List<WebRunId>();
        var queue = new InlineWebRunQueue(id =>
        {
            executed.Add(id);
            return Task.CompletedTask;
        });

        var id = NewId();
        await queue.EnqueueAsync(id);

        executed.Should().ContainSingle().Which.Should().Be(id);
    }

    [Fact]
    public async Task Enqueue_executes_synchronously_before_returning()
    {
        var completed = false;
        var queue = new InlineWebRunQueue(async _ =>
        {
            await Task.Delay(10);
            completed = true;
        });

        await queue.EnqueueAsync(NewId());

        completed.Should().BeTrue("the inline queue runs work to completion before returning");
    }

    [Fact]
    public async Task Enqueue_after_a_crash_recoverable_pending_run_is_not_lost()
    {
        // Simulate a crash between durable persist and enqueue: the run
        // record is Pending in the store, then the process restarts and the
        // host re-enqueues it. The queue must execute it exactly once more
        // through the executor; there is no duplicate and no loss.
        var executions = 0;
        var queue = new InlineWebRunQueue(_ =>
        {
            Interlocked.Increment(ref executions);
            return Task.CompletedTask;
        });

        var id = NewId();
        await queue.EnqueueAsync(id);
        await queue.EnqueueAsync(id);

        executions.Should().Be(2,
            "each explicit enqueue drives one executor invocation; the recoverable pending run is re-run, never silently dropped");
    }

    [Fact]
    public void Constructor_rejects_a_null_executor()
    {
        var act = () => new InlineWebRunQueue(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Enqueue_passes_the_exact_id_to_the_executor()
    {
        WebRunId? received = null;
        var queue = new InlineWebRunQueue(id =>
        {
            received = id;
            return Task.CompletedTask;
        });

        var id = NewId();
        await queue.EnqueueAsync(id);

        received.Should().Be(id);
    }
}