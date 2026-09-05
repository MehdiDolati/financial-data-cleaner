using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Validator.Application.Scoring;
using Validator.Application.Web;

namespace Validator.Parity.Tests;

// US3 scoring tests (T049): a Score=true run returns the identical scoring
// surface through the web view; Score=false returns no section; a failed
// run returns no section (FR-016 to FR-020, quickstart scenario 6).
public class ValidationWebServiceScoringTests
{
    private static string FindingsCsv() =>
        "date,time,open,high,low,close,volume\n" +
        "2026.01.02,00:00,0.63421,0.63580,0.63310,0.63502,125000\n" +
        "2026.01.02,00:00,0.63421,0.63580,0.63310,0.63502,125000\n" +
        "2026.01.03,00:00,0.63502,0.63310,0.63650,0.63612,118000\n" +
        "2026.01.03,00:00,0.63502,0.63310,0.63650,0.63612,abc\n" +
        "2026.01.04,00:00,0.63502,0.63310,0.63650,0.63612,118000\n" +
        "2026.01.10,00:00,0.63810,0.63950,0.63720,0.63900,122000\n";

    private static async Task<WebRunId> SubmitAsync(
        WebComposition composition, string content, WebRunOptions options)
    {
        var accepted = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "score.csv",
            new MemoryStream(Encoding.UTF8.GetBytes(content)), options));
        return ((WebRunSubmission.Accepted)accepted).Id;
    }

    [Fact]
    public async Task Score_true_run_returns_the_full_scoring_surface()
    {
        using var composition = new WebComposition();
        var id = await SubmitAsync(
            composition, FindingsCsv(), WebComposition.Options() with { Timeframe = "D1", Score = true });

        var retrieval = await composition.Service.GetResultAsync(id);
        var view = ((WebResultRetrieval.Ready)retrieval).View;

        view.Scoring.Should().NotBeNull("scoring was requested (FR-016)");
        var score = view.Scoring!.Score;

        // All six metrics exactly once in canonical order.
        score.Metrics.Should().HaveCount(6);
        score.Metrics.Select(m => m.Category).Should().ContainInOrder(
            Domain.Findings.FindingCategory.MissingCandle,
            Domain.Findings.FindingCategory.DuplicateRecord,
            Domain.Findings.FindingCategory.InvalidOhlc,
            Domain.Findings.FindingCategory.ClosedMarketRecord,
            Domain.Findings.FindingCategory.TimeGap,
            Domain.Findings.FindingCategory.MalformedRow);

        // Zero-normalization honesty: a metric with findings is never a
        // perfect score (FR-019).
        var duplicates = score.Metrics.Single(m =>
            m.Category == Domain.Findings.FindingCategory.DuplicateRecord);
        duplicates.Count.Should().BeGreaterThan(0);
        duplicates.Score.Should().NotBeNull();
        duplicates.Score!.Value.Rounded.Should().BeLessThan(100m,
            "a metric with findings is never scored 100 (FR-019)");

        // The dataset average is present and equally honest.
        score.Dataset.Average.Should().NotBeNull("the dataset average is carried (FR-017)");
        score.Dataset.Average!.Value.Rounded.Should().BeLessThan(100m,
            "a findings dataset cannot average 100 (FR-019)");

        // The resolved weighting is carried with its normalized shares.
        score.Weighting.Should().NotBeNull();
        score.Weighting.Weights.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Score_false_run_has_no_scoring_section()
    {
        using var composition = new WebComposition();
        var id = await SubmitAsync(
            composition, FindingsCsv(), WebComposition.Options() with { Timeframe = "D1", Score = false });

        var retrieval = await composition.Service.GetResultAsync(id);
        var view = ((WebResultRetrieval.Ready)retrieval).View;
        view.Scoring.Should().BeNull("scoring was not requested (FR-016)");
    }

    [Fact]
    public async Task Failed_run_has_no_scoring_section()
    {
        using var composition = new WebComposition();
        var id = await SubmitAsync(
            composition, string.Empty, WebComposition.Options() with { Score = true });

        var retrieval = await composition.Service.GetResultAsync(id);
        var view = ((WebResultRetrieval.Ready)retrieval).View;
        view.Status.Should().Be(WebRunStatus.Failed);
        view.Scoring.Should().BeNull("a failed run never carries a score (FR-018)");
    }

    [Fact]
    public async Task Custom_weights_flow_through_to_the_view()
    {
        using var composition = new WebComposition();
        var custom = WebComposition.Options() with
        {
            Timeframe = "D1",
            Score = true,
            ScoreWeights = "missingCandles=1.0,duplicateRecords=1.0,invalidOhlc=1.0,closedMarketRecords=1.0,timeGaps=1.0,malformedRows=1.0"
        };

        var id = await SubmitAsync(composition, FindingsCsv(), custom);

        var retrieval = await composition.Service.GetResultAsync(id);
        var view = ((WebResultRetrieval.Ready)retrieval).View;
        view.Scoring.Should().NotBeNull();

        // The submitted custom weights are carried verbatim into the view:
        // each of the six categories was submitted at 1.0 (FR-016).
        var weighting = view.Scoring!.Score.Weighting;
        weighting.Source.Should().Be(ScoreWeightingSource.CallerSupplied);
        foreach (var weight in weighting.Weights)
        {
            weight.Weight.Should().Be(1.0m, "each submitted custom weight flows through to the view");
        }
    }
}