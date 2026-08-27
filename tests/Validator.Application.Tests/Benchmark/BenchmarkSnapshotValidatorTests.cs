using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Benchmark;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;
using Xunit;

namespace Validator.Application.Tests.Benchmark
{
    public class BenchmarkSnapshotValidatorTests
    {
        [Fact]
        public void Validate_NullReport_ReturnsError()
        {
            Assert.NotNull(BenchmarkSnapshotValidator.Validate(null!));
        }

        [Fact]
        public void Validate_NullScore_ReturnsError()
        {
            // DetailedValidationReport with no Score (init-only default = null)
            var report = CreateReportWithoutScore();
            var result = BenchmarkSnapshotValidator.Validate(report);
            Assert.NotNull(result);
            Assert.Contains("scoring", result);
        }

        [Fact]
        public void Validate_ValidReport_ReturnsNull()
        {
            Assert.Null(BenchmarkSnapshotValidator.Validate(CreateReport()));
        }

        /// <summary>
        /// Creates a valid report with Score set — validator should accept it.
        /// </summary>
        private static DetailedValidationReport CreateReport()
        {
            var source = new SourceIdentity("test.csv", 100, "abc123def456abc123def456abc123def456abc123def456abc123def456abcd");
            var context = new ValidationContextSnapshot(
                "D1", new CalendarContext("forex", "Forex"),
                TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                "comma", false, null);
            var coverage = new ScanCoverage(10, 10, 0);

            var metrics = MetricPopulationMap.CanonicalOrder.Select(cat =>
                MetricScoreCalculator.ScoreMetric(cat, 0, 10, MetricPopulationMap.KindFor(cat))).ToList();
            var weighting = ScoreWeightResolver.Default();
            var ds = DatasetScore.Available(new ScoreValue(new ExactRatio(100, 1)),
                MetricPopulationMap.CanonicalOrder.ToList(), new List<ExcludedMetric>());
            var score = new DatasetScoreReport(metrics, weighting, ds);

            var cats = MetricPopulationMap.CanonicalOrder.Select(cat =>
                new CategoryReconciliation(cat, 0, 0, 0)).ToList();
            var reconciliation = new ReportReconciliation(cats, coverage);

            return new DetailedValidationReport(
                source, context, coverage, ValidChecks(),
                new DetailedSummary(0, 0, 0, 0, 0, 0),
                reconciliation, EmptyCatalog()) { Score = score };
        }

        /// <summary>
        /// Creates a report without Score to test validator rejection.
        /// </summary>
        private static DetailedValidationReport CreateReportWithoutScore()
        {
            var source = new SourceIdentity("test.csv", 100, "abc123def456abc123def456abc123def456abc123def456abc123def456abcd");
            var context = new ValidationContextSnapshot(
                "D1", new CalendarContext("forex", "Forex"),
                TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                "comma", false, null);
            var coverage = new ScanCoverage(10, 10, 0);

            var cats = MetricPopulationMap.CanonicalOrder.Select(cat =>
                new CategoryReconciliation(cat, 0, 0, 0)).ToList();
            var reconciliation = new ReportReconciliation(cats, coverage);

            // Score not set — validator should reject
            return new DetailedValidationReport(
                source, context, coverage, ValidChecks(),
                new DetailedSummary(0, 0, 0, 0, 0, 0),
                reconciliation, EmptyCatalog());
        }

        private static CheckExecution[] ValidChecks() => new[]
        {
            new CheckExecution(CheckName.MissingCandles, CheckStatus.Completed),
            new CheckExecution(CheckName.DuplicateRecords, CheckStatus.Completed),
            new CheckExecution(CheckName.InvalidOhlc, CheckStatus.Completed),
            new CheckExecution(CheckName.ClosedMarketRecords, CheckStatus.Completed),
            new CheckExecution(CheckName.TimeGaps, CheckStatus.Completed),
            new CheckExecution(CheckName.MalformedRows, CheckStatus.Completed)
        };

        private static ICompletedFindingCatalog EmptyCatalog() => new StubCatalog();

        private sealed class StubCatalog : ICompletedFindingCatalog
        {
            private static readonly CategoryStatistics Zero = new(0, 0);
            public FindingCatalogStatistics Statistics => new(Zero, Zero, Zero, Zero, Zero, Zero);
            public IAsyncEnumerable<IDetailedFindingCursor> ReadCanonicalAsync(CancellationToken ct = default)
                => AsyncEnumerable.Empty<IDetailedFindingCursor>();
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
