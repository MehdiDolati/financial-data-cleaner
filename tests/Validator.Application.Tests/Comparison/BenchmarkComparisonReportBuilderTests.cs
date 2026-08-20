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
    public class BenchmarkComparisonReportBuilderTests
    {
        [Fact]
        public void Build_WithAllResults_ProducesCompleteReport()
        {
            var benchmark = CreateBenchmark("test");
            var candidateIdentity = CreateCandidateIdentity();
            var config = ToleranceResolver.Resolve(null, "test");
            var coverage = new ComparisonCoverage(5, 5, 5, 0, 0,
                new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero));
            var fieldResults = CreateFieldResults();
            var discrepancies = new List<FieldDiscrepancy>();

            var report = BenchmarkComparisonReportBuilder.Build(
                benchmark, candidateIdentity, config, coverage,
                fieldResults, discrepancies);

            Assert.NotNull(report.Benchmark);
            Assert.NotNull(report.Candidate);
            Assert.NotNull(report.Configuration);
            Assert.NotNull(report.Coverage);
            Assert.Empty(report.MaterialDiscrepancies);
            Assert.NotNull(report.ToleratedSummary);
            Assert.NotNull(report.AgreementScore);
            Assert.Equal(100m, report.AgreementScore.Score!.Value.Rounded);
        }

        [Fact]
        public void Build_WithMaterialDiscrepancies_ComputesCorrectAgreementScore()
        {
            var benchmark = CreateBenchmark("test");
            var candidateIdentity = CreateCandidateIdentity();
            var config = ToleranceResolver.Resolve(null, "test");
            var coverage = new ComparisonCoverage(5, 5, 5, 0, 0);
            var fieldResults = CreateFieldResultsWithMaterial();
            var discrepancies = CreateMaterialDiscrepancies();

            var report = BenchmarkComparisonReportBuilder.Build(
                benchmark, candidateIdentity, config, coverage,
                fieldResults, discrepancies);

            // 5 matched, 2 timestamps with material discrepancies
            // Score = 100 * (5 - 2) / 5 = 60.00
            Assert.Equal(60m, report.AgreementScore.Score!.Value.Rounded);
            Assert.Equal(2, report.AgreementScore.MaterialDiscrepancyCount);
        }

        [Fact]
        public void Build_NoOverlap_UnavailableScore()
        {
            var benchmark = CreateBenchmark("test");
            var candidateIdentity = CreateCandidateIdentity();
            var config = ToleranceResolver.Resolve(null, "test");
            var coverage = new ComparisonCoverage(5, 2, 0, 5, 2);
            var fieldResults = new List<FieldComparisonResult>();

            var report = BenchmarkComparisonReportBuilder.Build(
                benchmark, candidateIdentity, config, coverage,
                fieldResults, new List<FieldDiscrepancy>());

            Assert.Null(report.AgreementScore.Score);
            Assert.NotNull(report.AgreementScore.UnavailableReason);
        }

        [Fact]
        public void BuildToleratedSummary_AllFieldsPresent()
        {
            var config = ToleranceResolver.Resolve(null, "test");
            var fieldResults = CreateFieldResults();

            var summary = BenchmarkComparisonReportBuilder.BuildToleratedSummary(config, fieldResults);

            Assert.Equal(5, summary.Count);
            Assert.Equal(OhlcvField.Open, summary[0].Field);
            Assert.Equal(OhlcvField.Volume, summary[4].Field);
        }

        [Fact]
        public void BuildToleratedSummary_CorrectCounts()
        {
            var config = ToleranceResolver.Resolve(null, "test");
            var fieldResults = new List<FieldComparisonResult>();

            // Add 3 results for Open: 2 accepted, 1 material
            fieldResults.Add(new FieldComparisonResult
            {
                TimestampUtc = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                Field = OhlcvField.Open,
                BenchmarkValue = 0.63421m,
                CandidateValue = 0.63421m,
                Decision = new ToleranceDecision.AcceptedByAbsolute()
            });
            fieldResults.Add(new FieldComparisonResult
            {
                TimestampUtc = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
                Field = OhlcvField.Open,
                BenchmarkValue = 0.63502m,
                CandidateValue = 0.63502m,
                Decision = new ToleranceDecision.AcceptedByAbsolute()
            });
            fieldResults.Add(new FieldComparisonResult
            {
                TimestampUtc = new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero),
                Field = OhlcvField.Open,
                BenchmarkValue = 0.63612m,
                CandidateValue = 0.63662m,
                Decision = new ToleranceDecision.MaterialDifference()
            });

            var summary = BenchmarkComparisonReportBuilder.BuildToleratedSummary(config, fieldResults);
            var openSummary = summary.First(s => s.Field == OhlcvField.Open);

            Assert.Equal(3, openSummary.TotalCompared);
            Assert.Equal(2, openSummary.AcceptedCount);
            Assert.Equal(2, openSummary.AcceptedByAbsoluteCount);
            Assert.Equal(0, openSummary.AcceptedByRelativeCount);
            Assert.Equal(1, openSummary.MaterialCount);
        }

        #region Test Helpers

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

        private static List<FieldComparisonResult> CreateFieldResults()
        {
            var results = new List<FieldComparisonResult>();
            var timestamps = new[]
            {
                new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 7, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero),
            };

            foreach (var ts in timestamps)
            {
                foreach (OhlcvField field in Enum.GetValues<OhlcvField>())
                {
                    results.Add(new FieldComparisonResult
                    {
                        TimestampUtc = ts,
                        Field = field,
                        BenchmarkValue = 0.63421m,
                        CandidateValue = 0.63421m,
                        Decision = new ToleranceDecision.AcceptedByAbsolute()
                    });
                }
            }

            return results;
        }

        private static List<FieldComparisonResult> CreateFieldResultsWithMaterial()
        {
            var results = CreateFieldResults();
            // Make 2 results material
            results[0] = results[0] with { Decision = new ToleranceDecision.MaterialDifference() };
            results[5] = results[5] with { Decision = new ToleranceDecision.MaterialDifference() };
            return results;
        }

        private static List<FieldDiscrepancy> CreateMaterialDiscrepancies()
        {
            return new List<FieldDiscrepancy>
            {
                new FieldDiscrepancy(
                    new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                    OhlcvField.Open, 0.63421m, 0.63471m, 0.00050m, 0.00050m,
                    0.00010m, 0.0001m, new ToleranceDecision.MaterialDifference()),
                new FieldDiscrepancy(
                    new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
                    OhlcvField.High, 0.63650m, 0.63700m, 0.00050m, 0.00050m,
                    0.00010m, 0.0001m, new ToleranceDecision.MaterialDifference())
            };
        }

        private static string Sha256() => "abc123def456abc123def456abc123def456abc123def456abc123def456abcd";

        #endregion
    }
}
