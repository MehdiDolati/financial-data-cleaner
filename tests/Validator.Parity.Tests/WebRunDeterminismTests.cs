using System.IO;
using System.Threading.Tasks;
using Validator.Application.Web;

namespace Validator.Parity.Tests;

// Determinism and idempotency tests (T027): identical bytes + identical
// options resolve to the same WebRunId with JoinedExistingRun=true, exactly
// one record, and no duplicate work; one changed material option produces a
// separately retrievable id; wall clock never influences identity
// (quickstart scenario 4, SC-004).
public class WebRunDeterminismTests
{
    [Fact]
    public async Task Identical_bytes_and_options_join_the_same_run()
    {
        using var composition = new WebComposition();

        var first = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "clean.csv", WebComposition.CleanDailyContent(), WebComposition.Options()));
        var second = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "clean.csv", WebComposition.CleanDailyContent(), WebComposition.Options()));

        var firstAccepted = first.Should().BeOfType<WebRunSubmission.Accepted>().Subject;
        var secondAccepted = second.Should().BeOfType<WebRunSubmission.Accepted>().Subject;

        secondAccepted.Id.Should().Be(firstAccepted.Id);
        secondAccepted.JoinedExistingRun.Should().BeTrue(
            "a refresh or double submission joins the existing run (FR-010)");

        // Exactly one record exists for both submissions.
        var runs = Path.Combine(composition.Root, "runs");
        Directory.GetFiles(runs, "*.json").Should().ContainSingle();
    }

    [Fact]
    public async Task One_changed_material_option_is_a_separate_retrievable_run()
    {
        using var composition = new WebComposition();

        var first = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "clean.csv", WebComposition.CleanDailyContent(), WebComposition.Options()));
        var changed = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "clean.csv",
            WebComposition.CleanDailyContent(),
            WebComposition.Options() with { Timeframe = "D1" }));

        var firstId = ((WebRunSubmission.Accepted)first).Id;
        var changedId = ((WebRunSubmission.Accepted)changed).Id;

        changedId.Should().NotBe(firstId);

        var changedStatus = await composition.Service.GetStatusAsync(changedId);
        changedStatus.Should().BeOfType<WebRunStatusResult.Known>()
            .Which.Status.Should().Be(WebRunStatus.CompletedClean);
    }

    [Fact]
    public async Task A_moved_clock_never_changes_the_id_or_the_result()
    {
        using var composition = new WebComposition();
        composition.Clock.UtcNow = new System.DateTimeOffset(2020, 6, 1, 0, 0, 0, System.TimeSpan.Zero);

        var early = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "clean.csv", WebComposition.CleanDailyContent(), WebComposition.Options()));
        var earlyId = ((WebRunSubmission.Accepted)early).Id;

        composition.Clock.UtcNow = new System.DateTimeOffset(2030, 1, 1, 0, 0, 0, System.TimeSpan.Zero);

        var late = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "clean.csv", WebComposition.CleanDailyContent(), WebComposition.Options()));
        var lateAccepted = ((WebRunSubmission.Accepted)late);

        lateAccepted.Id.Should().Be(earlyId, "wall clock never contributes to identity (SC-004)");
        lateAccepted.JoinedExistingRun.Should().BeTrue();
    }

    [Fact]
    public async Task Same_bytes_different_display_name_join_the_same_run()
    {
        using var composition = new WebComposition();

        var first = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "upload-a.csv", WebComposition.CleanDailyContent(), WebComposition.Options()));
        var renamed = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "upload-b.csv", WebComposition.CleanDailyContent(), WebComposition.Options()));

        var renamedAccepted = ((WebRunSubmission.Accepted)renamed);
        renamedAccepted.Id.Should().Be(((WebRunSubmission.Accepted)first).Id,
            "the upload name never contributes to identity");
        renamedAccepted.JoinedExistingRun.Should().BeTrue();
    }
}

// Interruption and retrieval tests (T028): status polling during
// Pending/Running, re-query never restarts or duplicates, aborted runs end
// Failed with a diagnostic, unknown/removed ids are Unavailable, and
// completed runs stay retrievable (quickstart scenario 5, SC-007, FR-009).
public class WebRunInterruptionTests
{
    [Fact]
    public async Task A_non_terminal_run_reports_not_ready_with_its_real_status()
    {
        using var composition = new WebComposition();
        // Build a run record directly (no queue execution) to observe a
        // durably-Pending run, as after a crash between persist and enqueue.
        var options = WebComposition.Options();
        var source = new Validator.Application.Ingestion.SourceIdentity(
            "pending.csv", 10, new string('a', 64));
        var id = WebRunId.Derive(source, options, WebRunOperation.Validate);
        await composition.RunStore.TryCreateAsync(new WebRunRecord(
            id, WebRunOperation.Validate, source, options,
            submittedAtUtc: new System.DateTimeOffset(2026, 1, 1, 0, 0, 0, System.TimeSpan.Zero)));

        var status = await composition.Service.GetStatusAsync(id);
        status.Should().BeOfType<WebRunStatusResult.Known>()
            .Which.Status.Should().Be(WebRunStatus.Pending);

        var result = await composition.Service.GetResultAsync(id);
        result.Should().BeOfType<WebResultRetrieval.NotReady>()
            .Which.Status.Should().Be(WebRunStatus.Pending);

        // Polling again neither restarts nor duplicates the run.
        var statusAgain = await composition.Service.GetStatusAsync(id);
        statusAgain.Should().BeOfType<WebRunStatusResult.Known>()
            .Which.Status.Should().Be(WebRunStatus.Pending);
    }

    [Fact]
    public async Task A_completed_run_stays_retrievable_across_repeated_queries()
    {
        using var composition = new WebComposition();
        var accepted = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "clean.csv", WebComposition.CleanDailyContent(), WebComposition.Options()));
        var id = ((WebRunSubmission.Accepted)accepted).Id;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var retrieval = await composition.Service.GetResultAsync(id);
            var view = retrieval.Should().BeOfType<WebResultRetrieval.Ready>().Subject.View;
            view.Status.Should().Be(WebRunStatus.CompletedClean);
            view.Validation!.Summary.IsClean.Should().BeTrue();
        }
    }

    [Fact]
    public async Task An_aborted_run_ends_failed_never_completed_clean()
    {
        using var composition = new WebComposition();
        // An unparsable upload aborts the run: it must end Failed with a
        // diagnostic, never CompletedClean (SC-007).
        var accepted = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "unparsable.csv",
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not a csv row\nfew,columns\n1,2")),
            WebComposition.Options()));
        var id = ((WebRunSubmission.Accepted)accepted).Id;

        var status = await composition.Service.GetStatusAsync(id);
        status.Should().BeOfType<WebRunStatusResult.Known>()
            .Which.Status.Should().Be(WebRunStatus.Failed);

        var retrieval = await composition.Service.GetResultAsync(id);
        var view = retrieval.Should().BeOfType<WebResultRetrieval.Ready>().Subject.View;
        view.Status.Should().Be(WebRunStatus.Failed);
        view.Diagnostic.Should().NotBeNull();
        view.Validation.Should().BeNull();
    }

    [Fact]
    public async Task A_removed_run_is_unavailable_with_a_reason()
    {
        using var composition = new WebComposition();
        var accepted = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "clean.csv", WebComposition.CleanDailyContent(), WebComposition.Options()));
        var id = ((WebRunSubmission.Accepted)accepted).Id;

        // Retention removal deletes the record; the id must read Unavailable,
        // never an empty success (FR-032).
        var recordPath = Path.Combine(composition.Root, "runs", id.Value + ".json");
        File.Exists(recordPath).Should().BeTrue();
        File.Delete(recordPath);

        var status = await composition.Service.GetStatusAsync(id);
        status.Should().BeOfType<WebRunStatusResult.Unavailable>()
            .Which.Reason.Should().NotBeNullOrWhiteSpace();

        var result = await composition.Service.GetResultAsync(id);
        result.Should().BeOfType<WebResultRetrieval.Unavailable>()
            .Which.Reason.Should().NotBeNullOrWhiteSpace();
    }
}