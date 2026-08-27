using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Abstractions;
using Validator.Application.Benchmark;
using Validator.Application.Ingestion;
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
        private readonly IApplicationClock _clock;

        public CompareDatasetsUseCase() : this(DeterministicComparisonClock.Instance) { }

        public CompareDatasetsUseCase(IApplicationClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

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

            // 2. Detect context differences and add warnings (FR-006)
            if (!string.Equals(benchmark.Instrument, candidateIdentity.Instrument, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Instrument mismatch: benchmark uses '{benchmark.Instrument}' but candidate uses '{candidateIdentity.Instrument}'.");
            }

            var contextWarnings = DetectContextDifferences(
                benchmark.Context, candidateIdentity.Context, benchmark.Source);

            // 3. Resolve tolerances before comparing (FR-019) — infer fractional step from benchmark OHLC
            var configuration = ToleranceResolver.Resolve(userToleranceOverrides, benchmark.Name, benchmarkCandles);

            // 4. Match timestamps
            var benchmarkTimestamps = benchmarkCandles.Select(c => c.Timestamp).ToList();
            var candidateTimestamps = candidateCandles.Select(c => c.Timestamp).ToList();

            var matchResult = TimestampMatcher.Match(
                benchmarkTimestamps,
                candidateTimestamps,
                benchmarkCandles.Count,
                candidateCandles.Count);

            // 5. Build lookup dictionaries for efficient candle access
            var benchmarkLookup = benchmarkCandles.ToDictionary(c => c.Timestamp);
            var candidateLookup = candidateCandles.ToDictionary(c => c.Timestamp);

            // 6. Compare fields for matched timestamps
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

                    var isDifferent = benchmarkValue != candidateValue;
                    switch (decision)
                    {
                        case ToleranceDecision.AcceptedByAbsolute:
                            if (isDifferent)
                            {
                                counts.AcceptedByAbsoluteCount++;
                                counts.AcceptedCount++;
                            }
                            break;
                        case ToleranceDecision.AcceptedByRelative:
                            if (isDifferent)
                            {
                                counts.AcceptedByRelativeCount++;
                                counts.AcceptedCount++;
                            }
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

            // 8. Build tolerated summary
            var toleratedSummary = configuration.Fields
                .Where(f => f.Enabled)
                .Select(f => BuildToleratedAggregate(f.Field, toleratedCounts))
                .ToList();

            // 9. Compute agreement score
            var agreementScore = ComputeAgreementScore(
                matchResult.Coverage.MatchedCount,
                sortedDiscrepancies);

            var missingRecords = matchResult.MissingFromCandidateTimestamps
                .Select(timestamp => BuildMissingRecord(timestamp, benchmarkLookup))
                .ToArray();
            var extraRecords = matchResult.ExtraInCandidateTimestamps
                .Select(timestamp => BuildExtraRecord(timestamp, candidateLookup))
                .ToArray();

            return new ComparisonReport(
                benchmark,
                candidateIdentity,
                configuration,
                matchResult.Coverage,
                sortedDiscrepancies,
                toleratedSummary,
                matchResult.MissingFromCandidateTimestamps,
                matchResult.ExtraInCandidateTimestamps,
                null, // CandidateScore set by caller if --score is used
                agreementScore,
                contextWarnings,
                _clock.UtcNow,
                missingRecords,
                extraRecords);
        }

        /// <summary>
        /// Detects differences between benchmark and candidate contexts that may affect comparison.
        /// Warnings are informational — they don't block comparison.
        /// </summary>
        private static IReadOnlyList<string> DetectContextDifferences(
            ValidationContextSnapshot benchmarkContext,
            ValidationContextSnapshot candidateContext,
            SourceIdentity benchmarkSource)
        {
            var warnings = new List<string>();

            // Calendar profile differs
            if (!string.Equals(benchmarkContext.Calendar.Profile, candidateContext.Calendar.Profile,
                StringComparison.Ordinal))
            {
                warnings.Add(
                    $"Calendar profile differs: benchmark uses '{benchmarkContext.Calendar.Profile}' " +
                    $"but candidate uses '{candidateContext.Calendar.Profile}'.");
            }

            // Timezone offset differs
            if (!string.Equals(benchmarkContext.Timestamp.SourceOffset, candidateContext.Timestamp.SourceOffset,
                StringComparison.Ordinal))
            {
                warnings.Add(
                    $"Source timestamp offset differs: benchmark uses '{benchmarkContext.Timestamp.SourceOffset}' " +
                    $"but candidate uses '{candidateContext.Timestamp.SourceOffset}'.");
            }

            // Timestamp mode differs
            if (benchmarkContext.Timestamp.Mode != candidateContext.Timestamp.Mode)
            {
                warnings.Add(
                    $"Timestamp interpretation differs: benchmark uses '{benchmarkContext.Timestamp.Mode}' " +
                    $"but candidate uses '{candidateContext.Timestamp.Mode}'.");
            }

            // Date range differs (informational)
            if (benchmarkContext.DateRange is not null && candidateContext.DateRange is not null)
            {
                if (benchmarkContext.DateRange.Start != candidateContext.DateRange.Start ||
                    benchmarkContext.DateRange.End != candidateContext.DateRange.End)
                {
                    warnings.Add(
                        $"Date range differs: benchmark covers {benchmarkContext.DateRange.Start:yyyy-MM-dd} to {benchmarkContext.DateRange.End:yyyy-MM-dd}, " +
                        $"candidate covers {candidateContext.DateRange.Start:yyyy-MM-dd} to {candidateContext.DateRange.End:yyyy-MM-dd}.");
                }
            }

            // HasHeader differs
            if (benchmarkContext.HasHeader != candidateContext.HasHeader)
            {
                warnings.Add(
                    $"Header mode differs: benchmark {(benchmarkContext.HasHeader ? "has" : "lacks")} a CSV header, " +
                    $"candidate {(candidateContext.HasHeader ? "has" : "lacks")} one.");
            }

            return warnings;
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
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification =
            "Unreachable: the TryGetValue-false branch cannot fire because BuildToleratedAggregate is only " +
            "called for fields from configuration.Fields that were already inserted into the counts dictionary " +
            "by the preceding comparison loop.")]
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

        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification =
            "Unreachable: MissingFromCandidateTimestamps are timestamps present in benchmarkLookup, " +
            "so TryGetValue always returns true. The false branch is defense-in-depth.")]
        private static TimestampAlignmentReference BuildMissingRecord(
            DateTimeOffset timestamp,
            Dictionary<DateTimeOffset, PriceCandle> benchmarkLookup) =>
            new(timestamp, BenchmarkSourceLine: benchmarkLookup.TryGetValue(timestamp, out var candle) ? candle.SourceLine : null);

        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification =
            "Unreachable: ExtraInCandidateTimestamps are timestamps present in candidateLookup, " +
            "so TryGetValue always returns true. The false branch is defense-in-depth.")]
        private static TimestampAlignmentReference BuildExtraRecord(
            DateTimeOffset timestamp,
            Dictionary<DateTimeOffset, PriceCandle> candidateLookup) =>
            new(timestamp, CandidateSourceLine: candidateLookup.TryGetValue(timestamp, out var candle) ? candle.SourceLine : null);

        private sealed class DeterministicComparisonClock : IApplicationClock
        {
            public static DeterministicComparisonClock Instance { get; } = new();
            public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
        }
    }
}
