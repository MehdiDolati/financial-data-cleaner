using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Validator.Application.Benchmark;
using Validator.Application.Comparison;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Abstractions;
using Validator.Application.Scoring;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;
using Xunit;

namespace Validator.Application.Tests.Benchmark
{
    public class BenchmarkCoverageTests
    {
        [Fact]
        public void BenchmarkSnapshot_AllProperties_SetCorrectly()
        {
            var snapshot = CreateSnapshot();
            Assert.Equal("test-benchmark", snapshot.Name);
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), snapshot.EstablishedAtUtc);
            Assert.NotNull(snapshot.Source);
            Assert.NotNull(snapshot.Context);
            Assert.NotNull(snapshot.Coverage);
            Assert.Equal(6, snapshot.Checks.Count);
            Assert.Equal(6, snapshot.Metrics.Count);
            Assert.NotNull(snapshot.Dataset);
            Assert.NotNull(snapshot.Weighting);
        }

        [Fact]
        public void BenchmarkSnapshot_NullName_Throws()
        {
            var source = new SourceIdentity("test.csv", 100, "abc123def456abc123def456abc123def456abc123def456abc123def456abcd");
            var context = CreateContext();

            Assert.Throws<ArgumentException>(() => new BenchmarkSnapshot(
                name: "",
                establishedAtUtc: DateTimeOffset.UtcNow,
                source: source,
                context: context,
                coverage: new ScanCoverage(10, 10, 0),
                checks: Array.Empty<CheckExecution>(),
                metrics: Array.Empty<MetricScore>(),
                dataset: DatasetScore.Unavailable("test", MetricPopulationMap.CanonicalOrder.ToList(), new List<ExcludedMetric>()),
                weighting: ScoreWeightResolver.Default()));
        }

        [Fact]
        public void BenchmarkSnapshot_NullSource_Throws()
        {
            var context = CreateContext();

            Assert.Throws<ArgumentNullException>(() => new BenchmarkSnapshot(
                name: "test",
                establishedAtUtc: DateTimeOffset.UtcNow,
                source: null!,
                context: context,
                coverage: new ScanCoverage(10, 10, 0),
                checks: Array.Empty<CheckExecution>(),
                metrics: Array.Empty<MetricScore>(),
                dataset: DatasetScore.Unavailable("test", MetricPopulationMap.CanonicalOrder.ToList(), new List<ExcludedMetric>()),
                weighting: ScoreWeightResolver.Default()));
        }

        [Fact]
        public void BenchmarkSnapshot_NullContext_Throws()
        {
            var source = new SourceIdentity("test.csv", 100, "abc123def456abc123def456abc123def456abc123def456abc123def456abcd");

            Assert.Throws<ArgumentNullException>(() => new BenchmarkSnapshot(
                name: "test",
                establishedAtUtc: DateTimeOffset.UtcNow,
                source: source,
                context: null!,
                coverage: new ScanCoverage(10, 10, 0),
                checks: Array.Empty<CheckExecution>(),
                metrics: Array.Empty<MetricScore>(),
                dataset: DatasetScore.Unavailable("test", MetricPopulationMap.CanonicalOrder.ToList(), new List<ExcludedMetric>()),
                weighting: ScoreWeightResolver.Default()));
        }

        [Fact]
        public void BenchmarkSnapshotValidator_NullReport_ReturnsError()
        {
            var result = BenchmarkSnapshotValidator.Validate(null!);
            Assert.NotNull(result);
        }

        [Fact]
        public void BenchmarkSnapshotValidator_NullScore_ReturnsError()
        {
            var report = CreateReportWithoutScore();
            var result = BenchmarkSnapshotValidator.Validate(report);
            Assert.NotNull(result);
            Assert.Contains("scoring", result);
        }

        [Fact]
        public void BenchmarkSnapshotValidator_ValidReport_ReturnsNull()
        {
            var report = CreateReport();
            var result = BenchmarkSnapshotValidator.Validate(report);
            Assert.Null(result);
        }

        [Fact]
        public void CandidateIdentity_AllProperties_AreAccessible()
        {
            var source = new SourceIdentity("candidate.csv", 200, "abc123def456abc123def456abc123def456abc123def456abc123def456abcd");
            var identity = new CandidateIdentity(source, CreateContext());

            Assert.NotNull(identity.Source);
            Assert.NotNull(identity.Context);
            Assert.Equal("candidate.csv", identity.Source.FileName);
            Assert.Equal(200, identity.Source.ByteSize);
        }

        [Fact]
        public void CandidateIdentity_NullSource_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CandidateIdentity(null!, CreateContext()));
        }

        [Fact]
        public void CandidateIdentity_NullContext_Throws()
        {
            var source = new SourceIdentity("test.csv", 100, "abc123def456abc123def456abc123def456abc123def456abc123def456abcd");
            Assert.Throws<ArgumentNullException>(() => new CandidateIdentity(source, null!));
        }

        private static ValidationContextSnapshot CreateContext()
        {
            return new ValidationContextSnapshot(
                "D1",
                new CalendarContext("forex", "Forex"),
                TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                "comma", false, null);
        }

        private static CheckExecution[] CanonicalChecks() => new[]
        {
            new CheckExecution(CheckName.MissingCandles, CheckStatus.Completed),
            new CheckExecution(CheckName.DuplicateRecords, CheckStatus.Completed),
            new CheckExecution(CheckName.InvalidOhlc, CheckStatus.Completed),
            new CheckExecution(CheckName.ClosedMarketRecords, CheckStatus.Completed),
            new CheckExecution(CheckName.TimeGaps, CheckStatus.Completed),
            new CheckExecution(CheckName.MalformedRows, CheckStatus.Completed)
        };

        private static BenchmarkSnapshot CreateSnapshot()
        {
            var source = new SourceIdentity("test.csv", 100, "abc123def456abc123def456abc123def456abc123def456abc123def456abcd");
            var metrics = MetricPopulationMap.CanonicalOrder.Select(cat =>
                MetricScoreCalculator.ScoreMetric(cat, 0, 10, MetricPopulationMap.KindFor(cat))
            ).ToList();
            var weighting = ScoreWeightResolver.Default();
            var datasetScore = DatasetScore.Available(
                new ScoreValue(new ExactRatio(100, 1)),
                MetricPopulationMap.CanonicalOrder.ToList(),
                new List<ExcludedMetric>());

            return new BenchmarkSnapshot(
                name: "test-benchmark",
                establishedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                source: source,
                context: CreateContext(),
                coverage: new ScanCoverage(10, 10, 0),
                checks: CanonicalChecks(),
                metrics: metrics,
                dataset: datasetScore,
                weighting: weighting);
        }

        private static DetailedValidationReport CreateReport()
        {
            var source = new SourceIdentity("test.csv", 100, "abc123def456abc123def456abc123def456abc123def456abc123def456abcd");
            var coverage = new ScanCoverage(10, 10, 0);
            var categories = MetricPopulationMap.CanonicalOrder.Select(cat =>
                new CategoryReconciliation(cat, 0, 0, 0)).ToList();
            var reconciliation = new ReportReconciliation(categories, coverage);

            var metrics = MetricPopulationMap.CanonicalOrder.Select(cat =>
                MetricScoreCalculator.ScoreMetric(cat, 0, 10, MetricPopulationMap.KindFor(cat))
            ).ToList();
            var weighting = ScoreWeightResolver.Default();
            var datasetScore = DatasetScore.Available(
                new ScoreValue(new ExactRatio(100, 1)),
                MetricPopulationMap.CanonicalOrder.ToList(),
                new List<ExcludedMetric>());
            var score = new DatasetScoreReport(metrics, weighting, datasetScore);

            return new DetailedValidationReport(
                source, CreateContext(), coverage, CanonicalChecks(),
                new DetailedSummary(0, 0, 0, 0, 0, 0),
                reconciliation, EmptyCatalog())
            {
                Score = score
            };
        }

        private static DetailedValidationReport CreateReportWithoutScore()
        {
            var source = new SourceIdentity("test.csv", 100, "abc123def456abc123def456abc123def456abc123def456abc123def456abcd");
            var coverage = new ScanCoverage(10, 10, 0);
            var categories = MetricPopulationMap.CanonicalOrder.Select(cat =>
                new CategoryReconciliation(cat, 0, 0, 0)).ToList();
            var reconciliation = new ReportReconciliation(categories, coverage);

            return new DetailedValidationReport(
                source, CreateContext(), coverage, CanonicalChecks(),
                new DetailedSummary(0, 0, 0, 0, 0, 0),
                reconciliation, EmptyCatalog());
            // Score is intentionally not set (null by default)
        }

        // --- Tests for unreachable validator branches via uninitialized object + reflection ---

        private static readonly SourceIdentity DummySource = new("test.csv", 100, "abc123def456abc123def456abc123def456abc123def456abc123def456abcd");
        private static readonly ValidationContextSnapshot DummyContext = CreateContext();
        private static readonly ScanCoverage DummyCoverage = new(10, 10, 0);

        private static DetailedValidationReport CreateUninitializedReport()
            => (DetailedValidationReport)RuntimeHelpers.GetUninitializedObject(typeof(DetailedValidationReport));

        private static void SetProperty(object obj, string name, object? value)
        {
            var type = obj.GetType();
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            // Try property first
            var prop = type.GetProperty(name, flags);
            if (prop?.GetSetMethod(true) is not null)
            {
                prop.SetValue(obj, value);
                return;
            }
            // Try backing field for auto-properties/records
            var field = type.GetField($"<{name}>k__BackingField", flags)
                        ?? type.GetField(name, flags);
            field?.SetValue(obj, value);
        }

        [Fact]
        public void Validator_NullSource_ReturnsError_Bypass()
        {
            var report = CreateUninitializedReport();
            var result = BenchmarkSnapshotValidator.Validate(report);
            Assert.Contains("source identity", result!);
        }

        [Fact]
        public void Validator_NullContext_ReturnsError_Bypass()
        {
            var report = CreateUninitializedReport();
            SetProperty(report, "Source", DummySource);
            var result = BenchmarkSnapshotValidator.Validate(report);
            Assert.Contains("validation context", result!);
        }

        [Fact]
        public void Validator_NullChecks_ReturnsError_Bypass()
        {
            var report = CreateUninitializedReport();
            SetProperty(report, "Source", DummySource);
            SetProperty(report, "Context", DummyContext);
            // Checks is null (default on uninitialized)
            var result = BenchmarkSnapshotValidator.Validate(report);
            Assert.Contains("six check results", result!);
        }

        [Fact]
        public void Validator_WrongCheckCount_ReturnsError_Bypass()
        {
            var report = CreateUninitializedReport();
            SetProperty(report, "Source", DummySource);
            SetProperty(report, "Context", DummyContext);
            SetProperty(report, "Checks", new[] { new CheckExecution(CheckName.MissingCandles, CheckStatus.Completed) });
            var result = BenchmarkSnapshotValidator.Validate(report);
            Assert.Contains("six check results", result!);
        }

        [Fact]
        public void Validator_IncompleteCheck_ReturnsError_Bypass()
        {
            var report = CreateUninitializedReport();
            SetProperty(report, "Source", DummySource);
            SetProperty(report, "Context", DummyContext);
            SetProperty(report, "Checks", new[]
            {
                new CheckExecution(CheckName.MissingCandles, CheckStatus.Completed),
                new CheckExecution(CheckName.DuplicateRecords, CheckStatus.NotCompleted, "skipped"),
                new CheckExecution(CheckName.InvalidOhlc, CheckStatus.Completed),
                new CheckExecution(CheckName.ClosedMarketRecords, CheckStatus.Completed),
                new CheckExecution(CheckName.TimeGaps, CheckStatus.Completed),
                new CheckExecution(CheckName.MalformedRows, CheckStatus.Completed)
            });
            var result = BenchmarkSnapshotValidator.Validate(report);
            Assert.Contains("completed", result!);
        }

        [Fact]
        public void Validator_NullDataset_ReturnsError_Bypass()
        {
            var report = CreateUninitializedReport();
            SetProperty(report, "Source", DummySource);
            SetProperty(report, "Context", DummyContext);
            SetProperty(report, "Checks", CanonicalChecks());
            // Score with null Dataset - set Score but leave Dataset null
            var metrics = MetricPopulationMap.CanonicalOrder.Select(cat =>
                MetricScoreCalculator.ScoreMetric(cat, 0, 10, MetricPopulationMap.KindFor(cat))
            ).ToList();
            var weighting = ScoreWeightResolver.Default();
            var ds = DatasetScore.Available(
                new ScoreValue(new ExactRatio(100, 1)),
                MetricPopulationMap.CanonicalOrder.ToList(),
                new List<ExcludedMetric>());
            var score = new DatasetScoreReport(metrics, weighting, ds);
            // Set Score, then null out Dataset via reflection on the score report
            SetProperty(report, "Score", score);
            SetProperty(score, "Dataset", null!);
            var result = BenchmarkSnapshotValidator.Validate(report);
            Assert.Contains("dataset score", result!);
        }

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
