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

        public TimeGapEvidence(
            DateTimeOffset firstMissingTimestampUtc,
            DateTimeOffset lastMissingTimestampUtc,
            Timeframe expectedTimeframe,
            long missingCandleCount,
            long elapsedSeconds,
            DateTimeOffset? previousObservedTimestampUtc = null,
            DateTimeOffset? nextObservedTimestampUtc = null)
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

            FirstMissingTimestampUtc = firstMissingTimestampUtc;
            LastMissingTimestampUtc = lastMissingTimestampUtc;
            ExpectedTimeframe = expectedTimeframe;
            MissingCandleCount = missingCandleCount;
            ElapsedSeconds = elapsedSeconds;
            PreviousObservedTimestampUtc = previousObservedTimestampUtc;
            NextObservedTimestampUtc = nextObservedTimestampUtc;
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