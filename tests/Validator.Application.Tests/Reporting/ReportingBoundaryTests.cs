using Validator.Application.Reporting;

namespace Validator.Application.Tests.Reporting;

public sealed class ReportingBoundaryTests
{
    [Fact]
    public void DateRange_RejectsAnEndBeforeTheStart()
    {
        var start = new DateTimeOffset(2026, 1, 5, 1, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => new DateRange(start, start.AddTicks(-1)));
    }

    [Fact]
    public void ValidationReport_NormalizesNullSourceAndIsNotCleanWithoutSummary()
    {
        var report = new ValidationReport(null!, null, null!);

        Assert.Equal(string.Empty, report.SourceFile);
        Assert.False(report.IsClean);
    }

    [Fact]
    public void ValidationReport_ReflectsNonCleanSummary()
    {
        var report = new ValidationReport(new ValidationSummary(1, 0, 1), null, "input.csv");

        Assert.False(report.IsClean);
    }
}