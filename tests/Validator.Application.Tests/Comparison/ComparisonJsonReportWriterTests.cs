using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Validator.Application.Benchmark;
using Validator.Application.Comparison;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Comparison;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;
using Xunit;

namespace Validator.Application.Tests.Comparison
{
    public class ComparisonJsonReportWriterTests
    {
        [Fact]
        public void Write_ContainsContractVersion()
        {
            var report = CreateReport();
            var writer = new ComparisonJsonReportWriter();
            var json = writer.Write(report);
            using var doc = JsonDocument.Parse(json);

            Assert.Equal(1, doc.RootElement.GetProperty("contractVersion").GetInt32());
        }

        [Fact]
        public void Write_ContainsBenchmarkSection()
        {
            var report = CreateReport();
            var writer = new ComparisonJsonReportWriter();
            var json = writer.Write(report);
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("benchmark", out var benchmark));
            Assert.Equal("test", benchmark.GetProperty("name").GetString());
        }

        [Fact]
        public void Write_ContainsConfigurationSection()
        {
            var report = CreateReport();
            var writer = new ComparisonJsonReportWriter();
            var json = writer.Write(report);
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("configuration", out var config));
            Assert.Equal("test", config.GetProperty("benchmarkName").GetString());
            Assert.Equal(5, config.GetProperty("fields").GetArrayLength());
        }

        [Fact]
        public void Write_ContainsCoverageSection()
        {
            var report = CreateReport();
            var writer = new ComparisonJsonReportWriter();
            var json = writer.Write(report);
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("comparisonCoverage", out var coverage));
            Assert.Equal(5, coverage.GetProperty("benchmarkRecordCount").GetInt64());
            Assert.Equal(5, coverage.GetProperty("candidateRecordCount").GetInt64());
            Assert.Equal(5, coverage.GetProperty("matchedCount").GetInt64());
        }

        [Fact]
        public void Write_ContainsMaterialDiscrepanciesArray()
        {
            var report = CreateReport();
            var writer = new ComparisonJsonReportWriter();
            var json = writer.Write(report);
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("materialDiscrepancies", out var discrepancies));
            Assert.Equal(JsonValueKind.Array, discrepancies.ValueKind);
            Assert.Equal(0, discrepancies.GetArrayLength());
        }

        [Fact]
        public void Write_ContainsToleratedSummaryArray()
        {
            var report = CreateReport();
            var writer = new ComparisonJsonReportWriter();
            var json = writer.Write(report);
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("toleratedSummary", out var summary));
            Assert.Equal(JsonValueKind.Array, summary.ValueKind);
            Assert.Equal(5, summary.GetArrayLength());
        }

        [Fact]
        public void Write_ContainsAgreementScore()
        {
            var report = CreateReport();
            var writer = new ComparisonJsonReportWriter();
            var json = writer.Write(report);
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("agreementScore", out var score));
            Assert.False(score.GetProperty("score").TryGetProperty("null", out _));
            Assert.Equal("100 × (matchedPopulation - materialDiscrepancyTimestamps) / matchedPopulation",
                score.GetProperty("formula").GetString());
            Assert.Equal(5, score.GetProperty("matchedPopulation").GetInt64());
        }

        [Fact]
        public void Write_NoOverlap_ScoreIsNull()
        {
            var report = CreateReportNoOverlap();
            var writer = new ComparisonJsonReportWriter();
            var json = writer.Write(report);
            using var doc = JsonDocument.Parse(json);

            var agreementScore = doc.RootElement.GetProperty("agreementScore");
            // score is null and excluded by WhenWritingNull
            Assert.False(agreementScore.TryGetProperty("score", out _));
            // unavailableReason is present
            var unavailableReason = agreementScore.GetProperty("unavailableReason").GetString();
            Assert.Contains("No overlapping timestamps", unavailableReason);
        }

        [Fact]
        public void Write_Deterministic_IdenticalOutput()
        {
            var report = CreateReport();
            var writer = new ComparisonJsonReportWriter();

            var json1 = writer.Write(report);
            var json2 = writer.Write(report);

            Assert.Equal(json1, json2);
        }

        [Fact]
        public void Write_WithDiscrepancies_ContainsDiscrepancyFields()
        {
            var report = CreateReportWithDiscrepancies();
            var writer = new ComparisonJsonReportWriter();
            var json = writer.Write(report);
            using var doc = JsonDocument.Parse(json);

            var discrepancies = doc.RootElement.GetProperty("materialDiscrepancies");
            Assert.Equal(1, discrepancies.GetArrayLength());

            var first = discrepancies[0];
            Assert.Equal("open", first.GetProperty("field").GetString());
            Assert.True(first.TryGetProperty("benchmarkValue", out _));
            Assert.True(first.TryGetProperty("candidateValue", out _));
            Assert.True(first.TryGetProperty("difference", out _));
            Assert.True(first.TryGetProperty("toleranceDecision", out _));
        }

        [Fact]
        public void Write_NullReport_Throws()
        {
            var writer = new ComparisonJsonReportWriter();
            Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        }

        [Fact]
        public void WriteSection_NullWriter_Throws()
        {
            var report = CreateReport();
            var writer = new ComparisonJsonReportWriter();
            Assert.Throws<ArgumentNullException>(() => writer.WriteSection(null!, report));
        }

        [Fact]
        public void WriteSection_NullReport_Throws()
        {
            using var stream = new System.IO.MemoryStream();
            using var jsonWriter = new System.Text.Json.Utf8JsonWriter(stream);
            var writer = new ComparisonJsonReportWriter();
            Assert.Throws<ArgumentNullException>(() => writer.WriteSection(jsonWriter, null!));
        }

        [Fact]
        public void WriteSection_WritesCorrectJsonStructure()
        {
            var report = CreateReport();
            var writer = new ComparisonJsonReportWriter();
            using var stream = new System.IO.MemoryStream();
            using (var jsonWriter = new System.Text.Json.Utf8JsonWriter(stream))
            {
                jsonWriter.WriteStartObject();
                writer.WriteSection(jsonWriter, report);
                jsonWriter.WriteEndObject();
            }
            stream.Position = 0;
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement.GetProperty("benchmarkComparison");
            Assert.Equal(1, root.GetProperty("contractVersion").GetInt32());
            Assert.True(root.TryGetProperty("benchmark", out _));
            Assert.True(root.TryGetProperty("configuration", out _));
            Assert.True(root.TryGetProperty("comparisonCoverage", out _));
            Assert.True(root.TryGetProperty("materialDiscrepancies", out _));
            Assert.True(root.TryGetProperty("toleratedSummary", out _));
            Assert.True(root.TryGetProperty("agreementScore", out _));
        }

        [Fact]
        public void WriteSection_WithOverlappingRange_IncludesRange()
        {
            var report = CreateReport();
            var writer = new ComparisonJsonReportWriter();
            using var stream = new System.IO.MemoryStream();
            using (var jsonWriter = new System.Text.Json.Utf8JsonWriter(stream))
            {
                jsonWriter.WriteStartObject();
                writer.WriteSection(jsonWriter, report);
                jsonWriter.WriteEndObject();
            }
            stream.Position = 0;
            using var doc = JsonDocument.Parse(stream);
            var coverage = doc.RootElement.GetProperty("benchmarkComparison").GetProperty("comparisonCoverage");
            Assert.True(coverage.TryGetProperty("overlappingRange", out var range));
            Assert.True(range.TryGetProperty("start", out _));
            Assert.True(range.TryGetProperty("end", out _));
        }

        [Fact]
        public void WriteSection_NoOverlap_NoOverlappingRange()
        {
            var report = CreateReportNoOverlap();
            var writer = new ComparisonJsonReportWriter();
            using var stream = new System.IO.MemoryStream();
            using (var jsonWriter = new System.Text.Json.Utf8JsonWriter(stream))
            {
                jsonWriter.WriteStartObject();
                writer.WriteSection(jsonWriter, report);
                jsonWriter.WriteEndObject();
            }
            stream.Position = 0;
            using var doc = JsonDocument.Parse(stream);
            var coverage = doc.RootElement.GetProperty("benchmarkComparison").GetProperty("comparisonCoverage");
            Assert.False(coverage.TryGetProperty("overlappingRange", out _));
        }

        [Fact]
        public void WriteSection_WithDiscrepancies_IncludesAllFields()
        {
            var report = CreateReportWithDiscrepancies();
            var writer = new ComparisonJsonReportWriter();
            using var stream = new System.IO.MemoryStream();
            using (var jsonWriter = new System.Text.Json.Utf8JsonWriter(stream))
            {
                jsonWriter.WriteStartObject();
                writer.WriteSection(jsonWriter, report);
                jsonWriter.WriteEndObject();
            }
            stream.Position = 0;
            using var doc = JsonDocument.Parse(stream);
            var disc = doc.RootElement.GetProperty("benchmarkComparison").GetProperty("materialDiscrepancies");
            Assert.Equal(1, disc.GetArrayLength());
            var first = disc[0];
            Assert.True(first.TryGetProperty("timestampUtc", out _));
            Assert.True(first.TryGetProperty("field", out _));
            Assert.True(first.TryGetProperty("benchmarkValue", out _));
            Assert.True(first.TryGetProperty("candidateValue", out _));
            Assert.True(first.TryGetProperty("difference", out _));
            Assert.True(first.TryGetProperty("directionalDifference", out _));
            Assert.True(first.TryGetProperty("resolvedAbsoluteTolerance", out _));
            Assert.True(first.TryGetProperty("resolvedRelativeTolerance", out _));
            Assert.True(first.TryGetProperty("toleranceDecision", out _));
        }

        [Fact]
        public void WriteSection_NoOverlap_AgreementScoreUnavailable()
        {
            var report = CreateReportNoOverlap();
            var writer = new ComparisonJsonReportWriter();
            using var stream = new System.IO.MemoryStream();
            using (var jsonWriter = new System.Text.Json.Utf8JsonWriter(stream))
            {
                jsonWriter.WriteStartObject();
                writer.WriteSection(jsonWriter, report);
                jsonWriter.WriteEndObject();
            }
            stream.Position = 0;
            using var doc = JsonDocument.Parse(stream);
            var score = doc.RootElement.GetProperty("benchmarkComparison").GetProperty("agreementScore");
            Assert.True(score.TryGetProperty("unavailableReason", out _));
        }

        [Fact]
        public void Write_WithCandidateScore_IncludesScore()
        {
            var benchmark = CreateBenchmark("test");
            var candidateIdentity = CreateCandidateIdentity();
            var config = ToleranceResolver.Resolve(null, "test");
            var coverage = new ComparisonCoverage(5, 5, 5, 0, 0);
            var metrics = MetricPopulationMap.CanonicalOrder.Select(cat =>
                MetricScoreCalculator.ScoreMetric(cat, 0, 100, MetricPopulationMap.KindFor(cat))
            ).ToList();
            var weighting = ScoreWeightResolver.Default();
            var datasetScore = DatasetScore.Available(
                new ScoreValue(new ExactRatio(100, 1)),
                MetricPopulationMap.CanonicalOrder.ToList(),
                new List<ExcludedMetric>());
            var candidateScore = new DatasetScoreReport(metrics, weighting, datasetScore);

            var report = new ComparisonReport(
                benchmark, candidateIdentity, config, coverage,
                new List<FieldDiscrepancy>(),
                config.Fields.Select(f => new ToleratedDifferenceAggregate(f.Field, 5, 5, 5, 0, 0)).ToList(),
                candidateScore,
                BenchmarkAgreementScore.Available(5, 0),
                DateTimeOffset.UtcNow);

            var jsonWriter = new ComparisonJsonReportWriter();
            var json = jsonWriter.Write(report);
            using var doc = JsonDocument.Parse(json);
            // The report should serialize correctly even with candidateScore
            Assert.True(doc.RootElement.TryGetProperty("contractVersion", out _));
        }

        #region Test Helpers

        private static ComparisonReport CreateReport()
        {
            var benchmark = CreateBenchmark("test");
            var candidateIdentity = CreateCandidateIdentity();
            var config = ToleranceResolver.Resolve(null, "test");
            var coverage = new ComparisonCoverage(5, 5, 5, 0, 0,
                new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero));

            var toleratedSummary = config.Fields.Select(f =>
                new ToleratedDifferenceAggregate(f.Field, 5, 5, 5, 0, 0)).ToList();

            var agreementScore = BenchmarkAgreementScore.Available(5, 0);

            return new ComparisonReport(
                benchmark, candidateIdentity, config, coverage,
                new List<FieldDiscrepancy>(), toleratedSummary,
                null, agreementScore, DateTimeOffset.UtcNow);
        }

        private static ComparisonReport CreateReportWithDiscrepancies()
        {
            var benchmark = CreateBenchmark("test");
            var candidateIdentity = CreateCandidateIdentity();
            var config = ToleranceResolver.Resolve(null, "test");
            var coverage = new ComparisonCoverage(5, 5, 5, 0, 0);

            var discrepancies = new List<FieldDiscrepancy>
            {
                new FieldDiscrepancy(
                    new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                    OhlcvField.Open, 0.63421m, 0.63471m, 0.00050m, 0.00050m,
                    0.00010m, 0.0001m, new ToleranceDecision.MaterialDifference())
            };

            var toleratedSummary = config.Fields.Select(f =>
                new ToleratedDifferenceAggregate(f.Field, 5, 4, 4, 0, 1)).ToList();

            var agreementScore = BenchmarkAgreementScore.Available(5, 1);

            return new ComparisonReport(
                benchmark, candidateIdentity, config, coverage,
                discrepancies, toleratedSummary,
                null, agreementScore, DateTimeOffset.UtcNow);
        }

        private static ComparisonReport CreateReportNoOverlap()
        {
            var benchmark = CreateBenchmark("test");
            var candidateIdentity = CreateCandidateIdentity();
            var config = ToleranceResolver.Resolve(null, "test");
            var coverage = new ComparisonCoverage(5, 2, 0, 5, 2);

            var toleratedSummary = config.Fields.Select(f =>
                new ToleratedDifferenceAggregate(f.Field, 0, 0, 0, 0, 0)).ToList();

            var agreementScore = BenchmarkAgreementScore.Unavailable(
                "No overlapping timestamps between benchmark and candidate");

            return new ComparisonReport(
                benchmark, candidateIdentity, config, coverage,
                new List<FieldDiscrepancy>(), toleratedSummary,
                null, agreementScore, DateTimeOffset.UtcNow);
        }

        private static BenchmarkSnapshot CreateBenchmark(string name)
        {
            var source = new SourceIdentity("test.csv", 100, Sha256());
            var metrics = MetricPopulationMap.CanonicalOrder.Select(cat =>
                MetricScoreCalculator.ScoreMetric(cat, 0, 100, MetricPopulationMap.KindFor(cat))
            ).ToList();
            var weighting = ScoreWeightResolver.Default();
            var datasetScore = DatasetScore.Available(
                new ScoreValue(new ExactRatio(100, 1)),
                MetricPopulationMap.CanonicalOrder.ToList(),
                new List<ExcludedMetric>());

            return new BenchmarkSnapshot(
                name: name,
                establishedAtUtc: DateTimeOffset.UtcNow,
                source: source,
                context: CreateContext(),
                coverage: new ScanCoverage(5, 5, 0),
                checks: CanonicalChecks(),
                metrics: metrics,
                dataset: datasetScore,
                weighting: weighting);
        }

        private static CandidateIdentity CreateCandidateIdentity()
        {
            return new CandidateIdentity(
                new SourceIdentity("candidate.csv", 100, Sha256()),
                CreateContext());
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

        private static string Sha256() => "abc123def456abc123def456abc123def456abc123def456abc123def456abcd";

        #endregion
    }
}
