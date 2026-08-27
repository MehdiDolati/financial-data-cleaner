using System;
using System.Collections.Generic;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;

namespace Validator.Application.Validation
{
    // One expected-but-absent candle with its evidence, relationship edge back
    // to the owning gap, and no invented physical source line.
    public sealed record MissingCandleEvidenceSet(
        FindingReference Reference,
        DetailedFindingHeader Header,
        FindingEvidenceRecord Evidence,
        FindingRelationship PartOfGap);

    // Generates the missing-candle findings of one contiguous gap. Each expected
    // slot yields exactly one finding contributing one to the missing-candles
    // count, carries the neighboring observed timestamps for context, and links
    // back to its gap without ever claiming a physical line.
    public static class MissingCandleProcessor
    {
        public static IEnumerable<MissingCandleEvidenceSet> Generate(
            DateTimeOffset previousObservedUtc,
            DateTimeOffset nextObservedUtc,
            Timeframe timeframe,
            FindingReference gapReference,
            long? previousObservedSourceLine = null,
            long? nextObservedSourceLine = null)
        {
            if (timeframe is null)
            {
                throw new ArgumentNullException(nameof(timeframe));
            }

            if (gapReference is null)
            {
                throw new ArgumentNullException(nameof(gapReference));
            }

            if (previousObservedUtc.Offset != TimeSpan.Zero || nextObservedUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Observed timestamps must be UTC.");
            }

            if (nextObservedUtc <= previousObservedUtc)
            {
                throw new ArgumentException("The next observed timestamp must follow the previous one.", nameof(nextObservedUtc));
            }

            return Enumerate(
                previousObservedUtc,
                nextObservedUtc,
                timeframe,
                gapReference,
                previousObservedSourceLine,
                nextObservedSourceLine);
        }

        private static IEnumerable<MissingCandleEvidenceSet> Enumerate(
            DateTimeOffset previousObservedUtc,
            DateTimeOffset nextObservedUtc,
            Timeframe timeframe,
            FindingReference gapReference,
            long? previousObservedSourceLine,
            long? nextObservedSourceLine)
        {
            // Every candle in one gap carries the same bracketing pair as the gap
            // itself, so each absence points at the same two real neighbouring
            // rows rather than at a per-candle guess.
            for (var expected = previousObservedUtc + timeframe.Duration;
                 expected < nextObservedUtc;
                 expected += timeframe.Duration)
            {
                yield return Build(
                    expected,
                    timeframe,
                    gapReference,
                    previousObservedUtc,
                    nextObservedUtc,
                    previousObservedSourceLine,
                    nextObservedSourceLine);
            }
        }

        public static MissingCandleEvidenceSet Build(
            DateTimeOffset expectedUtc,
            Timeframe timeframe,
            FindingReference gapReference,
            DateTimeOffset? previousObservedUtc = null,
            DateTimeOffset? nextObservedUtc = null,
            long? previousObservedSourceLine = null,
            long? nextObservedSourceLine = null)
        {
            var reference = FindingReferenceFactory.MissingCandle(expectedUtc);
            var evidence = new MissingCandleEvidence(
                expectedUtc,
                timeframe,
                gapReference,
                previousObservedUtc,
                nextObservedUtc,
                previousObservedSourceLine,
                nextObservedSourceLine);

            var header = new DetailedFindingHeader(
                reference,
                FindingCategory.MissingCandle,
                "Missing candle",
                $"No {timeframe} candle was present for expected timestamp {FindingReferenceFactory.UtcKey(expectedUtc)}.",
                countContribution: 1,
                // An expected-but-absent record has no physical line to cite.
                new FindingLocation(Array.Empty<long>(), expectedUtc),
                EvidenceKind.MissingCandle,
                "Backfill the missing candle from the authoritative source or confirm the market was closed.");

            return new MissingCandleEvidenceSet(
                reference,
                header,
                new FindingEvidenceRecord.MissingCandle(reference, evidence),
                new FindingRelationship(RelationshipKind.PartOfGap, gapReference));
        }
    }
}
