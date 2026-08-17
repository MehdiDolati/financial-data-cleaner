using System.Text;
using Validator.Application.Reporting;
using Validator.Infrastructure.Reporting;

namespace Validator.Cli.Tests;

// Staged rendering and commit behavior: a report is either published in full or
// the destination is left byte-for-byte unchanged. Staged artifacts never
// survive a run, and an output that aliases the validated input is rejected
// before any bytes are produced.
public sealed class ReportCommitTests : IDisposable
{
    private readonly string _directory;

    public ReportCommitTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"validator-commit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Publish_WithoutDestination_WritesCompleteReportToStandardOutput()
    {
        var writer = new StageAndCommitWriter(destinationPath: null);
        using var standardOutput = new StringWriter();

        var result = await writer.PublishAsync(
            async (staged, token) =>
            {
                await staged.WriteAsync("{\"contractVersion\":2,");
                await staged.WriteAsync("\"status\":\"Clean\"}");
            },
            standardOutput);

        var committed = Assert.IsType<ReportCommitResult.Committed>(result);
        Assert.Null(committed.DestinationPath);
        Assert.Equal("{\"contractVersion\":2,\"status\":\"Clean\"}", standardOutput.ToString());
        Assert.Empty(StagedArtifacts(Path.GetTempPath()));
    }

    [Fact]
    public async Task Publish_WithDestination_ReplacesExistingContentAndLeavesNoStagedArtifact()
    {
        var destination = Path.Combine(_directory, "report.json");
        await File.WriteAllTextAsync(destination, "previous-report");
        var writer = new StageAndCommitWriter(destination);
        using var standardOutput = new StringWriter();

        var result = await writer.PublishAsync(
            (staged, token) => staged.WriteAsync("{\"contractVersion\":2}"),
            standardOutput);

        var committed = Assert.IsType<ReportCommitResult.Committed>(result);
        Assert.Equal(Path.GetFullPath(destination), committed.DestinationPath);
        Assert.Equal("{\"contractVersion\":2}", await File.ReadAllTextAsync(destination));
        Assert.Empty(standardOutput.ToString());
        Assert.Empty(StagedArtifacts(_directory));
    }

    [Fact]
    public async Task Publish_WithDestination_WritesUtf8WithoutByteOrderMark()
    {
        var destination = Path.Combine(_directory, "encoding.json");
        var writer = new StageAndCommitWriter(destination);
        using var standardOutput = new StringWriter();

        await writer.PublishAsync(
            (staged, token) => staged.WriteAsync("{\"fileName\":\"prices.csv\"}"),
            standardOutput);

        var bytes = await File.ReadAllBytesAsync(destination);
        Assert.Equal(Encoding.UTF8.GetBytes("{\"fileName\":\"prices.csv\"}"), bytes);
    }

    [Fact]
    public async Task Publish_WhenDestinationAliasesInput_FailsWithoutTouchingTheInput()
    {
        var input = Path.Combine(_directory, "prices.csv");
        await File.WriteAllTextAsync(input, "2026.01.01,00:00,1,2,0.5,1.5,10");
        var writer = new StageAndCommitWriter(input, input);
        using var standardOutput = new StringWriter();
        var rendered = false;

        var result = await writer.PublishAsync(
            (staged, token) =>
            {
                rendered = true;
                return staged.WriteAsync("{}");
            },
            standardOutput);

        var failed = Assert.IsType<ReportCommitResult.Failed>(result);
        Assert.Equal("INVALID_ARGUMENT", failed.Diagnostic.Code);
        Assert.Equal(FailureClass.Configuration, failed.Diagnostic.FailureClass);
        Assert.Equal(FailureStage.ArgumentValidation, failed.Diagnostic.Stage);
        Assert.False(rendered);
        Assert.Equal("2026.01.01,00:00,1,2,0.5,1.5,10", await File.ReadAllTextAsync(input));
        Assert.Empty(standardOutput.ToString());
        Assert.Empty(StagedArtifacts(_directory));
    }

    [Fact]
    public async Task Publish_WhenRenderFails_LeavesDestinationUnchangedAndRemovesStagedArtifact()
    {
        var destination = Path.Combine(_directory, "report.json");
        await File.WriteAllTextAsync(destination, "previous-report");
        var writer = new StageAndCommitWriter(destination);
        using var standardOutput = new StringWriter();

        var result = await writer.PublishAsync(
            async (staged, token) =>
            {
                await staged.WriteAsync("{\"contractVersion\":2,\"findings\":[");
                throw new InvalidOperationException("The finding catalog could not be replayed.");
            },
            standardOutput);

        var failed = Assert.IsType<ReportCommitResult.Failed>(result);
        Assert.Equal("REPORT_RENDER_FAILED", failed.Diagnostic.Code);
        Assert.Equal(FailureStage.ReportRendering, failed.Diagnostic.Stage);
        Assert.Equal("previous-report", await File.ReadAllTextAsync(destination));
        Assert.Empty(standardOutput.ToString());
        Assert.Empty(StagedArtifacts(_directory));
    }

    [Fact]
    public async Task Publish_WhenRenderFailsWithoutDestination_PublishesNothingToStandardOutput()
    {
        var writer = new StageAndCommitWriter(destinationPath: null);
        using var standardOutput = new StringWriter();

        var result = await writer.PublishAsync(
            async (staged, token) =>
            {
                await staged.WriteAsync("{\"contractVersion\":2,\"findings\":[");
                throw new InvalidOperationException("The finding catalog could not be replayed.");
            },
            standardOutput);

        Assert.IsType<ReportCommitResult.Failed>(result);
        Assert.Empty(standardOutput.ToString());
    }

    [Fact]
    public async Task Publish_WhenCommitFails_LeavesDestinationUnchanged()
    {
        var destination = Path.Combine(_directory, "locked-report.json");
        await File.WriteAllTextAsync(destination, "previous-report");
        var writer = new StageAndCommitWriter(destination);
        using var standardOutput = new StringWriter();

        ReportCommitResult result;
        using (new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await writer.PublishAsync(
                (staged, token) => staged.WriteAsync("{\"contractVersion\":2}"),
                standardOutput);
        }

        var failed = Assert.IsType<ReportCommitResult.Failed>(result);
        Assert.Equal("REPORT_COMMIT_FAILED", failed.Diagnostic.Code);
        Assert.Equal(FailureClass.Operational, failed.Diagnostic.FailureClass);
        Assert.Equal(FailureStage.ReportCommit, failed.Diagnostic.Stage);
        Assert.Equal("previous-report", await File.ReadAllTextAsync(destination));
        Assert.Empty(StagedArtifacts(_directory));
    }

    private static string[] StagedArtifacts(string directory) =>
        Directory.GetFiles(directory, ".validator-report-*.staged");
}
