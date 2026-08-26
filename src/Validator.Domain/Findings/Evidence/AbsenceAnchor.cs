using System;

namespace Validator.Domain.Findings.Evidence
{
    // Shared invariant for the bracketing observed source lines that locate an
    // expected-but-absent record (FR-039, FR-040). Both MissingCandleEvidence
    // and TimeGapEvidence carry the same pair, so the rule lives in one place
    // rather than being restated — and enforced inconsistently — in each type.
    internal static class AbsenceAnchor
    {
        // A bracketing line describes a real observed row, so it is present
        // exactly when its paired observed timestamp is present and is always a
        // positive physical line. An absent side is left absent rather than
        // encoded as zero or a negative sentinel.
        internal static void RequirePairedLine(
            long? sourceLine,
            DateTimeOffset? pairedTimestampUtc,
            string sourceLineParameterName,
            string pairedTimestampParameterName)
        {
            if (!sourceLine.HasValue)
            {
                return;
            }

            if (!pairedTimestampUtc.HasValue)
            {
                throw new ArgumentException(
                    $"A bracketing source line requires its paired '{pairedTimestampParameterName}' to be present.",
                    sourceLineParameterName);
            }

            if (sourceLine.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    sourceLineParameterName,
                    "A bracketing source line must be positive.");
            }
        }
    }
}