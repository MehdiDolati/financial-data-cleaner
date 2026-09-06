using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Web;
using Validator.Application.Reporting;
using Validator.Infrastructure.Web;

namespace Validator.Parity.Tests;

// US2 export tests (T040): ExportAsync on a terminal success returns Written
// for each available representation using the existing writers; Failed,
// non-terminal, and unknown runs return NotAvailable; export streams UTF-8
// without silently truncating on a large-finding fixture (FR-014, quickstart
// scenario 3).
public class ValidationWebServiceExportTests
{
    private static async Task<WebRunId> SubmitAsync(WebComposition composition, string name, string content, WebRunOptions? options = null)
    {
        var accepted = await composition.Service.SubmitAsync(new WebRunRequest(
            WebRunOperation.Validate, name,
            new MemoryStream(Encoding.UTF8.GetBytes(content)),
            options ?? WebComposition.Options()));
        return ((WebRunSubmission.Accepted)accepted).Id;
    }

    private static string CleanDailyCsv() =>
        "date,time,open,high,low,close,volume\n" +
        "2026.01.02,00:00,0.63421,0.63580,0.63310,0.63502,125000\n" +
        "2026.01.05,00:00,0.63502,0.63650,0.63420,0.63612,118000\n" +
        "2026.01.06,00:00,0.63612,0.63780,0.63550,0.63720,132000\n" +
        "2026.01.07,00:00,0.63720,0.63890,0.63680,0.63850,115000\n" +
        "2026.01.08,00:00,0.63850,0.63920,0.63750,0.63810,128000\n" +
        "2026.01.09,00:00,0.63810,0.63950,0.63720,0.63900,122000\n";
    [Fact]
    public async Task Each_available_representation_exports_written_content()
    {
        using var composition = new WebComposition();
        var id = await SubmitAsync(composition, "clean.csv", CleanDailyCsv());

        var view = ((WebResultRetrieval.Ready)await composition.Service.GetResultAsync(id)).View;
        view.AvailableExports.Should().NotBeEmpty();

        foreach (var representation in view.AvailableExports)
        {
            using var destination = new MemoryStream();
            var result = await composition.Service.ExportAsync(id, representation, destination);
            result.Should().BeOfType<WebExportResult.Written>(
                $"the terminal-success run exports {representation}");
            destination.Length.Should().BeGreaterThan(0, $"{representation} writes actual content");
        }
    }

    [Fact]
    public async Task JsonV2_export_is_valid_utf8_json_matching_the_view_summary()
    {
        using var composition = new WebComposition();
        var id = await SubmitAsync(composition, "clean.csv", CleanDailyCsv());

        using var destination = new MemoryStream();
        var result = await composition.Service.ExportAsync(id, ReportRepresentation.JsonV2, destination);
        result.Should().BeOfType<WebExportResult.Written>();

        destination.Position = 0;
        using var reader = new StreamReader(destination, new UTF8Encoding(false));
        var json = await reader.ReadToEndAsync();

        json.Should().NotStartWith("\uFEFF", "exports stream UTF-8 without a BOM");
        using var document = System.Text.Json.JsonDocument.Parse(json);

        var view = ((WebResultRetrieval.Ready)await composition.Service.GetResultAsync(id)).View;
        document.RootElement.GetProperty("summary").GetProperty("duplicateRecords").GetInt64()
            .Should().Be(view.Validation!.Summary.DuplicateRecords);
        document.RootElement.GetProperty("status").GetString()
            .Should().Be(view.Status == WebRunStatus.CompletedClean ? "Clean" : "FindingsDetected");
    }

    [Fact]
    public async Task Failed_non_terminal_and_unknown_runs_never_export()
    {
        using var composition = new WebComposition();

        // Failed run.
        var failedId = await SubmitAsync(composition, "empty.csv", string.Empty);
        (await composition.Service.ExportAsync(failedId, ReportRepresentation.JsonV2, new MemoryStream()))
            .Should().BeOfType<WebExportResult.NotAvailable>();

        // Non-terminal (pending) run.
        var options = WebComposition.Options();
        var source = new Validator.Application.Ingestion.SourceIdentity("pending.csv", 10, new string('a', 64));
        var pendingId = WebRunId.Derive(source, options, WebRunOperation.Validate);
        await composition.RunStore.TryCreateAsync(new WebRunRecord(
            pendingId, WebRunOperation.Validate, source, options,
            submittedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        (await composition.Service.ExportAsync(pendingId, ReportRepresentation.JsonV2, new MemoryStream()))
            .Should().BeOfType<WebExportResult.NotAvailable>();

        // Unknown run.
        var unknownId = WebRunId.Parse(new string('0', 64));
        (await composition.Service.ExportAsync(unknownId, ReportRepresentation.JsonV2, new MemoryStream()))
            .Should().BeOfType<WebExportResult.NotAvailable>();
    }

    [Fact]
    public async Task A_large_finding_count_is_never_silently_truncated()
    {
        using var composition = new WebComposition();
        // 600 duplicate rows -> hundreds of findings; the streamed export
        // must carry every one of them.
        var builder = new StringBuilder("date,time,open,high,low,close,volume\n");
        for (var day = 1; day <= 300; day++)
        {
            var date = new DateTime(2026, 1, 1).AddDays(day - 1).ToString("yyyy.MM.dd");
            builder.Append($"{date},00:00,0.63421,0.63580,0.63310,0.63502,125000\n");
            builder.Append($"{date},00:00,0.63421,0.63580,0.63310,0.63502,125000\n");
        }

        var id = await SubmitAsync(
            composition, "large.csv", builder.ToString(), WebComposition.Options() with { Timeframe = "D1" });

        var view = ((WebResultRetrieval.Ready)await composition.Service.GetResultAsync(id)).View;
        view.Validation!.Summary.DuplicateRecords.Should().Be(300);

        using var destination = new MemoryStream();
        var result = await composition.Service.ExportAsync(id, ReportRepresentation.JsonV2, destination);
        result.Should().BeOfType<WebExportResult.Written>();

        destination.Position = 0;
        using var reader = new StreamReader(destination, new UTF8Encoding(false));
        var json = await reader.ReadToEndAsync();
        using var document = System.Text.Json.JsonDocument.Parse(json);
        document.RootElement.GetProperty("findings").GetArrayLength()
            .Should().Be((int)(view.Validation.Summary.DuplicateRecords + view.Validation.Summary.ClosedMarketRecords),
            "every duplicate and closed-market finding is exported - never silently truncated");
    }
}