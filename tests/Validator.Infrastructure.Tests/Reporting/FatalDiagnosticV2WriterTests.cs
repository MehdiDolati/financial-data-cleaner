using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Validator.Application.Reporting;
using Validator.Domain.Findings;
using Validator.Infrastructure.Reporting;
using Xunit;

namespace Validator.Infrastructure.Tests.Reporting;

// A fatal v2 document explains where processing stopped and which checks did
// not run, and exposes none of the fields that would let a consumer treat it as
// a complete report.
public sealed class FatalDiagnosticV2WriterTests
{
    private static readonly string Sha256 = new('b', 64);

    [Fact]
    public void Render_DefaultDiagnostic_DeclaresFatalStatusAndIncompleteFindingSet()
    {
        var json = new FatalDiagnosticV2Writer().Render(new FatalDiagnostic(
            "SOURCE_UNAVAILABLE",
            "The input file could not be opened for reading.",
            "Verify the path and read permissions, then run the validation again."));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("contractVersion").GetInt32());
        Assert.Equal("Fatal", root.GetProperty("status").GetString());
        Assert.False(root.GetProperty("findingSetComplete").GetBoolean());
        Assert.Equal("SOURCE_UNAVAILABLE", root.GetProperty("code").GetString());
        Assert.Equal("Operational", root.GetProperty("failureClass").GetString());
        Assert.Equal("SourceIdentity", root.GetProperty("stage").GetString());
        Assert.Equal(
            "The input file could not be opened for reading.",
            root.GetProperty("reason").GetString());
        Assert.Equal(
            "Verify the path and read permissions, then run the validation again.",
            root.GetProperty("guidance").GetString());
    }

    [Fact]
    public void Render_NeverExposesSuccessfulReportFields()
    {
        var json = new FatalDiagnosticV2Writer().Render(new FatalDiagnostic(
            "INVALID_CSV",
            "The source could not be parsed as delimited text.",
            "Repair the delimiter or quoting near the reported line."));

        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.DoesNotContain("summary", names);
        Assert.DoesNotContain("reconciliation", names);
        Assert.DoesNotContain("isClean", names);
        Assert.DoesNotContain("coverage", names);
        Assert.DoesNotContain("findings", names);
        Assert.DoesNotContain("totalErrors", names);
        Assert.DoesNotContain("uniqueProblems", names);
    }

    [Fact]
    public void Render_ListsSixChecksInCanonicalOrderWithReasonsForCheckThatDidNotRun()
    {
        var json = new FatalDiagnosticV2Writer().Render(new FatalDiagnostic(
            "VALIDATION_INCOMPLETE",
            "Validation stopped before every check completed.",
            "Re-run the validation once the source is stable."));

        using var document = JsonDocument.Parse(json);
        var checks = document.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Equal(6, checks.Length);
        Assert.Equal(
            new[]
            {
                "MissingCandles",
                "DuplicateRecords",
                "InvalidOhlc",
                "ClosedMarketRecords",
                "TimeGaps",
                "MalformedRows"
            },
            checks.Select(check => check.GetProperty("check").GetString()).ToArray());
        Assert.All(checks, check =>
        {
            Assert.Equal("NotCompleted", check.GetProperty("status").GetString());
            Assert.False(string.IsNullOrWhiteSpace(check.GetProperty("reason").GetString()));
        });
    }

    [Fact]
    public void Render_CompletedCheck_OmitsReason()
    {
        var checks = new CheckExecution[]
        {
            new(CheckName.MissingCandles, CheckStatus.Completed),
            new(CheckName.DuplicateRecords, CheckStatus.Completed),
            new(CheckName.InvalidOhlc, CheckStatus.Completed),
            new(CheckName.ClosedMarketRecords, CheckStatus.Completed),
            new(CheckName.TimeGaps, CheckStatus.NotApplicable, "Fewer than two open-market timestamps."),
            new(CheckName.MalformedRows, CheckStatus.NotCompleted, "Rendering stopped before the check finished.")
        };

        var json = new FatalDiagnosticV2Writer().Render(new FatalDiagnostic(
            "REPORT_RENDER_FAILED",
            "The report could not be rendered completely.",
            "Retry the run; if it repeats, report the failure with the input fingerprint.",
            checks: checks));

        using var document = JsonDocument.Parse(json);
        var rendered = document.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        Assert.False(rendered[0].TryGetProperty("reason", out _));
        Assert.Equal("NotApplicable", rendered[4].GetProperty("status").GetString());
        Assert.Equal("Fewer than two open-market timestamps.", rendered[4].GetProperty("reason").GetString());
        Assert.Equal("NotCompleted", rendered[5].GetProperty("status").GetString());
    }

    [Fact]
    public void Render_WritesOnlyEstablishedSourceFieldsAndUtcLocation()
    {
        var json = new FatalDiagnosticV2Writer().Render(new FatalDiagnostic(
            "INVALID_STRUCTURE",
            "A physical row did not contain the expected columns.",
            "Repair the row so every required column is present.",
            new PartialSourceIdentity("prices.csv", 2048, Sha256),
            new FailureLocation(
                sourceLine: 42,
                timestampUtc: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                field: "High")));

        using var document = JsonDocument.Parse(json);
        var source = document.RootElement.GetProperty("source");
        Assert.Equal("prices.csv", source.GetProperty("fileName").GetString());
        Assert.Equal(2048, source.GetProperty("byteSize").GetInt64());
        Assert.Equal(Sha256, source.GetProperty("sha256").GetString());

        var location = document.RootElement.GetProperty("location");
        Assert.Equal(42, location.GetProperty("sourceLine").GetInt64());
        Assert.Equal("2026-01-02T03:04:05Z", location.GetProperty("timestampUtc").GetString());
        Assert.Equal("High", location.GetProperty("field").GetString());
    }

    [Fact]
    public void Render_WithoutEstablishedSourceOrLocation_OmitsBothObjects()
    {
        var json = new FatalDiagnosticV2Writer().Render(new FatalDiagnostic(
            "INVALID_ARGUMENT",
            "The timeframe override is not a valid code.",
            "Use an M<n>, H<n>, or D<n> timeframe code."));

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("source", out _));
        Assert.False(document.RootElement.TryGetProperty("location", out _));
    }

    [Fact]
    public async Task WriteAsync_EmitsExactlyOneDocumentWithNoTrailingNewline()
    {
        var diagnostic = new FatalDiagnostic(
            "AMBIGUOUS_TIMEFRAME",
            "A unique timeframe could not be inferred.",
            "Pass an explicit --timeframe value.");
        var writer = new FatalDiagnosticV2Writer();
        using var destination = new System.IO.StringWriter();

        await writer.WriteAsync(diagnostic, destination);

        var text = destination.ToString();
        Assert.Equal(writer.Render(diagnostic), text);
        Assert.DoesNotContain("\n", text);
        using var document = JsonDocument.Parse(text);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }
}
