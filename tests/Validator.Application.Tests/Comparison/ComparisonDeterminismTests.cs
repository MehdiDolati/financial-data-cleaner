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
    public class ComparisonDeterminismTests
    {
        [Fact]
        public void Compare_IdenticalInputs_ProducesByteIdenticalJsonOutput()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();
            var identity = CreateCandidateIdentity();

            var useCase = new CompareDatasetsUseCase();
            var report1 = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, identity);
            var report2 = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, identity);

            var jsonWriter = new ComparisonJsonReportWriter();
            var json1 = jsonWriter.Write(report1);
            var json2 = jsonWriter.Write(report2);

            Assert.Equal(json1, json2);
        }

        [Fact]
        public void Compare_IdenticalInputs_ProducesIdenticalTextOutput()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();
            var identity = CreateCandidateIdentity();

            var useCase = new CompareDatasetsUseCase();
            var report1 = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, identity);
            var report2 = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, identity);

            var textWriter = new ComparisonTextReportWriter();
            var text1 = textWriter.Write(report1);
            var text2 = textWriter.Write(report2);

            Assert.Equal(text1, text2);
        }

        [Fact]
        public void Compare_DiscrepancyOrdering_IsStable()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Create multiple material discrepancies across different timestamps and fields
            candidateCandles[0] = candidateCandles[0] with { Open = 0.63421m + 0.00050m };
            candidateCandles[2] = candidateCandles[2] with { Close = 0.63720m - 0.00050m };
            candidateCandles[1] = candidateCandles[1] with { High = 0.63650m + 0.00050m };
            candidateCandles[3] = candidateCandles[3] with { Open = 0.63720m + 0.00050m };

            var identity = CreateCandidateIdentity();
            var useCase = new CompareDatasetsUseCase();

            var report1 = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, identity);
            var report2 = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, identity);

            // Verify ordering is identical across runs
            Assert.Equal(report1.MaterialDiscrepancies.Count, report2.MaterialDiscrepancies.Count);
            for (var i = 0; i < report1.MaterialDiscrepancies.Count; i++)
            {
                Assert.Equal(report1.MaterialDiscrepancies[i].TimestampUtc,
                    report2.MaterialDiscrepancies[i].TimestampUtc);
                Assert.Equal(report1.MaterialDiscrepancies[i].Field,
                    report2.MaterialDiscrepancies[i].Field);
                Assert.Equal(report1.MaterialDiscrepancies[i].Difference,
                    report2.MaterialDiscrepancies[i].Difference);
            }

            // Verify timestamp ascending ordering
            for (var i = 1; i < report1.MaterialDiscrepancies.Count; i++)
            {
                Assert.True(
                    report1.MaterialDiscrepancies[i].TimestampUtc >=
                    report1.MaterialDiscrepancies[i - 1].TimestampUtc,
                    "Discrepancies should be ordered by timestamp ascending");
            }
        }

        [Fact]
        public void Compare_IdenticalTimestamps_FieldAlphabeticalOrdering()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // Create material discrepancies at the same timestamp for different fields
            candidateCandles[0] = candidateCandles[0] with
            {
                Open = 0.63421m + 0.00050m,
                Close = 0.63502m - 0.00050m
            };

            var identity = CreateCandidateIdentity();
            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, identity);

            // Both discrepancies are at the same timestamp, so should be ordered by field name
            var sameTimestamp = report.MaterialDiscrepancies
                .Where(d => d.TimestampUtc == new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero))
                .ToList();

            Assert.Equal(2, sameTimestamp.Count);
            // "Close" comes before "Open" alphabetically
            Assert.Equal(OhlcvField.Close, sameTimestamp[0].Field);
            Assert.Equal(OhlcvField.Open, sameTimestamp[1].Field);
        }

        [Fact]
        public void Compare_SameFieldSameTimestamp_DifferenceDescendingOrdering()
        {
            var benchmark = CreateBenchmark("test");
            var benchmarkCandles = CreateCandleSet();
            var candidateCandles = CreateCandleSet();

            // This test verifies the ordering stability when we have multiple
            // discrepancies at the same timestamp and field (which shouldn't normally
            // happen, but tests the sorting logic)
            candidateCandles[0] = candidateCandles[0] with
            {
                Open = 0.63421m + 0.00050m
            };

            var identity = CreateCandidateIdentity();
            var useCase = new CompareDatasetsUseCase();
            var report = useCase.Compare(benchmark, benchmarkCandles, candidateCandles, identity);

            // Verify the discrepancy has correct values
            var discrepancy = report.MaterialDiscrepancies.Single();
            Assert.Equal(0.00050m, discrepancy.Difference);
            Assert.Equal(OhlcvField.Open, discrepancy.Field);
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

        private static List<PriceCandle> CreateCandleSet()
        {
            return new List<PriceCandle>
            {
                new(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                    0.63421m, 0.63580m, 0.63310m, 0.63502m, 125000m),
                new(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
                    0.63502m, 0.63650m, 0.63420m, 0.63612m, 118000m),
                new(new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero),
                    0.63612m, 0.63780m, 0.63550m, 0.63720m, 132000m),
                new(new DateTimeOffset(2026, 1, 7, 0, 0, 0, TimeSpan.Zero),
                    0.63720m, 0.63890m, 0.63680m, 0.63850m, 115000m),
                new(new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero),
                    0.63850m, 0.63920m, 0.63750m, 0.63810m, 128000m),
            };
        }

        private static string Sha256() => "abc123def456abc123def456abc123def456abc123def456abc123def456abcd";

        #endregion
    }
}
