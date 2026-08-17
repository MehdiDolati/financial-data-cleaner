using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Tests.Reporting;
using Validator.Application.Validation;
using Validator.Domain.Findings;

namespace Validator.Application.Tests;

// The refusals that keep a report honest: a diagnostic never claims a source
// fact it did not establish, a fatal code always carries a known class and
// stage, per-category lookups never answer for a category that does not exist,
// and an absent sort value sorts last instead of being invented.
public sealed class GuardBehaviourTests
{
    private static DateTimeOffset Utc(int day = 1, int hour = 10) =>
        new(2024, 8, day, hour, 0, 0, TimeSpan.Zero);

    private static readonly CheckName[] CanonicalChecks =
    [
        CheckName.MissingCandles,
        CheckName.DuplicateRecords,
        CheckName.InvalidOhlc,
        CheckName.ClosedMarketRecords,
        CheckName.TimeGaps,
        CheckName.MalformedRows
    ];

    private static CheckExecution[] CompletedChecks() =>
        [.. CanonicalChecks.Select(check => new CheckExecution(check, CheckStatus.Completed))];

    private static SourceIdentity Source() => new("data.csv", 1024, new string('c', 64));

    private static ValidationContextSnapshot Context() => new(
        "H1",
        new CalendarContext("forex", "Forex 24-5"),
        TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
        "comma",
        false,
        null);

    [Theory]
    [InlineData("sub/data.csv")]
    [InlineData("sub\\data.csv")]
    [InlineData("C:data.csv")]
    public void PartialSourceIdentity_RejectsAnythingButABaseName(string fileName)
    {
        // A diagnostic that echoed a path could disclose where a file lives, and
        // the report only ever needs the name of the source.
        var error = Assert.Throws<ArgumentException>(() => new PartialSourceIdentity(fileName));

        Assert.Equal("fileName", error.ParamName);
    }

    [Fact]
    public void PartialSourceIdentity_RejectsANegativeByteSize()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new PartialSourceIdentity("data.csv", byteSize: -1));

        Assert.Equal("byteSize", error.ParamName);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void PartialSourceIdentity_RejectsAHashThatIsNotLowerCaseHex(string sha256)
    {
        // A hash is how a reader proves which bytes were validated, so a value
        // that is not in the published form is refused rather than reported.
        var error = Assert.Throws<ArgumentException>(
            () => new PartialSourceIdentity("data.csv", sha256: sha256));

        Assert.Equal("sha256", error.ParamName);
    }

    [Fact]
    public void PartialSourceIdentity_KeepsTheFactsItWasGiven()
    {
        var identity = new PartialSourceIdentity(
            "data.csv",
            byteSize: 42,
            sha256: new string('a', 64));

        Assert.Equal("data.csv", identity.FileName);
        Assert.Equal(42, identity.ByteSize);
        Assert.Equal(new string('a', 64), identity.Sha256);
    }

    [Fact]
    public void PartialSourceIdentity_ReportsUnknownFactsAsAbsent()
    {
        // A failure before the size or hash was computed must leave them absent,
        // never zero or empty, which a reader could mistake for a measurement.
        var identity = new PartialSourceIdentity("data.csv");

        Assert.Null(identity.ByteSize);
        Assert.Null(identity.Sha256);
    }

    [Fact]
    public void FailureLocation_RequiresAtLeastOneKnownPosition()
    {
        Assert.Throws<ArgumentException>(() => new FailureLocation());
    }

    [Fact]
    public void FailureLocation_RejectsANonPositiveSourceLine()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new FailureLocation(sourceLine: 0));

        Assert.Equal("sourceLine", error.ParamName);
    }

    [Fact]
    public void FailureLocation_RejectsATimestampThatIsNotUtc()
    {
        var error = Assert.Throws<ArgumentException>(
            () => new FailureLocation(timestampUtc: new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.FromHours(2))));

        Assert.Equal("timestampUtc", error.ParamName);
    }

    [Fact]
    public void FatalCodeRegistry_RefusesToClassifyAnUnpublishedCode()
    {
        // Guessing a class or stage would let an unknown failure masquerade as a
        // known one, so an unregistered code is an error at the boundary.
        Assert.False(FatalCodeRegistry.IsKnown("NOT_A_REAL_CODE"));
        Assert.Throws<ArgumentException>(() => FatalCodeRegistry.ClassOf("NOT_A_REAL_CODE"));
        Assert.Throws<ArgumentException>(() => FatalCodeRegistry.StageOf("NOT_A_REAL_CODE"));
    }

    [Fact]
    public void FatalDiagnostic_RejectsChecksThatAreNotInCanonicalOrder()
    {
        var outOfOrder = new CheckExecution[]
        {
            new(CheckName.DuplicateRecords, CheckStatus.NotCompleted, "Validation did not run."),
            new(CheckName.MissingCandles, CheckStatus.NotCompleted, "Validation did not run."),
            new(CheckName.InvalidOhlc, CheckStatus.NotCompleted, "Validation did not run."),
            new(CheckName.ClosedMarketRecords, CheckStatus.NotCompleted, "Validation did not run."),
            new(CheckName.TimeGaps, CheckStatus.NotCompleted, "Validation did not run."),
            new(CheckName.MalformedRows, CheckStatus.NotCompleted, "Validation did not run.")
        };

        var error = Assert.Throws<ArgumentException>(() => new FatalDiagnostic(
            "INVALID_CSV",
            "The source could not be parsed.",
            "Repair the delimiter and retry.",
            checks: outOfOrder));

        Assert.Equal("checks", error.ParamName);
    }

    [Fact]
    public void FatalDiagnostic_RejectsAPartialListOfChecks()
    {
        var tooFew = new CheckExecution[]
        {
            new(CheckName.MissingCandles, CheckStatus.NotCompleted, "Validation did not run.")
        };

        var error = Assert.Throws<ArgumentException>(() => new FatalDiagnostic(
            "INVALID_CSV",
            "The source could not be parsed.",
            "Repair the delimiter and retry.",
            checks: tooFew));

        Assert.Equal("checks", error.ParamName);
    }

    [Fact]
    public void FatalDiagnostic_RequiresAReasonAndGuidance()
    {
        Assert.Throws<ArgumentException>(() => new FatalDiagnostic("INVALID_CSV", "  ", "Retry."));
        Assert.Throws<ArgumentException>(() => new FatalDiagnostic("INVALID_CSV", "Unparseable.", "  "));
    }

    [Fact]
    public void FatalDiagnostic_RejectsAnUnpublishedCode()
    {
        var error = Assert.Throws<ArgumentException>(
            () => new FatalDiagnostic("NOT_A_REAL_CODE", "Something failed.", "Retry."));

        Assert.Equal("code", error.ParamName);
    }

    [Fact]
    public void FatalDiagnostic_AcceptsChecksThatRanBeforeTheFailure()
    {
        // A failure part-way through must still report which checks completed, so
        // a reader can tell proven-clean categories from unknown ones.
        var checks = new CheckExecution[]
        {
            new(CheckName.MissingCandles, CheckStatus.Completed),
            new(CheckName.DuplicateRecords, CheckStatus.Completed),
            new(CheckName.InvalidOhlc, CheckStatus.NotCompleted, "Validation stopped."),
            new(CheckName.ClosedMarketRecords, CheckStatus.NotCompleted, "Validation stopped."),
            new(CheckName.TimeGaps, CheckStatus.NotCompleted, "Validation stopped."),
            new(CheckName.MalformedRows, CheckStatus.NotCompleted, "Validation stopped.")
        };

        var diagnostic = new FatalDiagnostic(
            "VALIDATION_INCOMPLETE",
            "Validation stopped before completing.",
            "Rerun the validation.",
            checks: checks);

        Assert.Equal(FailureClass.Operational, diagnostic.FailureClass);
        Assert.Equal(FailureStage.Validation, diagnostic.Stage);
        Assert.False(diagnostic.FindingSetComplete);
        Assert.Equal(CheckStatus.Completed, diagnostic.Checks[0].Status);
    }

    [Fact]
    public void FatalDiagnostic_DefaultsEveryCheckToNotCompleted()
    {
        var diagnostic = new FatalDiagnostic(
            "SOURCE_UNAVAILABLE",
            "The source could not be opened.",
            "Check the path and retry.");

        Assert.Equal(6, diagnostic.Checks.Count);
        Assert.All(diagnostic.Checks, check => Assert.Equal(CheckStatus.NotCompleted, check.Status));
        Assert.Equal(CanonicalChecks, [.. diagnostic.Checks.Select(check => check.Check)]);
    }

    [Fact]
    public void DetailedSummary_RefusesToAnswerForACategoryThatDoesNotExist()
    {
        var summary = new DetailedSummary(1, 2, 3, 4, 5, 6);

        Assert.Throws<ArgumentOutOfRangeException>(() => summary.For((FindingCategory)999));
    }

    [Fact]
    public void DetailedSummary_RejectsANegativeCount()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new DetailedSummary(-1, 0, 0, 0, 0, 0));

        Assert.Equal("missingCandles", error.ParamName);
    }

    [Fact]
    public void CategoryStatistics_RejectsNegativeTotals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CategoryStatistics(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CategoryStatistics(0, -1));
    }

    [Fact]
    public void FindingCatalogStatistics_RequiresEveryCategory()
    {
        // Statistics reconcile the report's summary, so a missing category would
        // leave a total that nothing proves.
        var zero = new CategoryStatistics(0, 0);

        Assert.Throws<ArgumentNullException>(() => new FindingCatalogStatistics(null!, zero, zero, zero, zero, zero));
        Assert.Throws<ArgumentNullException>(() => new FindingCatalogStatistics(zero, null!, zero, zero, zero, zero));
        Assert.Throws<ArgumentNullException>(() => new FindingCatalogStatistics(zero, zero, null!, zero, zero, zero));
        Assert.Throws<ArgumentNullException>(() => new FindingCatalogStatistics(zero, zero, zero, null!, zero, zero));
        Assert.Throws<ArgumentNullException>(() => new FindingCatalogStatistics(zero, zero, zero, zero, null!, zero));
        Assert.Throws<ArgumentNullException>(() => new FindingCatalogStatistics(zero, zero, zero, zero, zero, null!));
    }

    [Fact]
    public void FindingCatalogStatistics_AnswersForEveryEstablishedCategoryAndNoOther()
    {
        var statistics = new FindingCatalogStatistics(
            new CategoryStatistics(1, 1),
            new CategoryStatistics(2, 4),
            new CategoryStatistics(3, 3),
            new CategoryStatistics(4, 4),
            new CategoryStatistics(5, 5),
            new CategoryStatistics(6, 6));

        Assert.Equal(1, statistics.For(FindingCategory.MissingCandle).EntryCount);
        Assert.Equal(4, statistics.For(FindingCategory.DuplicateRecord).ContributionSum);
        Assert.Equal(3, statistics.For(FindingCategory.InvalidOhlc).EntryCount);
        Assert.Equal(4, statistics.For(FindingCategory.ClosedMarketRecord).EntryCount);
        Assert.Equal(5, statistics.For(FindingCategory.TimeGap).EntryCount);
        Assert.Equal(6, statistics.For(FindingCategory.MalformedRow).EntryCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => statistics.For((FindingCategory)999));
    }

    [Fact]
    public void CanonicalFindingOrder_SortsAFindingWithoutATimestampAfterOneWithIt()
    {
        // An absent instant is not a position, so it sorts last within its
        // category rather than being replaced by an invented value.
        var timed = Header(FindingCategory.MalformedRow, Utc(), sourceLine: 5);
        var untimed = Header(FindingCategory.MalformedRow, timestampUtc: null, sourceLine: 9);

        Assert.True(CanonicalFindingOrder.Instance.Compare(timed, untimed) < 0);
        Assert.True(CanonicalFindingOrder.Instance.Compare(untimed, timed) > 0);
        Assert.Equal(0, CanonicalFindingOrder.Instance.Compare(untimed, untimed));
    }

    [Fact]
    public void CanonicalFindingOrder_TreatsAnAbsentHeaderAsSortingFirst()
    {
        var header = Header(FindingCategory.MalformedRow, Utc(), sourceLine: 5);

        Assert.True(CanonicalFindingOrder.Instance.Compare(null, header) < 0);
        Assert.True(CanonicalFindingOrder.Instance.Compare(header, null) > 0);
        Assert.Equal(0, CanonicalFindingOrder.Instance.Compare(null, null));
    }

    [Fact]
    public void CanonicalFindingOrder_RefusesToKeyAnAbsentHeader()
    {
        Assert.Throws<ArgumentNullException>(() => CanonicalFindingOrder.SortKey(null!));
    }

    [Fact]
    public void CanonicalFindingOrder_KeysAnAbsentValueAsTheHighestSegment()
    {
        // The spool sorts by this text, so an absent value must key higher than
        // any real one for the text order to match the comparer's order.
        var timed = CanonicalFindingOrder.SortKey(Header(FindingCategory.MalformedRow, Utc(), sourceLine: 5));
        var untimed = CanonicalFindingOrder.SortKey(Header(FindingCategory.MalformedRow, null, sourceLine: 9));

        Assert.True(string.CompareOrdinal(timed, untimed) < 0);
    }

    [Fact]
    public void DetailedValidationReport_RejectsAChecklistThatIsNotComplete()
    {
        // A successful report asserts every check ran; allowing a NotCompleted
        // check would let a partial run publish complete-looking totals.
        var checks = CompletedChecks();
        checks[2] = new CheckExecution(CheckName.InvalidOhlc, CheckStatus.NotCompleted, "Validation stopped.");

        var error = Assert.Throws<ArgumentException>(() => new DetailedValidationReport(
            Source(),
            Context(),
            new ScanCoverage(10, 10, 0),
            checks,
            summary: null!,
            reconciliation: null!,
            findings: null!,
            ReportStatus.Clean));

        Assert.Equal("checks", error.ParamName);
    }

    [Fact]
    public void DetailedValidationReport_RejectsChecksThatAreNotInCanonicalOrder()
    {
        var checks = CompletedChecks();
        (checks[0], checks[1]) = (checks[1], checks[0]);

        var error = Assert.Throws<ArgumentException>(() => new DetailedValidationReport(
            Source(),
            Context(),
            new ScanCoverage(10, 10, 0),
            checks,
            summary: null!,
            reconciliation: null!,
            findings: null!,
            ReportStatus.Clean));

        Assert.Equal("checks", error.ParamName);
    }

    [Fact]
    public void DetailedValidationReport_RequiresTheSourceItValidated()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new DetailedValidationReport(
            source: null!,
            context: null!,
            coverage: null!,
            checks: null!,
            summary: null!,
            reconciliation: null!,
            findings: null!,
            ReportStatus.Clean));

        Assert.Equal("source", error.ParamName);
    }

    [Fact]
    public void DetailedValidationReport_RequiresTheContextItValidatedUnder()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new DetailedValidationReport(
            Source(),
            context: null!,
            coverage: null!,
            checks: null!,
            summary: null!,
            reconciliation: null!,
            findings: null!,
            ReportStatus.Clean));

        Assert.Equal("context", error.ParamName);
    }

    [Fact]
    public void DetailedValidationReport_RequiresCoverage()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new DetailedValidationReport(
            Source(),
            Context(),
            coverage: null!,
            checks: null!,
            summary: null!,
            reconciliation: null!,
            findings: null!,
            ReportStatus.Clean));

        Assert.Equal("coverage", error.ParamName);
    }

    [Fact]
    public void DetailedValidationReport_RequiresAChecklist()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new DetailedValidationReport(
            Source(),
            Context(),
            new ScanCoverage(10, 10, 0),
            checks: null!,
            summary: null!,
            reconciliation: null!,
            findings: null!,
            ReportStatus.Clean));

        Assert.Equal("checks", error.ParamName);
    }

    [Fact]
    public void DetailedValidationReport_RequiresASummary()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new DetailedValidationReport(
            Source(),
            Context(),
            new ScanCoverage(10, 10, 0),
            CompletedChecks(),
            summary: null!,
            reconciliation: null!,
            findings: null!,
            ReportStatus.Clean));

        Assert.Equal("summary", error.ParamName);
    }

    [Fact]
    public void DetailedValidationReport_RequiresReconciliationAndFindings()
    {
        // A report is only trustworthy if it carries the reconciliation that
        // proves its totals and the catalog those totals were counted from.
        var coverage = new ScanCoverage(10, 10, 0);
        var summary = new DetailedSummary(0, 0, 0, 0, 0, 0);

        var missingReconciliation = Assert.Throws<ArgumentNullException>(() => new DetailedValidationReport(
            Source(),
            Context(),
            coverage,
            CompletedChecks(),
            summary,
            reconciliation: null!,
            findings: null!,
            ReportStatus.Clean));

        Assert.Equal("reconciliation", missingReconciliation.ParamName);

        var reconciliation = new ReportReconciliation(CleanCategories(), coverage);

        var missingFindings = Assert.Throws<ArgumentNullException>(() => new DetailedValidationReport(
            Source(),
            Context(),
            coverage,
            CompletedChecks(),
            summary,
            reconciliation,
            findings: null!,
            ReportStatus.Clean));

        Assert.Equal("findings", missingFindings.ParamName);
    }

    [Fact]
    public void DetailedValidationReport_RejectsAStatusThatContradictsItsCounts()
    {
        // Status is derived from the counts, so a clean status over non-zero
        // counts would be a claim the report itself disproves.
        var coverage = new ScanCoverage(10, 10, 0);

        var error = Assert.Throws<ArgumentException>(() => new DetailedValidationReport(
            Source(),
            Context(),
            coverage,
            CompletedChecks(),
            new DetailedSummary(1, 0, 0, 0, 0, 0),
            new ReportReconciliation(CleanCategories(), coverage),
            new InMemoryCompletedCatalog(),
            ReportStatus.Clean));

        Assert.Equal("status", error.ParamName);
    }

    [Fact]
    public void DetailedValidationReport_DerivesACleanStatusFromZeroCounts()
    {
        var coverage = new ScanCoverage(10, 10, 0);

        var report = new DetailedValidationReport(
            Source(),
            Context(),
            coverage,
            CompletedChecks(),
            new DetailedSummary(0, 0, 0, 0, 0, 0),
            new ReportReconciliation(CleanCategories(), coverage),
            new InMemoryCompletedCatalog());

        Assert.Equal(ReportStatus.Clean, report.Status);
        Assert.Equal(2, report.ContractVersion);
        Assert.True(report.FindingSetComplete);
    }

    private static List<CategoryReconciliation> CleanCategories() =>
    [
        new(FindingCategory.MissingCandle, 0, 0, 0),
        new(FindingCategory.DuplicateRecord, 0, 0, 0),
        new(FindingCategory.InvalidOhlc, 0, 0, 0),
        new(FindingCategory.ClosedMarketRecord, 0, 0, 0),
        new(FindingCategory.TimeGap, 0, 0, 0),
        new(FindingCategory.MalformedRow, 0, 0, 0)
    ];

    private static DetailedFindingHeader Header(
        FindingCategory category,
        DateTimeOffset? timestampUtc,
        long? sourceLine) =>
        new(
            FindingReferenceFactory.PhysicalRecord(category, sourceLine!.Value),
            category,
            "Finding",
            "A finding was detected.",
            1,
            new FindingLocation(sourceLine.HasValue ? [sourceLine.Value] : null, timestampUtc),
            DetailedFindingHeader.EvidenceKindOf(category),
            "Review the source rows.");
}
