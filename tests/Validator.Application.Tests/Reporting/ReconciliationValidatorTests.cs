using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Domain.Findings;

namespace Validator.Application.Tests.Reporting;

public sealed class ReconciliationValidatorTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2024, 8, 1, 10, 0, 0, TimeSpan.Zero);

    private static CheckExecution[] SixCompletedChecks() => new[]
    {
        new CheckExecution(CheckName.MissingCandles, CheckStatus.Completed),
        new CheckExecution(CheckName.DuplicateRecords, CheckStatus.Completed),
        new CheckExecution(CheckName.InvalidOhlc, CheckStatus.Completed),
        new CheckExecution(CheckName.ClosedMarketRecords, CheckStatus.Completed),
        new CheckExecution(CheckName.TimeGaps, CheckStatus.Completed),
        new CheckExecution(CheckName.MalformedRows, CheckStatus.Completed)
    };

    private static DetailedSummary Summary(long missing = 0, long duplicate = 0, long invalid = 0, long closed = 0, long gap = 0, long malformed = 0) =>
        new(missing, duplicate, invalid, closed, gap, malformed);

    private static FindingCatalogStatistics Catalog(long missing = 0, long duplicate = 0, long invalid = 0, long closed = 0, long gap = 0, long malformed = 0) =>
        new(
            new CategoryStatistics(missing, missing),
            new CategoryStatistics(duplicate, duplicate),
            new CategoryStatistics(invalid, invalid),
            new CategoryStatistics(closed, closed),
            new CategoryStatistics(gap, gap),
            new CategoryStatistics(malformed, malformed));

    private static ScanCoverage ReconciledCoverage(long examined = 100, long malformed = 3) =>
        new(examined, examined - malformed, malformed);

    private static FatalDiagnostic? RunValidator(
        IReadOnlyList<CheckExecution>? checks = null,
        DetailedSummary? summary = null,
        ScanCoverage? coverage = null,
        FindingCatalogStatistics? catalog = null) =>
        ReconciliationValidator.Validate(
            checks ?? SixCompletedChecks(),
            summary ?? Summary(missing: 1),
            coverage ?? ReconciledCoverage(),
            catalog ?? Catalog(missing: 1));

    private static FatalDiagnostic? RunValidatorRaw(
        IReadOnlyList<CheckExecution>? checks,
        DetailedSummary? summary,
        ScanCoverage? coverage,
        FindingCatalogStatistics? catalog) =>
        ReconciliationValidator.Validate(checks!, summary!, coverage!, catalog!);

    [Fact]
    public void Validate_AcceptsFullyReconciledInputs()
    {
        Assert.Null(RunValidator());
    }

    [Fact]
    public void Validate_RejectsCategoryContributionMismatch()
    {
        var diagnostic = RunValidator(summary: Summary(missing: 2));

        Assert.NotNull(diagnostic);
        Assert.Equal("REPORT_RECONCILIATION_FAILED", diagnostic!.Code);
        Assert.Equal(FailureClass.Operational, diagnostic.FailureClass);
        Assert.Equal(FailureStage.Reconciliation, diagnostic.Stage);
    }

    [Fact]
    public void Validate_RejectsUnreconciledPhysicalRowTotals()
    {
        var diagnostic = RunValidator(coverage: new ScanCoverage(10, 10, 5));

        Assert.NotNull(diagnostic);
        Assert.Equal("REPORT_RECONCILIATION_FAILED", diagnostic!.Code);
    }

    [Fact]
    public void Validate_RejectsNotCompletedChecks()
    {
        var checks = SixCompletedChecks();
        checks[2] = new CheckExecution(CheckName.InvalidOhlc, CheckStatus.NotCompleted, "Check did not run.");

        var diagnostic = RunValidator(checks: checks);
        Assert.NotNull(diagnostic);
        Assert.Equal("REPORT_RECONCILIATION_FAILED", diagnostic!.Code);
    }

    [Fact]
    public void Validate_RejectsMissingChecks()
    {
        var diagnostic = RunValidator(checks: SixCompletedChecks().Take(5).ToArray());
        Assert.NotNull(diagnostic);
    }

    [Fact]
    public void Validate_RejectsOutOfOrderChecks()
    {
        var checks = SixCompletedChecks();
        (checks[0], checks[1]) = (checks[1], checks[0]);

        var diagnostic = RunValidator(checks: checks);
        Assert.NotNull(diagnostic);
    }

    [Fact]
    public void Validate_RejectsNullInputs()
    {
        Assert.NotNull(RunValidatorRaw(null!, null!, null!, null!));
        Assert.NotNull(RunValidatorRaw(null!, Summary(missing: 1), ReconciledCoverage(), Catalog(missing: 1)));
        Assert.NotNull(RunValidatorRaw(SixCompletedChecks(), null!, ReconciledCoverage(), Catalog(missing: 1)));
        Assert.NotNull(RunValidatorRaw(SixCompletedChecks(), Summary(missing: 1), null!, Catalog(missing: 1)));
        Assert.NotNull(RunValidatorRaw(SixCompletedChecks(), Summary(missing: 1), ReconciledCoverage(), null!));
    }

    [Fact]
    public void Validate_AcceptsNotApplicableSequenceChecks()
    {
        var checks = SixCompletedChecks();
        checks[4] = new CheckExecution(CheckName.TimeGaps, CheckStatus.NotApplicable, "Fewer than two open-market timestamps.");

        Assert.Null(RunValidator(checks: checks));
    }
}