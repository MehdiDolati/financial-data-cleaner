using System;
using Validator.Domain.Timeframes;

namespace Validator.Domain.Findings.Evidence
{
    // Evidence for one expected-but-absent candle. The expected record has no
    // physical source line; neighboring observed timestamps provide context.
    public sealed record MissingCandleEvidence
    {
        public DateTimeOffset ExpectedTimestampUtc { get; }
        public Timeframe ExpectedTimeframe { get; }
        public FindingReference TimeGapReference { get; }
        public DateTimeOffset? PreviousObservedTimestampUtc { get; }
        public DateTimeOffset? NextObservedTimestampUtc { get; }

        public MissingCandleEvidence(
            DateTimeOffset expectedTimestampUtc,
            Timeframe expectedTimeframe,
            FindingReference timeGapReference,
            DateTimeOffset? previousObservedTimestampUtc = null,
            DateTimeOffset? nextObservedTimestampUtc = null)
        {
            if (expectedTimestampUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Expected timestamp must be UTC.", nameof(expectedTimestampUtc));
            }

            if (expectedTimeframe is null)
            {
                throw new ArgumentNullException(nameof(expectedTimeframe));
            }

            if (timeGapReference is null)
            {
                throw new ArgumentNullException(nameof(timeGapReference));
            }

            RequireUtc(previousObservedTimestampUtc, nameof(previousObservedTimestampUtc));
            RequireUtc(nextObservedTimestampUtc, nameof(nextObservedTimestampUtc));

            ExpectedTimestampUtc = expectedTimestampUtc;
            ExpectedTimeframe = expectedTimeframe;
            TimeGapReference = timeGapReference;
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