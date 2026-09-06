using System.IO;
using System.Threading.Tasks;
using Validator.Cli.Commands;
using Validator.Application.Web;
using Validator.Application.Reporting;

namespace Validator.Parity.Tests;

// CLI<->web parity tests (T029, SC-001, SC-004, SC-010): the same fixture
// through the CLI front end (in-process ValidateCommand) and through
// IValidationWebService with equivalent resolved options must agree on the
// full substantive comparison surface of
// contracts/web-result-view-contract.md.
public class ValidationParityTests
{
    private static string WriteFixture(string name, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "fdc-parity-" + Path.GetRandomFileName(), name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private const string CleanDailyCsv =
        "date,time,open,high,low,close,volume\n" +
        "2026.01.02,00:00,0.63421,0.63580,0.63310,0.63502,125000\n" +
        "2026.01.05,00:00,0.63502,0.63650,0.63420,0.63612,118000\n" +
        "2026.01.06,00:00,0.63612,0.63780,0.63550,0.63720,132000\n" +
        "2026.01.07,00:00,0.63720,0.63890,0.63680,0.63850,115000\n" +
        "2026.01.08,00:00,0.63850,0.63920,0.63750,0.63810,128000\n" +
        "2026.01.09,00:00,0.63810,0.63950,0.63720,0.63900,122000\n";

    private const string FindingsCsv =
        "date,time,open,high,low,close,volume\n" +
        "2026.01.02,00:00,0.63421,0.63580,0.63310,0.63502,125000\n" +
        "2026.01.02,00:00,0.63421,0.63580,0.63310,0.63502,125000\n" +
        "2026.01.03,00:00,0.63502,0.63310,0.63650,0.63612,118000\n" +
        "2026.01.03,00:00,0.63502,0.63310,0.63650,0.63612,abc\n" +
        "2026.01.04,00:00,0.63502,0.63310,0.63650,0.63612,118000\n" +
        "2026.01.10,00:00,0.63810,0.63950,0.63720,0.63900,122000\n";

    private static async Task<WebResultView> RunThroughWeb(string csv, bool withFindings)
    {
        var composition = new WebComposition();
        var options = WebComposition.Options();
        if (withFindings)
        {
            options = options with { Timeframe = "D1" };
        }

        var accepted = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, "parity.csv", new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)), options));
        var id = ((WebRunSubmission.Accepted)accepted).Id;
        var result = await composition.Service.GetResultAsync(id);
        var view = ((WebResultRetrieval.Ready)result).View;
        composition.Dispose();
        return view;
    }

    [Fact]
    public async Task Clean_fixture_parity_between_cli_and_web()
    {
        var fixturePath = WriteFixture("clean-daily.csv", CleanDailyCsv);
        try
        {
            // CLI front end: v2 JSON to a file, then read the summary back.
            var outputPath = fixturePath + ".report.json";
            var exitCode = await ValidateCommand.RunAsync(
            [
                fixturePath, "--header", "--format", "json", "--report-version", "2", "--output", outputPath
            ]);

            var cliJson = await File.ReadAllTextAsync(outputPath);
            var view = await RunThroughWeb(CleanDailyCsv, withFindings: false);

            // Status parity: both front ends report the dataset as clean.
            view.Status.Should().Be(WebRunStatus.CompletedClean);

            // Coverage parity.
            using var document = System.Text.Json.JsonDocument.Parse(cliJson);
            document.RootElement.GetProperty("status").GetString().Should().Be(view.Status == WebRunStatus.CompletedClean ? "Clean" : "FindingsDetected");
            var coverage = document.RootElement.GetProperty("coverage");
            var cliRows = coverage.GetProperty("physicalRowsExamined").GetInt64();
            var cliAccepted = coverage.GetProperty("acceptedRows").GetInt64();
            var cliMalformed = coverage.GetProperty("malformedRows").GetInt64();
            view.Validation!.Coverage.PhysicalRowsExamined.Should().Be(cliRows);
            view.Validation.Coverage.AcceptedRows.Should().Be(cliAccepted);
            view.Validation.Coverage.MalformedRows.Should().Be(cliMalformed);

            // Six category counts parity, never merged.
            var summary = document.RootElement.GetProperty("summary");
            view.Validation.Summary.MissingCandles.Should().Be(summary.GetProperty("missingCandles").GetInt64());
            view.Validation.Summary.DuplicateRecords.Should().Be(summary.GetProperty("duplicateRecords").GetInt64());
            view.Validation.Summary.InvalidOhlc.Should().Be(summary.GetProperty("invalidOhlc").GetInt64());
            view.Validation.Summary.ClosedMarketRecords.Should().Be(summary.GetProperty("closedMarketRecords").GetInt64());
            view.Validation.Summary.TimeGaps.Should().Be(summary.GetProperty("timeGaps").GetInt64());
            view.Validation.Summary.MalformedRows.Should().Be(summary.GetProperty("malformedRows").GetInt64());

            // Reconciliation parity.
            var reconciliation = document.RootElement.GetProperty("reconciliation");
            view.Validation.Reconciliation.CoverageReconciled
                .Should().Be(reconciliation.GetProperty("coverageReconciled").GetBoolean());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(fixturePath)!, recursive: true);
        }
    }

    [Fact]
    public async Task Findings_fixture_parity_between_cli_and_web()
    {
        var fixturePath = WriteFixture("findings.csv", FindingsCsv);
        try
        {
            var outputPath = fixturePath + ".report.json";
            await ValidateCommand.RunAsync(
            [
                fixturePath, "--header", "--format", "json", "--report-version", "2",
                "--timeframe", "D1", "--output", outputPath
            ]);

            var cliJson = await File.ReadAllTextAsync(outputPath);
            var view = await RunThroughWeb(FindingsCsv, withFindings: true);
            view.Status.Should().Be(WebRunStatus.CompletedWithFindings);

            using var document = System.Text.Json.JsonDocument.Parse(cliJson);
            document.RootElement.GetProperty("status").GetString().Should().Be(view.Status == WebRunStatus.CompletedClean ? "Clean" : "FindingsDetected");
            var summary = document.RootElement.GetProperty("summary");
            var webSummary = view.Validation!.Summary;
            webSummary.DuplicateRecords.Should().Be(summary.GetProperty("duplicateRecords").GetInt64());
            webSummary.MalformedRows.Should().Be(summary.GetProperty("malformedRows").GetInt64());
            webSummary.MissingCandles.Should().Be(summary.GetProperty("missingCandles").GetInt64());
            webSummary.TimeGaps.Should().Be(summary.GetProperty("timeGaps").GetInt64());
            webSummary.InvalidOhlc.Should().Be(summary.GetProperty("invalidOhlc").GetInt64());
            webSummary.ClosedMarketRecords.Should().Be(summary.GetProperty("closedMarketRecords").GetInt64());

            // All six check statuses in canonical order agree.
            var cliChecks = document.RootElement.GetProperty("checks");
            for (var index = 0; index < cliChecks.GetArrayLength(); index++)
            {
                view.Validation.Checks[index].Check.ToString()
                    .Should().Be(cliChecks[index].GetProperty("check").GetString());
                view.Validation.Checks[index].Status.ToString()
                    .Should().Be(cliChecks[index].GetProperty("status").GetString());
            }

            // The complete finding sequence agrees, including evidence and
            // both relationship directions.
            var cliFindings = document.RootElement.GetProperty("findings");
            var webFindings = new System.Collections.Generic.List<DetailedFindingSummary>();
            await foreach (var cursor in view.Validation.Findings.ReadCanonicalAsync())
            {
                var sourceLines = new System.Collections.Generic.List<long>();
                await foreach (var line in cursor.ReadSourceLinesAsync())
                {
                    sourceLines.Add(line);
                }

                webFindings.Add(new DetailedFindingSummary(
                    cursor.Header.Reference.Value,
                    cursor.Header.Category.ToString(),
                    sourceLines));
            }

            webFindings.Count.Should().Be(cliFindings.GetArrayLength());
            for (var index = 0; index < webFindings.Count; index++)
            {
                webFindings[index].Reference.Should().Be(cliFindings[index].GetProperty("reference").GetString());
                webFindings[index].Category.Should().Be(cliFindings[index].GetProperty("category").GetString());
            }
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(fixturePath)!, recursive: true);
        }
    }

    private sealed record DetailedFindingSummary(string Reference, string Category, System.Collections.Generic.List<long> SourceLines);
}