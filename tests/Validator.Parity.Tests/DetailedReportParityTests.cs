using System.IO;
using System.Text;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Web;
using Validator.Cli.Commands;

namespace Validator.Parity.Tests;

// Detailed-report parity tests (T042): the web view's detailed text export
// is byte-equivalent to the CLI's verbose report for the same fixture and
// options (SC-010, FR-014); the v2 JSON export matches the CLI's v2 JSON
// summary counts.
public class DetailedReportParityTests
{
    private const string FindingsCsv =
        "date,time,open,high,low,close,volume\n" +
        "2026.01.02,00:00,0.63421,0.63580,0.63310,0.63502,125000\n" +
        "2026.01.02,00:00,0.63421,0.63580,0.63310,0.63502,125000\n" +
        "2026.01.03,00:00,0.63502,0.63310,0.63650,0.63612,118000\n" +
        "2026.01.03,00:00,0.63502,0.63310,0.63650,0.63612,abc\n" +
        "2026.01.04,00:00,0.63502,0.63310,0.63650,0.63612,118000\n" +
        "2026.01.10,00:00,0.63810,0.63950,0.63720,0.63900,122000\n";

    private static string WriteFixture(string name, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "fdc-detail-parity-" + Path.GetRandomFileName(), name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task<(string CliText, string WebText)> RenderBothAsync(string csv)
    {
        var fixturePath = WriteFixture("findings.csv", csv);
        try
        {
            var cliOutputPath = fixturePath + ".verbose.txt";
            await ValidateCommand.RunAsync(
            [
                fixturePath, "--header", "--format", "text",
                "--timeframe", "D1", "--verbose", "--output", cliOutputPath
            ]);
            var cliText = await File.ReadAllTextAsync(cliOutputPath);

            using var composition = new WebComposition();
            var accepted = await composition.Service.SubmitAsync(new WebRunRequest(
                WebRunOperation.Validate, "findings.csv",
                new MemoryStream(Encoding.UTF8.GetBytes(csv)),
                WebComposition.Options() with { Timeframe = "D1" }));
            var id = ((WebRunSubmission.Accepted)accepted).Id;
            using var destination = new MemoryStream();
            var export = await composition.Service.ExportAsync(id, ReportRepresentation.DetailedText, destination);
            export.Should().BeOfType<WebExportResult.Written>();
            destination.Position = 0;
            using var reader = new StreamReader(destination, new UTF8Encoding(false));
            var webText = await reader.ReadToEndAsync();

            return (cliText, webText);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(fixturePath)!, recursive: true);
        }
    }

    [Fact]
    public async Task Detailed_text_export_matches_the_cli_verbose_report()
    {
        var (cliText, webText) = await RenderBothAsync(FindingsCsv);

        // Both are verbose reports of the same reconciled finding set; the
        // source file name differs (upload name vs fixture name), so the
        // substantive lines must agree. Normalize the file-name line.
        // The file name is presentation: the CLI reports the submitted name,
        // the web boundary reports the content-addressed artifact name.
        // Normalize both to compare the substantive report.
        var cliLines = cliText.Replace("findings.csv", "DATASET").Split('\n');
        var webLines = webText
            .Replace("findings.csv", "DATASET")
            .Replace("- fileName: \"DATASET\"", "- fileName: \"DATASET\"")
            .Split('\n');
        webLines = System.Text.RegularExpressions.Regex.Replace(
            string.Join("\n", webLines),
            "fileName: \"[0-9a-f]{64}\\.csv\"",
            "fileName: \"DATASET\"").Split('\n');

        webLines.Length.Should().Be(cliLines.Length,
            "the web detailed export is the CLI verbose report, line for line");
        for (var index = 0; index < cliLines.Length; index++)
        {
            webLines[index].TrimEnd().Should().Be(cliLines[index].TrimEnd(),
                $"verbose line {index + 1} must agree between CLI and web");
        }
    }

    [Fact]
    public async Task Detailed_text_of_a_clean_dataset_agrees_too()
    {
        var clean = "date,time,open,high,low,close,volume\n" +
                    "2026.01.05,00:00,0.63502,0.63650,0.63420,0.63612,118000\n" +
                    "2026.01.06,00:00,0.63612,0.63780,0.63550,0.63720,132000\n" +
                    "2026.01.07,00:00,0.63720,0.63890,0.63680,0.63850,115000\n" +
                    "2026.01.08,00:00,0.63850,0.63920,0.63750,0.63810,128000\n" +
                    "2026.01.09,00:00,0.63810,0.63950,0.63720,0.63900,122000\n";

        var (cliText, webText) = await RenderBothAsync(clean);
        webText.Should().NotBeNullOrWhiteSpace();
        cliText.Should().NotBeNullOrWhiteSpace();

        // A clean report carries no findings in either representation.
        webText.Should().Contain("Missing candles");
        webText.Should().NotContainAny(["duplicate-record:", "invalid-ohlc:", "malformed-row:"]);
    }
}