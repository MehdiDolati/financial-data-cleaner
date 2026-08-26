using System;
using System.Collections.Generic;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;

namespace Validator.Application.Validation
{
    // One contiguous run of missing candles together with the missing-candle
    // findings it contains. The gap contributes exactly one to the time-gaps
    // count while each contained candle contributes one to the missing-candles
    // count, so the two categories never double-count each other.
    public sealed record TimeGapEvidenceSet(
        FindingReference Reference,
        DetailedFindingHeader Header,
        TimeGapEvidence Evidence,
        IReadOnlyList<FindingEvidenceRecord> Records,
        IReadOnlyList<FindingRelationship> Relationships,
        long MissingCandleCount);

    // Derives a time gap from two adjacent observed timestamps. Missing-candle
    // references are streamed as child records rather than nested collections,
    // so a gap spanning an arbitrarily large number of expected slots stays
    // bounded in memory, and both directions of every gap/candle edge are
    // emitted.
    public static class TimeGapProcessor
    {
        public static bool TryBuild(
            DateTimeOffset previousObservedUtc,
            DateTimeOffset nextObservedUtc,
            Timeframe timeframe,
            out TimeGapEvidenceSet? gap,
            long? previousObservedSourceLine = null,
            long? nextObservedSourceLine = null)
        {
            if (timeframe is null)
            {
                throw new ArgumentNullException(nameof(timeframe));
            }

            if (previousObservedUtc.Offset != TimeSpan.Zero || nextObservedUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Observed timestamps must be UTC.");
            }

            if (nextObservedUtc <= previousObservedUtc)
            {
                throw new ArgumentException("The next observed timestamp must follow the previous one.", nameof(nextObservedUtc));
            }

            var step = timeframe.Duration;
            var elapsed = nextObservedUtc - previousObservedUtc;
            var missingCount = (long)(elapsed.Ticks / step.Ticks) - 1;
            if (missingCount <= 0)
            {
                gap = null;
                return false;
            }

            var firstMissing = previousObservedUtc + step;
            var lastMissing = previousObservedUtc + step * missingCount;
            var reference = FindingReferenceFactory.TimeGap(firstMissing, lastMissing);

            var evidence = new TimeGapEvidence(
                firstMissing,
                lastMissing,
                timeframe,
                missingCount,
                (long)elapsed.TotalSeconds,
                previousObservedUtc,
                nextObservedUtc,
                previousObservedSourceLine,
                nextObservedSourceLine);

            var records = new List<FindingEvidenceRecord>
            {
                new FindingEvidenceRecord.TimeGapHeader(reference, evidence)
            };

            var relationships = new List<FindingRelationship>();
            var childOrder = 1L;
            foreach (var candle in MissingCandleProcessor.Generate(
                previousObservedUtc,
                nextObservedUtc,
                timeframe,
                reference,
                previousObservedSourceLine,
                nextObservedSourceLine))
            {
                records.Add(new FindingEvidenceRecord.TimeGapMissingReference(
                    reference,
                    candle.Reference,
                    childOrder++));
                relationships.Add(new FindingRelationship(
                    RelationshipKind.ContainsMissingCandle,
                    candle.Reference));
            }

            var header = new DetailedFindingHeader(
                reference,
                FindingCategory.TimeGap,
                "Time gap",
                $"{missingCount} consecutive {timeframe} candles are absent between "
                    + $"{FindingReferenceFactory.UtcKey(previousObservedUtc)} and {FindingReferenceFactory.UtcKey(nextObservedUtc)}.",
                countContribution: 1,
                // A gap spans expected slots only; it cites no physical line.
                new FindingLocation(Array.Empty<long>(), firstMissing),
                EvidenceKind.TimeGap,
                "Backfill the absent interval from the authoritative source or confirm a scheduled closure covers it.");

            gap = new TimeGapEvidenceSet(reference, header, evidence, records, relationships, missingCount);
            return true;
        }

        // Streams the missing-candle findings of one gap so a caller can spool
        // each contained candle without materializing the whole gap.
        public static IEnumerable<MissingCandleEvidenceSet> MissingCandlesOf(
            DateTimeOffset previousObservedUtc,
            DateTimeOffset nextObservedUtc,
            Timeframe timeframe,
            FindingReference gapReference,
            long? previousObservedSourceLine = null,
            long? nextObservedSourceLine = null) =>
            MissingCandleProcessor.Generate(
                previousObservedUtc,
                nextObservedUtc,
                timeframe,
                gapReference,
                previousObservedSourceLine,
                nextObservedSourceLine);
    }
}
