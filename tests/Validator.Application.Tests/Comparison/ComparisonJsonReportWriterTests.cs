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
