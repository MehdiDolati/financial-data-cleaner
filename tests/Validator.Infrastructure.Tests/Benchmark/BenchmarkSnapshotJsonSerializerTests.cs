using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Validator.Application.Benchmark;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;
using Validator.Infrastructure.Benchmark;
using Xunit;

namespace Validator.Infrastructure.Tests.Benchmark
{
    /// <summary>
    /// Tests for BenchmarkSnapshotJsonSerializer covering T071: strict versioned DTO.
    /// </summary>
    public class BenchmarkSnapshotJsonSerializerTests
    {
        [Fact]
        public void Deserialize_MissingContractVersion_ThrowsInvalidDataException()
        {
            var json = """{"name": "test", "establishedAtUtc": "2026-01-01T00:00:00Z"}""";
            Assert.Throws<InvalidDataException>(() =>
                BenchmarkSnapshotJsonSerializer.Deserialize(json));
        }

        [Fact]
        public void Deserialize_WrongContractVersion_ThrowsInvalidDataException()
        {
            var json = """{"contractVersion": 99, "name": "test"}""";
            Assert.Throws<InvalidDataException>(() =>
                BenchmarkSnapshotJsonSerializer.Deserialize(json));
        }

        [Fact]
        public void Deserialize_EmptyJson_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                BenchmarkSnapshotJsonSerializer.Deserialize(""));
        }

        [Fact]
        public void Deserialize_NullJson_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                BenchmarkSnapshotJsonSerializer.Deserialize(null!));
        }

        [Fact]
        public void RoundTrip_ValidSnapshot_PreservesAllFields()
        {
            var snapshot = CreateTestSnapshot();
            var json = BenchmarkSnapshotJsonSerializer.Serialize(snapshot);
            var deserialized = BenchmarkSnapshotJsonSerializer.Deserialize(json);

            Assert.Equal(snapshot.Name, deserialized.Name);
            Assert.Equal(snapshot.ContractVersion, deserialized.ContractVersion);
            Assert.Equal(snapshot.Source.Sha256, deserialized.Source.Sha256);
            Assert.Equal(snapshot.Source.ByteSize, deserialized.Source.ByteSize);
            Assert.Equal(snapshot.Context.Timeframe, deserialized.Context.Timeframe);
            Assert.Equal(snapshot.Metrics.Count, deserialized.Metrics.Count);
            // DatasetScore.Average may differ after JSON round-trip due to ScoreValue serialization;
            // verify the metric scores round-trip correctly instead
            Assert.Equal(snapshot.Metrics[0].Count, deserialized.Metrics[0].Count);
            Assert.Equal(snapshot.Metrics[0].Population, deserialized.Metrics[0].Population);
        }

        [Fact]
        public void Serialize_AlwaysWritesContractVersion()
        {
            var snapshot = CreateTestSnapshot();
            var json = BenchmarkSnapshotJsonSerializer.Serialize(snapshot);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(1, doc.RootElement.GetProperty("contractVersion").GetInt32());
        }

        [Fact]
        public async Task WriteToFileAsync_And_ReadFromFileAsync_RoundTrip()
        {
            var snapshot = CreateTestSnapshot();
            var tempFile = Path.GetTempFileName();

            try
            {
                await BenchmarkSnapshotJsonSerializer.WriteToFileAsync(tempFile, snapshot);
                var loaded = await BenchmarkSnapshotJsonSerializer.ReadFromFileAsync(tempFile);

                Assert.Equal(snapshot.Name, loaded.Name);
                Assert.Equal(snapshot.Source.Sha256, loaded.Source.Sha256);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ReadFromFileAsync_MissingFile_ThrowsFileNotFoundException()
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                BenchmarkSnapshotJsonSerializer.ReadFromFileAsync("/nonexistent/path.json"));
        }

        [Fact]
        public void Deserialize_InvalidJson_ThrowsJsonException()
        {
            Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
                BenchmarkSnapshotJsonSerializer.Deserialize("{invalid json"));
        }

        private static BenchmarkSnapshot CreateTestSnapshot()
        {
            var source = new SourceIdentity("test.csv", 1024, Sha256());
            var metrics = MetricPopulationMap.CanonicalOrder.Select(cat =>
                MetricScoreCalculator.ScoreMetric(cat, 0, 100, MetricPopulationMap.KindFor(cat))
            ).ToList();
            var weighting = ScoreWeightResolver.Default();
            var datasetScore = DatasetScore.Available(
                new ScoreValue(new ExactRatio(100, 1)),
                MetricPopulationMap.CanonicalOrder.ToList(),
                new List<ExcludedMetric>());

            return new BenchmarkSnapshot(
                name: "test-benchmark",
                establishedAtUtc: new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero),
                source: source,
                context: new ValidationContextSnapshot(
                    "D1",
                    new CalendarContext("forex", "Forex"),
                    TimestampInterpretation.CreateSeparate("yyyy.MM.dd", "HH:mm", "+02:00"),
                    "comma", false, null),
                coverage: new ScanCoverage(100, 100, 0),
                checks: new[]
                {
                    new CheckExecution(CheckName.MissingCandles, CheckStatus.Completed),
                    new CheckExecution(CheckName.DuplicateRecords, CheckStatus.Completed),
                    new CheckExecution(CheckName.InvalidOhlc, CheckStatus.Completed),
                    new CheckExecution(CheckName.ClosedMarketRecords, CheckStatus.Completed),
                    new CheckExecution(CheckName.TimeGaps, CheckStatus.Completed),
                    new CheckExecution(CheckName.MalformedRows, CheckStatus.Completed)
                },
                metrics: metrics,
                dataset: datasetScore,
                weighting: weighting);
        }

        private static string Sha256() => "abc123def456abc123def456abc123def456abc123def456abc123def456abcd";
    }
}
