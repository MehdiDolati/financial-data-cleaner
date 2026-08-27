using System;
using Validator.Domain.Timeframes;

namespace Validator.Domain.Findings.Evidence
{
    // Evidence for one contiguous run of missing candles. Child references to
    // every missing candle in the gap are streamed separately and must equal
    // MissingCandleCount, while the gap itself contributes one count.
    public sealed record TimeGapEvidence
    {
        public DateTimeOffset FirstMissingTimestampUtc { get; }
        public DateTimeOffset LastMissingTimestampUtc { get; }
        public Timeframe ExpectedTimeframe { get; }
        public long MissingCandleCount { get; }
        public long ElapsedSeconds { get; }
        public DateTimeOffset? PreviousObservedTimestampUtc { get; }
        public DateTimeOffset? NextObservedTimestampUtc { get; }

        // Physical source lines of the observed records bracketing the gap, so
        // the absence can be located in the file without inventing a line for it
        // (FR-039). When a bracketing timestamp occurs on several rows these
        // resolve to the tightest bracket: the highest line sharing the preceding
        // timestamp and the lowest line sharing the following one. Because
        // unsorted input is accepted, the two need not be consecutive or
        // ascending — they identify the temporal neighbours, not physically
        // adjacent rows. A boundary gap leaves the unavailable side absent.
        public long? PreviousObservedSourceLine { get; }
        public long? NextObservedSourceLine { get; }

        public TimeGapEvidence(
            DateTimeOffset firstMissingTimestampUtc,
            DateTimeOffset lastMissingTimestampUtc,
            Timeframe expectedTimeframe,
            long missingCandleCount,
            long elapsedSeconds,
            DateTimeOffset? previousObservedTimestampUtc = null,
            DateTimeOffset? nextObservedTimestampUtc = null,
            long? previousObservedSourceLine = null,
            long? nextObservedSourceLine = null)
        {
            if (firstMissingTimestampUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("First missing timestamp must be UTC.", nameof(firstMissingTimestampUtc));
            }

            if (lastMissingTimestampUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Last missing timestamp must be UTC.", nameof(lastMissingTimestampUtc));
            }

            if (lastMissingTimestampUtc < firstMissingTimestampUtc)
            {
                throw new ArgumentException("Last missing timestamp must not precede the first.", nameof(lastMissingTimestampUtc));
            }

            if (expectedTimeframe is null)
            {
                throw new ArgumentNullException(nameof(expectedTimeframe));
            }

            if (missingCandleCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(missingCandleCount), "Missing candle count must be positive.");
            }

            if (elapsedSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Elapsed seconds must be positive.");
            }

            RequireUtc(previousObservedTimestampUtc, nameof(previousObservedTimestampUtc));
            RequireUtc(nextObservedTimestampUtc, nameof(nextObservedTimestampUtc));
            AbsenceAnchor.RequirePairedLine(
                previousObservedSourceLine,
                previousObservedTimestampUtc,
                nameof(previousObservedSourceLine),
                nameof(previousObservedTimestampUtc));
            AbsenceAnchor.RequirePairedLine(
                nextObservedSourceLine,
                nextObservedTimestampUtc,
                nameof(nextObservedSourceLine),
                nameof(nextObservedTimestampUtc));

            FirstMissingTimestampUtc = firstMissingTimestampUtc;
            LastMissingTimestampUtc = lastMissingTimestampUtc;
            ExpectedTimeframe = expectedTimeframe;
            MissingCandleCount = missingCandleCount;
            ElapsedSeconds = elapsedSeconds;
            PreviousObservedTimestampUtc = previousObservedTimestampUtc;
            NextObservedTimestampUtc = nextObservedTimestampUtc;
            PreviousObservedSourceLine = previousObservedSourceLine;
            NextObservedSourceLine = nextObservedSourceLine;
        }

        private static void RequireUtc(DateTimeOffset? value, string parameterName)
        {
            if (value.HasValue && value.Value.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Observed timestamp must be UTC.", parameterName);
            }
        }
    }
}