using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Benchmark;
using Validator.Application.Scoring;
using Validator.Domain.Candles;
using Validator.Domain.Comparison;

namespace Validator.Application.Comparison
{
    /// <summary>
    /// Orchestrates the comparison of a candidate dataset against a named benchmark.
    /// Validates timeframe compatibility (FR-006 hard fail), resolves tolerances,
    /// matches timestamps, compares fields, builds ComparisonReport with ordered
    /// discrepancies, and computes BenchmarkAgreementScore. Fails safe on any error (FR-030).
    /// </summary>
    public sealed class CompareDatasetsUseCase
    {
        /// <summary>
        /// Compares pre-loaded candidate candles against a loaded benchmark snapshot.
        /// Tolerance resolution happens before comparison (FR-019).
        /// </summary>
        /// <param name="benchmark">The loaded benchmark snapshot.</param>
        /// <param name="benchmarkCandles">Pre-loaded benchmark candles (sorted by timestamp).</param>
        /// <param name="candidateCandles">Pre-loaded candidate candles (sorted by timestamp).</param>
        /// <param name="candidateIdentity">Identity of the candidate dataset.</param>
        /// <param name="userToleranceOverrides">Optional user tolerance overrides.</param>
        /// <returns>A complete ComparisonReport with discrepancies, scores, and coverage.</returns>
        public ComparisonReport Compare(
            BenchmarkSnapshot benchmark,
            IReadOnlyList<PriceCandle> benchmarkCandles,
            IReadOnlyList<PriceCandle> candidateCandles,
            CandidateIdentity candidateIdentity,
            IReadOnlyList<ComparedField>? userToleranceOverrides = null)
        {
            ArgumentNullException.ThrowIfNull(benchmark);
            ArgumentNullException.ThrowIfNull(benchmarkCandles);
            ArgumentNullException.ThrowIfNull(candidateCandles);
            ArgumentNullException.ThrowIfNull(candidateIdentity);

            // 1. Validate timeframe compatibility (FR-006 hard fail)
            if (!string.Equals(benchmark.Context.Timeframe, candidateIdentity.Context.Timeframe,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Timeframe mismatch: benchmark uses '{benchmark.Context.Timeframe}' " +
                    $"but candidate uses '{candidateIdentity.Context.Timeframe}'. " +
                    $"Timeframe must match for comparison (FR-006).");
            }

            // 2. Resolve tolerances before comparing (FR-019)
            var configuration = ToleranceResolver.Resolve(userToleranceOverrides, benchmark.Name);

            // 3. Match timestamps
            var benchmarkTimestamps = benchmarkCandles.Select(c => c.Timestamp).ToList();
            var candidateTimestamps = candidateCandles.Select(c => c.Timestamp).ToList();

            var matchResult = TimestampMatcher.Match(
                benchmarkTimestamps,
                candidateTimestamps,
                benchmarkCandles.Count,
                candidateCandles.Count);

            // 4. Build lookup dictionaries for efficient candle access
            var benchmarkLookup = benchmarkCandles.ToDictionary(c => c.Timestamp);
            var candidateLookup = candidateCandles.ToDictionary(c => c.Timestamp);

            // 5. Compare fields for matched timestamps
            var allDiscrepancies = new List<FieldDiscrepancy>();
            var toleratedCounts = new Dictionary<OhlcvField, FieldToleranceCounts>();

            foreach (var field in configuration.Fields.Where(f => f.Enabled))
            {
                var counts = new FieldToleranceCounts { Field = field.Field };
                toleratedCounts[field.Field] = counts;

                foreach (var timestamp in matchResult.MatchedTimestamps)
                {
                    var benchmarkCandle = benchmarkLookup[timestamp];
                    var candidateCandle = candidateLookup[timestamp];

                    var benchmarkValue = GetFieldValue(benchmarkCandle, field.Field);
                    var candidateValue = GetFieldValue(candidateCandle, field.Field);

                    var decision = FieldComparator.Compare(
                        benchmarkValue, candidateValue,
                        field.ResolvedAbsolute, field.ResolvedRelative);

                    counts.TotalCompared++;

                    switch (decision)
                    {
                        case ToleranceDecision.AcceptedByAbsolute:
                            counts.AcceptedByAbsoluteCount++;
                            counts.AcceptedCount++;
                            break;
                        case ToleranceDecision.AcceptedByRelative:
                            counts.AcceptedByRelativeCount++;
                            counts.AcceptedCount++;
                            break;
                        case ToleranceDecision.MaterialDifference:
                            counts.MaterialCount++;
                            allDiscrepancies.Add(FieldComparator.CreateDiscrepancy(
                                timestamp, field.Field,
                                benchmarkValue, candidateValue,
                                field.ResolvedAbsolute, field.ResolvedRelative,
                                candidateCandle.SourceLine));
                            break;
                    }
                }
            }

            // 6. Sort material discrepancies: timestamp ascending, field alphabetically, difference descending
            var sortedDiscrepancies = allDiscrepancies
                .OrderBy(d => d.TimestampUtc)
                .ThenBy(d => d.Field.ToString())
                .ThenByDescending(d => d.Difference)
                .ToList();

            // 7. Build tolerated summary
            var toleratedSummary = configuration.Fields
                .Where(f => f.Enabled)
                .Select(f => BuildToleratedAggregate(f.Field, toleratedCounts))
                .ToList();

            // 8. Compute agreement score
            var agreementScore = ComputeAgreementScore(
                matchResult.Coverage.MatchedCount,
                sortedDiscrepancies);

            return new ComparisonReport(
                benchmark,
                candidateIdentity,
                configuration,
                matchResult.Coverage,
                sortedDiscrepancies,
                toleratedSummary,
                null, // CandidateScore set by caller if --score is used
                agreementScore,
                DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Gets a field value from a PriceCandle.
        /// </summary>
        internal static decimal GetFieldValue(PriceCandle candle, OhlcvField field) => field switch
        {
            OhlcvField.Open => candle.Open,
            OhlcvField.High => candle.High,
            OhlcvField.Low => candle.Low,
            OhlcvField.Close => candle.Close,
            OhlcvField.Volume => candle.Volume,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        /// <summary>
        /// Builds a ToleratedDifferenceAggregate for a field.
        /// </summary>
        private static ToleratedDifferenceAggregate BuildToleratedAggregate(
            OhlcvField field,
            Dictionary<OhlcvField, FieldToleranceCounts> counts)
        {
            if (!counts.TryGetValue(field, out var c))
            {
                return new ToleratedDifferenceAggregate(field, 0, 0, 0, 0, 0);
            }

            return new ToleratedDifferenceAggregate(
                field,
                c.TotalCompared,
                c.AcceptedCount,
                c.AcceptedByAbsoluteCount,
                c.AcceptedByRelativeCount,
                c.MaterialCount);
        }

        /// <summary>
        /// Computes the benchmark agreement score.
        /// </summary>
        private static BenchmarkAgreementScore ComputeAgreementScore(
            long matchedCount,
            IReadOnlyList<FieldDiscrepancy> discrepancies)
        {
            if (matchedCount <= 0)
            {
                return BenchmarkAgreementScore.Unavailable(
                    "No overlapping timestamps between benchmark and candidate");
            }

            // Count timestamps with at least one material discrepancy
            var timestampsWithMaterial = discrepancies
                .Select(d => d.TimestampUtc)
                .Distinct()
                .Count();

            return BenchmarkAgreementScore.Available(matchedCount, timestampsWithMaterial);
        }

        /// <summary>
        /// Internal counter for per-field tolerance decisions.
        /// </summary>
        private sealed class FieldToleranceCounts
        {
            public OhlcvField Field { get; init; }
            public long TotalCompared { get; set; }
            public long AcceptedCount { get; set; }
            public long AcceptedByAbsoluteCount { get; set; }
            public long AcceptedByRelativeCount { get; set; }
            public long MaterialCount { get; set; }
        }
    }
}
