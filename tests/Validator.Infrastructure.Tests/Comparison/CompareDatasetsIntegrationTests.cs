using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Validator.Application.Benchmark;
using Validator.Application.Comparison;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Candles;
using Validator.Domain.Comparison;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;
using Validator.Infrastructure.Benchmark;
using Validator.Infrastructure.Csv;
using Xunit;

namespace Validator.Infrastructure.Tests.Comparison
{
    /// <summary>
    /// Integration tests for CompareDatasetsUseCase with file-based benchmark store.
    /// Tests end-to-end: establish benchmark from CSV, load from FileBenchmarkStore,
    /// load candidate from CsvCandleSource, compare, verify ComparisonReport structure.
    /// </summary>
    public class CompareDatasetsIntegrationTests : IDisposable
    {
        private readonly string _tempDir;

        public CompareDatasetsIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "benchmark-integration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, recursive: true); }
                catch { /* best effort cleanup */ }
            }
        }

        [Fact]
        public async Task Compare_IdenticalCandidate_NoMaterialDiscrepancies()
        {
            // Arrange: Establish a benchmark from the reference CSV
            var referencePath = GetFixturePath("AUDUSD_D1_reference.csv");
            var store = new FileBenchmarkStore(_tempDir);
            var snapshot = await EstablishBenchmarkAsync(store, referencePath);

            // Load benchmark candles from the stored source
            var benchmarkSourcePath = Path.Combine(_tempDir, snapshot.Name, "source.csv");
            var benchmarkCandles = await LoadCandlesAsync(benchmarkSourcePath);

            // Load candidate (identical to reference)
            var candidatePath = GetFixturePath("AUDUSD_D1_candidate_identical.csv");
            var candidateCandles = await LoadCandlesAsync(candidatePath);

            var candidateIdentity = CreateCandidateIdentity();

            // Act
            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(snapshot, benchmarkCandles, candidateCandles, candidateIdentity);

            // Assert
            Assert.Empty(report.MaterialDiscrepancies);
            Assert.Equal(benchmarkCandles.Count, report.Coverage.MatchedCount);
            Assert.Equal(0L, report.Coverage.MissingFromCandidateCount);
            Assert.Equal(0L, report.Coverage.ExtraInCandidateCount);
            Assert.True(report.AgreementScore.Score.HasValue);
            Assert.Equal("100.00", report.AgreementScore.Score.Value.Format());
        }

        [Fact]
        public async Task Compare_CandidateWithDifferences_DetectsMaterialDiscrepancy()
        {
            // Arrange
            var referencePath = GetFixturePath("AUDUSD_D1_reference.csv");
            var store = new FileBenchmarkStore(_tempDir);
            var snapshot = await EstablishBenchmarkAsync(store, referencePath);

            var benchmarkSourcePath = Path.Combine(_tempDir, snapshot.Name, "source.csv");
            var benchmarkCandles = await LoadCandlesAsync(benchmarkSourcePath);

            // Candidate has a known material difference: Open on 2026.01.02 is 0.63458 vs 0.63421
            var candidatePath = GetFixturePath("AUDUSD_D1_candidate_with_differences.csv");
            var candidateCandles = await LoadCandlesAsync(candidatePath);

            var candidateIdentity = CreateCandidateIdentity();

            // Act
            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(snapshot, benchmarkCandles, candidateCandles, candidateIdentity);

            // Assert
            Assert.True(report.MaterialDiscrepancies.Count > 0, "Expected material discrepancies");

            // Verify the Open price difference at the first candle
            // (2026.01.02 00:00 with +02:00 offset → 2026-01-01T22:00:00Z)
            var openDiscrepancy = report.MaterialDiscrepancies
                .FirstOrDefault(d => d.Field == OhlcvField.Open);

            Assert.NotNull(openDiscrepancy);
            Assert.Equal(0.63421m, openDiscrepancy!.BenchmarkValue);
            Assert.Equal(0.63458m, openDiscrepancy.CandidateValue);
            Assert.IsType<ToleranceDecision.MaterialDifference>(openDiscrepancy.ToleranceDecision);
        }

        [Fact]
        public async Task Compare_CoverageReportsCorrectCounts()
        {
            // Arrange
            var referencePath = GetFixturePath("AUDUSD_D1_reference.csv");
            var store = new FileBenchmarkStore(_tempDir);
            var snapshot = await EstablishBenchmarkAsync(store, referencePath);

            var benchmarkSourcePath = Path.Combine(_tempDir, snapshot.Name, "source.csv");
            var benchmarkCandles = await LoadCandlesAsync(benchmarkSourcePath);

            var candidatePath = GetFixturePath("AUDUSD_D1_candidate_with_differences.csv");
            var candidateCandles = await LoadCandlesAsync(candidatePath);

            var candidateIdentity = CreateCandidateIdentity();

            // Act
            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(snapshot, benchmarkCandles, candidateCandles, candidateIdentity);

            // Assert
            Assert.Equal(benchmarkCandles.Count, report.Coverage.BenchmarkRecordCount);
            Assert.Equal(candidateCandles.Count, report.Coverage.CandidateRecordCount);
            Assert.True(report.Coverage.MatchedCount > 0);
            Assert.True(report.Coverage.OverlappingRangeStart.HasValue);
            Assert.True(report.Coverage.OverlappingRangeEnd.HasValue);
        }

        [Fact]
        public async Task Compare_TextReportWriter_RendersCompleteReport()
        {
            // Arrange
            var referencePath = GetFixturePath("AUDUSD_D1_reference.csv");
            var store = new FileBenchmarkStore(_tempDir);
            var snapshot = await EstablishBenchmarkAsync(store, referencePath);

            var benchmarkSourcePath = Path.Combine(_tempDir, snapshot.Name, "source.csv");
            var benchmarkCandles = await LoadCandlesAsync(benchmarkSourcePath);

            var candidatePath = GetFixturePath("AUDUSD_D1_candidate_identical.csv");
            var candidateCandles = await LoadCandlesAsync(candidatePath);

            var candidateIdentity = CreateCandidateIdentity();

            // Act
            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(snapshot, benchmarkCandles, candidateCandles, candidateIdentity);

            var textWriter = new ComparisonTextReportWriter();
            var text = textWriter.Write(report);

            // Assert
            Assert.Contains("=== BENCHMARK COMPARISON ===", text);
            Assert.Contains($"Benchmark: {snapshot.Name}", text);
            Assert.Contains("Coverage:", text);
            Assert.Contains("Tolerated Differences:", text);
            Assert.Contains("Benchmark-Agreement Score:", text);
        }

        [Fact]
        public async Task Compare_JsonReportWriter_RendersCompleteReport()
        {
            // Arrange
            var referencePath = GetFixturePath("AUDUSD_D1_reference.csv");
            var store = new FileBenchmarkStore(_tempDir);
            var snapshot = await EstablishBenchmarkAsync(store, referencePath);

            var benchmarkSourcePath = Path.Combine(_tempDir, snapshot.Name, "source.csv");
            var benchmarkCandles = await LoadCandlesAsync(benchmarkSourcePath);

            var candidatePath = GetFixturePath("AUDUSD_D1_candidate_identical.csv");
            var candidateCandles = await LoadCandlesAsync(candidatePath);

            var candidateIdentity = CreateCandidateIdentity();

            // Act
            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(snapshot, benchmarkCandles, candidateCandles, candidateIdentity);

            var jsonWriter = new ComparisonJsonReportWriter();
            var json = jsonWriter.Write(report);

            // Assert
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(1, doc.RootElement.GetProperty("contractVersion").GetInt32());
            Assert.True(doc.RootElement.TryGetProperty("benchmark", out _));
            Assert.True(doc.RootElement.TryGetProperty("configuration", out _));
            Assert.True(doc.RootElement.TryGetProperty("comparisonCoverage", out _));
            Assert.True(doc.RootElement.TryGetProperty("materialDiscrepancies", out _));
            Assert.True(doc.RootElement.TryGetProperty("toleratedSummary", out _));
            Assert.True(doc.RootElement.TryGetProperty("agreementScore", out _));
        }

        #region Helpers

        private async Task<BenchmarkSnapshot> EstablishBenchmarkAsync(
            FileBenchmarkStore store, string sourcePath)
        {
            var candles = await LoadCandlesAsync(sourcePath);

            var sourceIdentity = new SourceIdentity(
                Path.GetFileName(sourcePath),
                new FileInfo(sourcePath).Length,
                await ComputeSha256Async(sourcePath));

            var context = new ValidationContextSnapshot(
                "D1",
                new CalendarContext("forex", "Forex"),
                TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                "comma", false, null);

            var coverage = new ScanCoverage(candles.Count, candles.Count, 0);
            var metrics = MetricPopulationMap.CanonicalOrder.Select(cat =>
                MetricScoreCalculator.ScoreMetric(cat, 0, 100, MetricPopulationMap.KindFor(cat))
            ).ToList();
            var weighting = ScoreWeightResolver.Default();
            var datasetScore = DatasetScore.Available(
                new ScoreValue(new ExactRatio(100, 1)),
                MetricPopulationMap.CanonicalOrder.ToList(),
                new List<ExcludedMetric>());

            var checks = new[]
            {
                new CheckExecution(CheckName.MissingCandles, CheckStatus.Completed),
                new CheckExecution(CheckName.DuplicateRecords, CheckStatus.Completed),
                new CheckExecution(CheckName.InvalidOhlc, CheckStatus.Completed),
                new CheckExecution(CheckName.ClosedMarketRecords, CheckStatus.Completed),
                new CheckExecution(CheckName.TimeGaps, CheckStatus.Completed),
                new CheckExecution(CheckName.MalformedRows, CheckStatus.Completed)
            };

            var snapshot = new BenchmarkSnapshot(
                name: "integration-test-benchmark",
                establishedAtUtc: DateTimeOffset.UtcNow,
                source: sourceIdentity,
                context: context,
                coverage: coverage,
                checks: checks,
                metrics: metrics,
                dataset: datasetScore,
                weighting: weighting);

            // Save directly via store (avoids needing a DetailedValidationReport)
            await store.SaveAsync(snapshot, sourcePath).ConfigureAwait(false);
            return snapshot;
        }

        private static async Task<List<PriceCandle>> LoadCandlesAsync(string path)
        {
            var source = new CsvCandleSource(path);
            var candles = new List<PriceCandle>();
            await foreach (var candle in source.ReadAllAsync().ConfigureAwait(false))
            {
                candles.Add(candle);
            }
            candles.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return candles;
        }

        private static CandidateIdentity CreateCandidateIdentity()
        {
            var source = new SourceIdentity("candidate.csv", 100,
                "abc123def456abc123def456abc123def456abc123def456abc123def456abcd");
            var context = new ValidationContextSnapshot(
                "D1",
                new CalendarContext("forex", "Forex"),
                TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                "comma", false, null);
            return new CandidateIdentity(source, context);
        }

        private static string GetFixturePath(string fileName)
        {
            var assemblyDir = Path.GetDirectoryName(typeof(CompareDatasetsIntegrationTests).Assembly.Location)!;
            return Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "tests", "Fixtures", fileName);
        }

        private static async Task<string> ComputeSha256Async(string path)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = await sha256.ComputeHashAsync(stream).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        #endregion
    }
}
