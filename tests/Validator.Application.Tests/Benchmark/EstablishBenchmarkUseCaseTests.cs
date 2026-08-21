using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Benchmark;
using Validator.Application.Comparison;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;
using Xunit;

namespace Validator.Application.Tests.Benchmark
{
    public class EstablishBenchmarkUseCaseTests
    {
        [Fact]
        public async Task ExecuteAsync_ValidReport_CreatesSnapshot()
        {
            var store = new FakeBenchmarkStore();
            var useCase = new EstablishBenchmarkUseCase(store);
            var report = CreateValidReport();
            var tmpFile = Path.GetTempFileName();

            try
            {
                var snapshot = await useCase.ExecuteAsync(report, "My Benchmark", tmpFile);
                Assert.Equal("my-benchmark", snapshot.Name);
                Assert.NotNull(snapshot.Source);
                Assert.Single(store.SavedSnapshots);
                Assert.Equal("my-benchmark", store.SavedSnapshots[0].Name);
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [Fact]
        public async Task ExecuteAsync_NameCollision_Throws()
        {
            var store = new FakeBenchmarkStore { ExistsResult = true };
            var useCase = new EstablishBenchmarkUseCase(store);
            var report = CreateValidReport();
            var tmpFile = Path.GetTempFileName();

            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => useCase.ExecuteAsync(report, "existing", tmpFile));
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [Fact]
        public async Task ExecuteAsync_NullSourceFile_Throws()
        {
            var store = new FakeBenchmarkStore();
            var useCase = new EstablishBenchmarkUseCase(store);
            var report = CreateValidReport();

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => useCase.ExecuteAsync(report, "test", "/nonexistent/file.csv"));
        }

        [Fact]
        public async Task ExecuteAsync_NullReport_ThrowsInvalidOperation()
        {
            var store = new FakeBenchmarkStore();
            var useCase = new EstablishBenchmarkUseCase(store);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => useCase.ExecuteAsync(null!, "test", "dummy.csv"));
        }

        [Fact]
        public void Constructor_NullStore_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new EstablishBenchmarkUseCase(null!));
        }

        [Fact]
        public void BenchmarkName_DerivesSafeName()
        {
            var name = new BenchmarkName("My Benchmark!");
            Assert.Equal("my-benchmark", name.Safe);
        }

        [Fact]
        public void BenchmarkName_RejectsPathSeparators()
        {
            Assert.Throws<ArgumentException>(() => new BenchmarkName("test/../../../etc/passwd"));
        }

        [Fact]
        public void BenchmarkName_Equality()
        {
            var a = new BenchmarkName("Test Benchmark");
            var b = new BenchmarkName("Test Benchmark");
            Assert.Equal(a, b);
        }

        [Fact]
        public void BenchmarkName_ImplicitStringConversion()
        {
            var name = new BenchmarkName("My Benchmark");
            string safe = name;
            Assert.Equal("my-benchmark", safe);
        }

        [Fact]
        public void BenchmarkName_CollapsesMultipleHyphens()
        {
            var name = new BenchmarkName("My   Benchmark   Test");
            Assert.Equal("my-benchmark-test", name.Safe);
        }

        [Fact]
        public void BenchmarkName_RemovesSpecialCharacters()
        {
            var name = new BenchmarkName("AUDUSD D1 Benchmark!");
            Assert.Equal("audusd-d1-benchmark", name.Safe);
        }

        [Fact]
        public void BenchmarkSnapshot_AllPropertiesAccessible()
        {
            var snapshot = CreateSnapshot();
            Assert.Equal("test", snapshot.Name);
            Assert.NotNull(snapshot.Source);
            Assert.NotNull(snapshot.Context);
            Assert.NotNull(snapshot.Coverage);
            Assert.NotNull(snapshot.Checks);
            Assert.NotNull(snapshot.Metrics);
            Assert.NotNull(snapshot.Dataset);
            Assert.NotNull(snapshot.Weighting);
        }

        [Fact]
        public void BenchmarkSnapshot_NullName_Throws()
        {
            Assert.Throws<ArgumentException>(() => new BenchmarkSnapshot(
                name: "",
                establishedAtUtc: DateTimeOffset.UtcNow,
                source: new SourceIdentity("test.csv", 100, "abc123def456abc123def456abc123def456abc123def456abc123def456abcd"),
                context: CreateContext(),
                coverage: new ScanCoverage(10, 10, 0),
                checks: Array.Empty<CheckExecution>(),
                metrics: Array.Empty<MetricScore>(),
                dataset: DatasetScore.Unavailable("test", MetricPopulationMap.CanonicalOrder.ToList(), new List<ExcludedMetric>()),
                weighting: ScoreWeightResolver.Default()));
        }

        [Fact]
        public void CandidateIdentity_AllPropertiesAccessible()
        {
            var identity = new CandidateIdentity(
                new SourceIdentity("candidate.csv", 200, "abc123def456abc123def456abc123def456abc123def456abc123def456abcd"),
                CreateContext());

            Assert.NotNull(identity.Source);
            Assert.NotNull(identity.Context);
            Assert.Equal("candidate.csv", identity.Source.FileName);
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
                name: "test",
                establishedAtUtc: DateTimeOffset.UtcNow,
                source: source,
                context: CreateContext(),
                coverage: new ScanCoverage(10, 10, 0),
                checks: CanonicalChecks(),
                metrics: metrics,
                dataset: datasetScore,
                weighting: weighting);
        }

        private static DetailedValidationReport CreateValidReport()
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

        private static ICompletedFindingCatalog EmptyCatalog() => new StubCatalog();

        private sealed class StubCatalog : ICompletedFindingCatalog
        {
            private static readonly CategoryStatistics Zero = new(0, 0);
            public FindingCatalogStatistics Statistics => new(Zero, Zero, Zero, Zero, Zero, Zero);
            public IAsyncEnumerable<IDetailedFindingCursor> ReadCanonicalAsync(CancellationToken ct = default)
                => AsyncEnumerable.Empty<IDetailedFindingCursor>();
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        /// <summary>
        /// Fake IBenchmarkStore for testing the use case.
        /// </summary>
        private sealed class FakeBenchmarkStore : IBenchmarkStore
        {
            public bool ExistsResult { get; set; }
            public List<BenchmarkSnapshot> SavedSnapshots { get; } = new();

            public ValueTask SaveAsync(BenchmarkSnapshot snapshot, string sourceFilePath, CancellationToken ct = default)
            {
                SavedSnapshots.Add(snapshot);
                return ValueTask.CompletedTask;
            }

            public ValueTask<BenchmarkSnapshot> LoadAsync(string name, CancellationToken ct = default)
                => throw new NotImplementedException();

            public ValueTask<bool> DeleteAsync(string name, CancellationToken ct = default)
                => throw new NotImplementedException();

            public ValueTask<bool> ExistsAsync(string name, CancellationToken ct = default)
                => new(ExistsResult);

            public ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
                => new(Array.Empty<string>());
        }
    }
}
