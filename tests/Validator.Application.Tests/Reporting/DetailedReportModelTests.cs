using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Domain.Calendars;
using Validator.Domain.Findings;

namespace Validator.Application.Tests.Reporting;

public sealed class DetailedReportModelTests
{
    private static readonly SourceIdentity Source = new("known-defects.csv", 1024, new string('a', 64));
    private static readonly DateTimeOffset Ts = new(2024, 8, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SourceIdentity_RejectsEmptyOrUnsafeFileName()
    {
        Assert.Throws<ArgumentException>(() => new SourceIdentity("", 10, Hex64('b')));
        Assert.Throws<ArgumentException>(() => new SourceIdentity("   ", 10, Hex64('b')));
        Assert.Throws<ArgumentException>(() => new SourceIdentity(@"C:\data\file.csv", 10, Hex64('b')));
        Assert.Throws<ArgumentException>(() => new SourceIdentity("data/file.csv", 10, Hex64('b')));
        Assert.Throws<ArgumentException>(() => new SourceIdentity("a:b.csv", 10, Hex64('b')));
    }

    [Fact]
    public void SourceIdentity_RejectsNegativeByteSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceIdentity("file.csv", -1, Hex64('b')));
    }

    [Fact]
    public void SourceIdentity_RejectsInvalidSha256()
    {
        Assert.Throws<ArgumentException>(() => new SourceIdentity("file.csv", 10, ""));
        Assert.Throws<ArgumentException>(() => new SourceIdentity("file.csv", 10, new string('g', 64)));
        Assert.Throws<ArgumentException>(() => new SourceIdentity("file.csv", 10, new string('A', 64)));
        Assert.Throws<ArgumentException>(() => new SourceIdentity("file.csv", 10, new string('a', 63)));
        Assert.Throws<ArgumentException>(() => new SourceIdentity("file.csv", 10, new string('a', 65)));
    }

    [Fact]
    public void SourceIdentity_AcceptsSafeValues()
    {
        var identity = new SourceIdentity("file.csv", 1024, Hex64('c'));

        Assert.Equal("file.csv", identity.FileName);
        Assert.Equal(1024, identity.ByteSize);
        Assert.Equal(Hex64('c'), identity.Sha256);
    }

    [Fact]
    public void ScanCoverage_RejectsNegativeCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanCoverage(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanCoverage(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanCoverage(0, 0, -1));
    }

    [Fact]
    public void ScanCoverage_ReconcilesWhenCountsAlign()
    {
        Assert.True(new ScanCoverage(10, 8, 2).IsReconciled);
        Assert.True(new ScanCoverage(0, 0, 0).IsReconciled);
        Assert.False(new ScanCoverage(10, 8, 3).IsReconciled);
    }

    [Fact]
    public void TimestampInterpretation_RejectsCanonicalOffsetViolations()
    {
        Assert.Throws<ArgumentException>(() =>
            TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+15:00"));
        Assert.Throws<ArgumentException>(() =>
            TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "02:00"));
        Assert.Throws<ArgumentException>(() =>
            TimestampInterpretation.CreateCombined("yyyy-MM-dd HH:mm:ss", "Timestamp", "+00:61"));
    }

    [Fact]
    public void TimestampInterpretation_RejectsBlankFormats()
    {
        Assert.Throws<ArgumentException>(() =>
            TimestampInterpretation.CreateSeparate("", "HH:mm", "+02:00"));
        Assert.Throws<ArgumentException>(() =>
            TimestampInterpretation.CreateCombined("yyyy-MM-dd HH:mm:ss", "", "+02:00"));
    }

    [Fact]
    public void TimestampInterpretation_PopulatesOnlyModeRelevantFields()
    {
        var separate = TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00");
        var combined = TimestampInterpretation.CreateCombined("yyyy-MM-dd HH:mm:ss", "Timestamp", "+00:00");

        Assert.Equal(TimestampMode.SeparateDateTime, separate.Mode);
        Assert.Equal("yyyy.MM.dd", separate.DateFormat);
        Assert.Equal("HH:mm", separate.TimeFormat);
        Assert.Equal("+02:00", separate.SourceOffset);
        Assert.Null(separate.TimestampFormat);
        Assert.Null(separate.TimestampColumn);

        Assert.Equal(TimestampMode.CombinedTimestamp, combined.Mode);
        Assert.Equal("yyyy-MM-dd HH:mm:ss", combined.TimestampFormat);
        Assert.Equal("Timestamp", combined.TimestampColumn);
        Assert.Equal("+00:00", combined.SourceOffset);
        Assert.Null(combined.DateFormat);
        Assert.Null(combined.TimeFormat);
    }

    [Fact]
    public void CalendarContext_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => new CalendarContext("other", "Name"));
        Assert.Throws<ArgumentException>(() => new CalendarContext("forex", " "));
        Assert.Throws<ArgumentException>(() =>
            new CalendarContext("equities", "Equities", definitionSha256: "not-a-hash"));
    }

    [Fact]
    public void CalendarContext_AcceptsSessionsAndDefinitionFingerprint()
    {
        var session = new WeeklySession(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(17));
        var context = new CalendarContext(
            "equities", "Equities US", [session], timeZone: "America/New_York", definitionSha256: Hex64('d'));

        Assert.Equal("equities", context.Profile);
        Assert.Equal("Equities US", context.Name);
        Assert.Equal("America/New_York", context.TimeZone);
        Assert.Equal(Hex64('d'), context.DefinitionSha256);
        Assert.Single(context.Sessions);
    }

    [Fact]
    public void ValidationContextSnapshot_RejectsInvalidArguments()
    {
        var calendar = new CalendarContext("forex", "Forex 24-5");
        var timestamp = TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00");

        Assert.Throws<ArgumentException>(() =>
            new ValidationContextSnapshot("X1", calendar, timestamp, "comma", false, null));
        Assert.Throws<ArgumentException>(() =>
            new ValidationContextSnapshot("H1", calendar, timestamp, "pipe", false, null));
        Assert.Throws<ArgumentNullException>(() =>
            new ValidationContextSnapshot("H1", null!, timestamp, "comma", false, null));
        Assert.Throws<ArgumentNullException>(() =>
            new ValidationContextSnapshot("H1", calendar, null!, "comma", false, null));
    }

    [Fact]
    public void ValidationContextSnapshot_ExposesResolvedContext()
    {
        var calendar = new CalendarContext("forex", "Forex 24-5");
        var timestamp = TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00");
        var range = new DateRange(Ts, Ts.AddHours(3));

        var context = new ValidationContextSnapshot("H1", calendar, timestamp, "comma", true, range);

        Assert.Equal("H1", context.Timeframe);
        Assert.Equal("comma", context.Delimiter);
        Assert.True(context.HasHeader);
        Assert.Equal(range, context.DateRange);
    }

    [Fact]
    public void CheckExecution_RequiresReasonForNonCompleted()
    {
        Assert.Throws<ArgumentException>(() => new CheckExecution(CheckName.TimeGaps, CheckStatus.NotApplicable));
        Assert.Throws<ArgumentException>(() => new CheckExecution(CheckName.TimeGaps, CheckStatus.NotCompleted));
    }

    [Fact]
    public void CheckExecution_RejectsReasonForCompleted()
    {
        Assert.Throws<ArgumentException>(() =>
            new CheckExecution(CheckName.TimeGaps, CheckStatus.Completed, "ran"));
    }

    [Fact]
    public void CheckExecution_ExposesState()
    {
        var completed = new CheckExecution(CheckName.MissingCandles, CheckStatus.Completed);
        var notApplicable = new CheckExecution(CheckName.TimeGaps, CheckStatus.NotApplicable, "Fewer than two open timestamps.");

        Assert.Equal(CheckName.MissingCandles, completed.Check);
        Assert.Equal(CheckStatus.Completed, completed.Status);
        Assert.Null(completed.Reason);
        Assert.Equal(CheckStatus.NotApplicable, notApplicable.Status);
        Assert.Equal("Fewer than two open timestamps.", notApplicable.Reason);
    }

    [Fact]
    public void DetailedSummary_RejectsNegativeCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetailedSummary(-1, 0, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetailedSummary(0, -1, 0, 0, 0, 0));
    }

    [Fact]
    public void DetailedSummary_DerivesTotalAndClean()
    {
        var clean = new DetailedSummary(0, 0, 0, 0, 0, 0);
        var detected = new DetailedSummary(2, 1, 0, 0, 0, 1);

        Assert.True(clean.IsClean);
        Assert.Equal(0, clean.TotalFindings);
        Assert.False(detected.IsClean);
        Assert.Equal(4, detected.TotalFindings);
        Assert.Equal(2, detected.For(FindingCategory.MissingCandle));
    }

    [Fact]
    public void DetailedValidationReport_RequiresCompleteChecks()
    {
        var report = CreateValidReport();
        var incompleteChecks = SixChecks();
        incompleteChecks[0] = new CheckExecution(CheckName.MissingCandles, CheckStatus.NotCompleted, "stopped");

        var exception = Assert.Throws<ArgumentException>(() =>
            new DetailedValidationReport(
                report.Source, report.Context, report.Coverage, incompleteChecks, report.Summary, report.Reconciliation, report.Findings));
        Assert.Contains("completed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetailedValidationReport_RejectsStatusMismatchingSummary()
    {
        var report = CreateValidReport();

        Assert.Throws<ArgumentException>(() =>
            new DetailedValidationReport(
                report.Source, report.Context, report.Coverage, report.Checks, report.Summary, report.Reconciliation, report.Findings,
                ReportStatus.FindingsDetected));
    }

    [Fact]
    public void DetailedValidationReport_RejectsCleanStatusForDetectedSummary()
    {
        var report = CreateValidReport();
        var detected = new DetailedSummary(1, 0, 0, 0, 0, 0);
        var reconciliation = new ReportReconciliation(
        [
            new(FindingCategory.MissingCandle, 1, 1, 1),
            new(FindingCategory.DuplicateRecord, 0, 0, 0),
            new(FindingCategory.InvalidOhlc, 0, 0, 0),
            new(FindingCategory.ClosedMarketRecord, 0, 0, 0),
            new(FindingCategory.TimeGap, 0, 0, 0),
            new(FindingCategory.MalformedRow, 0, 0, 0)
        ], report.Coverage);

        Assert.Throws<ArgumentException>(() =>
            new DetailedValidationReport(
                report.Source, report.Context, report.Coverage, report.Checks, detected, reconciliation, report.Findings,
                ReportStatus.Clean));
    }

    [Fact]
    public void DetailedValidationReport_ExposesCompleteInvariants()
    {
        var report = CreateValidReport();

        Assert.Equal(2, report.ContractVersion);
        Assert.True(report.FindingSetComplete);
        Assert.Equal(ReportStatus.Clean, report.Status);
        Assert.Same(report.Source, report.Source);
        Assert.NotNull(report.Findings);
    }

    [Fact]
    public void FatalDiagnostic_RejectsUnknownCode()
    {
        Assert.Throws<ArgumentException>(() =>
            new FatalDiagnostic("UNKNOWN_CODE", "reason", "guidance"));
    }

    [Fact]
    public void FatalDiagnostic_RejectsEmptyReasonOrGuidance()
    {
        Assert.Throws<ArgumentException>(() =>
            new FatalDiagnostic("SOURCE_UNAVAILABLE", " ", "guidance"));
        Assert.Throws<ArgumentException>(() =>
            new FatalDiagnostic("SOURCE_UNAVAILABLE", "reason", " "));
    }

    [Fact]
    public void FatalDiagnostic_ExposesStableClassification()
    {
        var diagnostic = new FatalDiagnostic(
            "INVALID_STRUCTURE", "Rows are inconsistent.", "Check column counts.",
            source: new PartialSourceIdentity("bad.csv", 50, Hex64('e')),
            location: new FailureLocation(sourceLine: 12));

        Assert.Equal(2, diagnostic.ContractVersion);
        Assert.Equal("Fatal", diagnostic.Status);
        Assert.False(diagnostic.FindingSetComplete);
        Assert.Equal(FailureClass.Dataset, diagnostic.FailureClass);
        Assert.Equal(FailureStage.Ingestion, diagnostic.Stage);
        Assert.Equal("INVALID_STRUCTURE", diagnostic.Code);
        Assert.Equal("Rows are inconsistent.", diagnostic.Reason);
        Assert.Equal("Check column counts.", diagnostic.Guidance);
        Assert.Equal("bad.csv", diagnostic.Source!.FileName);
        Assert.Equal(12, diagnostic.Location!.SourceLine);
    }

    [Fact]
    public void FatalDiagnostic_DefaultsToSixNotCompletedChecks()
    {
        var diagnostic = new FatalDiagnostic("SOURCE_UNAVAILABLE", "Missing file.", "Provide the file.");

        Assert.Equal(6, diagnostic.Checks.Count);
        Assert.All(diagnostic.Checks, check => Assert.Equal(CheckStatus.NotCompleted, check.Status));
    }

    [Fact]
    public void FatalDiagnostic_RejectsMalformedChecksList()
    {
        var checks = SixChecks();
        checks.RemoveAt(0);

        Assert.Throws<ArgumentException>(() =>
            new FatalDiagnostic("SOURCE_UNAVAILABLE", "Missing file.", "Provide the file.", checks: checks));
    }

    [Fact]
    public void PartialSourceIdentity_ValidatesFields()
    {
        Assert.Throws<ArgumentException>(() => new PartialSourceIdentity("", null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PartialSourceIdentity("f.csv", -1, null));
        Assert.Throws<ArgumentException>(() => new PartialSourceIdentity("f.csv", 5, "x"));
    }

    [Fact]
    public void FailureLocation_RequiresAtLeastOneValue()
    {
        Assert.Throws<ArgumentException>(() => new FailureLocation(null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FailureLocation(0, null, null));
    }

    [Fact]
    public void FailureLocation_AcceptsEveryKind()
    {
        Assert.Equal(5, new FailureLocation(5, null, null).SourceLine);
        Assert.Equal(Ts, new FailureLocation(null, Ts, null).TimestampUtc);
        Assert.Equal("Open", new FailureLocation(null, null, "Open").Field);
        Assert.Throws<ArgumentException>(() => new FailureLocation(null, new DateTimeOffset(2024, 8, 1, 12, 0, 0, TimeSpan.FromHours(2)), null));
    }

    private static DetailedValidationReport CreateValidReport()
    {
        var coverage = new ScanCoverage(10, 10, 0);
        var calendar = new CalendarContext("forex", "Forex 24-5");
        var timestamp = TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00");
        var context = new ValidationContextSnapshot("H1", calendar, timestamp, "comma", false, null);
        var summary = new DetailedSummary(0, 0, 0, 0, 0, 0);
        var reconciliation = new ReportReconciliation(
        [
            new(FindingCategory.MissingCandle, 0, 0, 0),
            new(FindingCategory.DuplicateRecord, 0, 0, 0),
            new(FindingCategory.InvalidOhlc, 0, 0, 0),
            new(FindingCategory.ClosedMarketRecord, 0, 0, 0),
            new(FindingCategory.TimeGap, 0, 0, 0),
            new(FindingCategory.MalformedRow, 0, 0, 0)
        ], coverage);

        return new DetailedValidationReport(
            Source, context, coverage, SixChecks(), summary, reconciliation, new InMemoryCompletedCatalog());
    }

    internal static List<CheckExecution> SixChecks() =>
    [
        new(CheckName.MissingCandles, CheckStatus.Completed),
        new(CheckName.DuplicateRecords, CheckStatus.Completed),
        new(CheckName.InvalidOhlc, CheckStatus.Completed),
        new(CheckName.ClosedMarketRecords, CheckStatus.Completed),
        new(CheckName.TimeGaps, CheckStatus.Completed),
        new(CheckName.MalformedRows, CheckStatus.Completed)
    ];

    private static string Hex64(char seed) => new(seed, 64);
}