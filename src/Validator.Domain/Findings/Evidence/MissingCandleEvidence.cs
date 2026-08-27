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

        // Physical source line of the observed record that immediately precedes
        // the absence, and of the one that immediately follows it. These locate
        // an absent record without inventing a line for it (FR-039): they belong
        // to real neighbouring rows and never enter FindingLocation.SourceLines.
        // Each is present exactly when its paired observed timestamp is present,
        // so a boundary gap leaves the unavailable side absent (FR-040).
        public long? PreviousObservedSourceLine { get; }
        public long? NextObservedSourceLine { get; }

        public MissingCandleEvidence(
            DateTimeOffset expectedTimestampUtc,
            Timeframe expectedTimeframe,
            FindingReference timeGapReference,
            DateTimeOffset? previousObservedTimestampUtc = null,
            DateTimeOffset? nextObservedTimestampUtc = null,
            long? previousObservedSourceLine = null,
            long? nextObservedSourceLine = null)
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

            ExpectedTimestampUtc = expectedTimestampUtc;
            ExpectedTimeframe = expectedTimeframe;
            TimeGapReference = timeGapReference;
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