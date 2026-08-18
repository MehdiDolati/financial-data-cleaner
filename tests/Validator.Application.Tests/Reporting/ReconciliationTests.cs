using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Domain.Findings;

namespace Validator.Application.Tests.Reporting;

public sealed class ReconciliationTests
{
    [Theory]
    [InlineData(-1L)]
    public void CategoryReconciliation_RejectsNegativeCounts(long invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CategoryReconciliation(FindingCategory.TimeGap, invalid, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CategoryReconciliation(FindingCategory.TimeGap, 0, invalid, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CategoryReconciliation(FindingCategory.TimeGap, 0, 0, invalid));
    }

    [Fact]
    public void CategoryReconciliation_RejectsUnestablishedCategory()
    {
        Assert.Throws<ArgumentException>(() =>
            new CategoryReconciliation(FindingCategory.Critical, 0, 0, 0));
    }

    [Fact]
    public void CategoryReconciliation_RequiresSummaryEqualsContributionSum()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new CategoryReconciliation(FindingCategory.DuplicateRecord, summaryCount: 3, entryCount: 2, contributionSum: 4));
        Assert.Contains("contribution", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CategoryReconciliation_AcceptsEqualCountsWithDifferentEntryCounts()
    {
        var reconciliation = new CategoryReconciliation(
            FindingCategory.DuplicateRecord,
            summaryCount: 4,
            entryCount: 2,
            contributionSum: 4);

        Assert.Equal(FindingCategory.DuplicateRecord, reconciliation.Category);
        Assert.Equal(4, reconciliation.SummaryCount);
        Assert.Equal(2, reconciliation.EntryCount);
        Assert.Equal(4, reconciliation.ContributionSum);
    }

    [Fact]
    public void ReportReconciliation_RequiresSixCanonicalCategories()
    {
        var coverage = new ScanCoverage(10, 8, 2);
        var single = new[]
        {
            new CategoryReconciliation(FindingCategory.MissingCandle, 0, 0, 0)
        };

        Assert.Throws<ArgumentException>(() => new ReportReconciliation(single, coverage));
    }

    [Fact]
    public void ReportReconciliation_RejectsDuplicateOrOutOfOrderCategories()
    {
        var coverage = new ScanCoverage(10, 8, 2);
        var outOfOrder = SixCategories();
        (outOfOrder[0], outOfOrder[1]) = (outOfOrder[1], outOfOrder[0]);

        Assert.Throws<ArgumentException>(() => new ReportReconciliation(outOfOrder, coverage));
    }

    [Fact]
    public void ReportReconciliation_RejectsUnreconciledCoverage()
    {
        var categories = SixCategories();
        var brokenCoverage = new ScanCoverage(10, 8, 3);

        var exception = Assert.Throws<ArgumentException>(() => new ReportReconciliation(categories, brokenCoverage));
        Assert.Contains("coverage", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportReconciliation_AcceptsReconciledInput()
    {
        var categories = SixCategories();
        var coverage = new ScanCoverage(10, 8, 2);

        var reconciliation = new ReportReconciliation(categories, coverage);

        Assert.True(reconciliation.CoverageReconciled);
        Assert.Equal(6, reconciliation.Categories.Count);
        Assert.Equal(FindingCategory.MissingCandle, reconciliation.Categories[0].Category);
        Assert.Equal(FindingCategory.MalformedRow, reconciliation.Categories[5].Category);
    }

    [Fact]
    public void CategoryCounters_MaintainConstantSizeTotals()
    {
        var counters = new CategoryCounters();
        counters.Add(FindingCategory.DuplicateRecord, contribution: 2);
        counters.Add(FindingCategory.DuplicateRecord, contribution: 1);
        counters.Add(FindingCategory.MissingCandle, contribution: 1);

        var snapshot = counters.Snapshot();

        Assert.Equal(2, snapshot.DuplicateRecords.EntryCount);
        Assert.Equal(3, snapshot.DuplicateRecords.ContributionSum);
        Assert.Equal(1, snapshot.MissingCandles.EntryCount);
        Assert.Equal(1, snapshot.MissingCandles.ContributionSum);
        Assert.Equal(0, snapshot.InvalidOhlc.EntryCount);
        Assert.Equal(0, snapshot.InvalidOhlc.ContributionSum);
    }

    [Fact]
    public void CategoryCounters_RejectsNonPositiveContribution()
    {
        var counters = new CategoryCounters();

        Assert.Throws<ArgumentOutOfRangeException>(() => counters.Add(FindingCategory.TimeGap, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.Add(FindingCategory.TimeGap, -1));
    }

    [Fact]
    public void FindingCatalogStatistics_ForMapsCategories()
    {
        var statistics = new FindingCatalogStatistics(
            new CategoryStatistics(1, 1),
            new CategoryStatistics(2, 3),
            new CategoryStatistics(0, 0),
            new CategoryStatistics(0, 0),
            new CategoryStatistics(1, 1),
            new CategoryStatistics(0, 0));

        Assert.Equal(1, statistics.For(FindingCategory.MissingCandle).EntryCount);
        Assert.Equal(3, statistics.For(FindingCategory.DuplicateRecord).ContributionSum);
        Assert.Equal(0, statistics.For(FindingCategory.InvalidOhlc).EntryCount);
        Assert.Equal(1, statistics.For(FindingCategory.TimeGap).EntryCount);
    }

    [Fact]
    public void CategoryStatistics_RejectsNegativeCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CategoryStatistics(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CategoryStatistics(0, -1));
    }

    private static List<CategoryReconciliation> SixCategories() =>
    [
        new(FindingCategory.MissingCandle, 0, 0, 0),
        new(FindingCategory.DuplicateRecord, 0, 0, 0),
        new(FindingCategory.InvalidOhlc, 0, 0, 0),
        new(FindingCategory.ClosedMarketRecord, 0, 0, 0),
        new(FindingCategory.TimeGap, 0, 0, 0),
        new(FindingCategory.MalformedRow, 0, 0, 0)
    ];
}