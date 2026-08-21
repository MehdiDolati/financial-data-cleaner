using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Benchmark;
using Validator.Application.Comparison;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Candles;
using Validator.Domain.Comparison;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;
using Xunit;

namespace Validator.Application.Tests.Comparison
{
    public class ComparisonAuditTrailTests
    {
        [Fact]
        public void Discrepancy_CarriesTimestamp()
        {
            var discrepancy = CreateDiscrepancy(
                new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero));

            Assert.Equal(new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
                discrepancy.TimestampUtc);
        }

        [Fact]
        public void Discrepancy_CarriesField()
        {
            var discrepancy = CreateDiscrepancy(
                new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
                OhlcvField.Open);

            Assert.Equal(OhlcvField.Open, discrepancy.Field);
        }

        [Fact]
        public void Discrepancy_CarriesBenchmarkAndCandidateValues()
        {
            var discrepancy = CreateDiscrepancy(
                new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
                OhlcvField.Close,
                benchmarkValue: 0.65100m,
                candidateValue: 0.65062m);

            Assert.Equal(0.65100m, discrepancy.BenchmarkValue);
            Assert.Equal(0.65062m, discrepancy.CandidateValue);
        }

        [Fact]
        public void Discrepancy_CarriesTolerances()
        {
            var discrepancy = CreateDiscrepancy(
                new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
                OhlcvField.High,
                resolvedAbsolute: 0.00010m,
                resolvedRelative: 0.0001m);

            Assert.Equal(0.00010m, discrepancy.ResolvedAbsoluteTolerance);
            Assert.Equal(0.0001m, discrepancy.ResolvedRelativeTolerance);
        }

        [Fact]
        public void Discrepancy_CarriesSourceReference()
        {
            var discrepancy = CreateDiscrepancy(
                new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
                candidateSourceLine: 42);

            Assert.Equal(42, discrepancy.CandidateSourceLine);
        }

        [Fact]
        public void Report_CarriesConfiguration()
        {
            var report = CreateReportWithDiscrepancy();

            Assert.NotNull(report.Configuration);
            Assert.Equal("test", report.Configuration.BenchmarkName);
            Assert.Equal(5, report.Configuration.Fields.Count);
            Assert.Equal(Domain.Comparison.TimestampMode.Exact, report.Configuration.TimestampMode);
        }

        [Fact]
        public void Report_Configuration_RecordsResolvedTolerances()
        {
            var report = CreateReportWithDiscrepancy();

            var openField = report.Configuration.Fields.First(f => f.Field == OhlcvField.Open);
            Assert.Equal(0.00010m, openField.ResolvedAbsolute);
            Assert.Equal(0.0001m, openField.ResolvedRelative);
        }

        [Fact]
        public void Report_CarriesCoverage()
        {
            var report = CreateReportWithDiscrepancy();

            Assert.NotNull(report.Coverage);
            Assert.Equal(5, report.Coverage.BenchmarkRecordCount);
            Assert.Equal(5, report.Coverage.CandidateRecordCount);
            Assert.Equal(5, report.Coverage.MatchedCount);
        }

        [Fact]
        public void Report_CarriesBenchmarkSnapshot()
        {
            var report = CreateReportWithDiscrepancy();

            Assert.NotNull(report.Benchmark);
            Assert.Equal("test", report.Benchmark.Name);
            Assert.NotNull(report.Benchmark.Source);
            Assert.NotNull(report.Benchmark.Context);
        }

        [Fact]
        public void Report_CarriesCandidateIdentity()
        {
            var report = CreateReportWithDiscrepancy();

            Assert.NotNull(report.Candidate);
            Assert.NotNull(report.Candidate.Source);
            Assert.NotNull(report.Candidate.Context);
        }

        [Fact]
        public void Report_CarriesResolutionTimestamp()
        {
            var before = DateTimeOffset.UtcNow;
            var report = CreateReportWithDiscrepancy();
            var after = DateTimeOffset.UtcNow;

            Assert.True(report.ResolutionTimestamp >= before);
            Assert.True(report.ResolutionTimestamp <= after);
        }

        #region Test Helpers

        private static FieldDiscrepancy CreateDiscrepancy(
            DateTimeOffset timestamp,
            OhlcvField field = OhlcvField.Open,
            decimal benchmarkValue = 0.63421m,
            decimal candidateValue = 0.63471m,
            decimal resolvedAbsolute = 0.00010m,
            decimal resolvedRelative = 0.0001m,
            long? candidateSourceLine = null)
        {
            var difference = benchmarkValue - candidateValue;
            var absoluteDifference = difference < 0 ? -difference : difference;

            return new FieldDiscrepancy(
                timestamp,
                field,
                benchmarkValue,
                candidateValue,
                absoluteDifference,
                candidateValue - benchmarkValue,
                resolvedAbsolute,
                resolvedRelative,
                new ToleranceDecision.MaterialDifference(),
                candidateSourceLine);
        }

        private static ComparisonReport CreateReportWithDiscrepancy()
        {
            var benchmark = CreateBenchmark("test");
            var candidateIdentity = CreateCandidateIdentity();
            var config = ToleranceResolver.Resolve(null, "test");
            var coverage = new ComparisonCoverage(5, 5, 5, 0, 0);

            var discrepancies = new List<FieldDiscrepancy>
            {
                CreateDiscrepancy(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero))
            };

            var toleratedSummary = config.Fields.Select(f =>
                new ToleratedDifferenceAggregate(f.Field, 5, 4, 4, 0, 1)).ToList();

            var agreementScore = BenchmarkAgreementScore.Available(5, 1);

            return new ComparisonReport(
                benchmark, candidateIdentity, config, coverage,
                discrepancies, toleratedSummary,
                null, agreementScore, resolutionTimestamp: DateTimeOffset.UtcNow);
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
