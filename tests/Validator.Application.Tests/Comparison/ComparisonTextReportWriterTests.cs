using System;
using System.Collections.Generic;
using System.Linq;
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
    public class ComparisonTextReportWriterTests
    {
        [Fact]
        public void Write_ContainsBenchmarkSection()
        {
            var report = CreateReport();
            var writer = new ComparisonTextReportWriter();
            var text = writer.Write(report);

            Assert.Contains("=== BENCHMARK COMPARISON ===", text);
            Assert.Contains("Benchmark: test", text);
        }

        [Fact]
        public void Write_ContainsCoverageSection()
        {
            var report = CreateReport();
            var writer = new ComparisonTextReportWriter();
            var text = writer.Write(report);

            Assert.Contains("Coverage:", text);
            Assert.Contains("Benchmark records: 5", text);
            Assert.Contains("Candidate records: 5", text);
            Assert.Contains("Matched timestamps: 5", text);
        }

        [Fact]
        public void Write_ContainsToleratedDifferencesSection()
        {
            var report = CreateReport();
            var writer = new ComparisonTextReportWriter();
            var text = writer.Write(report);

            Assert.Contains("Tolerated Differences:", text);
            Assert.Contains("Open:", text);
            Assert.Contains("Volume:", text);
        }

        [Fact]
        public void Write_ContainsScoresSection()
        {
            var report = CreateReport();
            var writer = new ComparisonTextReportWriter();
            var text = writer.Write(report);

            Assert.Contains("Benchmark-Agreement Score:", text);
            Assert.Contains("100.00", text);
        }

        [Fact]
        public void Write_WithDiscrepancies_ShowsMaterialSection()
        {
            var report = CreateReportWithDiscrepancies();
            var writer = new ComparisonTextReportWriter();
            var text = writer.Write(report);

            Assert.Contains("Material Discrepancies (1 found):", text);
            Assert.Contains("Open", text);
            Assert.Contains("Material (exceeds both tolerances)", text);
        }

        [Fact]
        public void Write_NoDiscrepancies_ShowsZeroFound()
        {
            var report = CreateReport();
            var writer = new ComparisonTextReportWriter();
            var text = writer.Write(report);

            Assert.Contains("Material Discrepancies (0 found):", text);
        }

        [Fact]
        public void Write_NoOverlap_ShowsUnavailable()
        {
            var report = CreateReportNoOverlap();
            var writer = new ComparisonTextReportWriter();
            var text = writer.Write(report);

            Assert.Contains("Benchmark-Agreement Score: UNAVAILABLE", text);
            Assert.Contains("No overlapping timestamps", text);
        }

        [Fact]
        public void Write_NullReport_Throws()
        {
            var writer = new ComparisonTextReportWriter();
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
