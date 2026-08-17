using System;
using System.Collections.Generic;
using System.Linq;

namespace Validator.Domain.Findings.Evidence
{
    /// <summary>
    /// Whether repeated records agree with each other.
    /// </summary>
    /// <remarks>
    /// The distinction matters to the reader: identical repeats can be dropped
    /// safely, while conflicting ones require deciding which values are right.
    /// </remarks>
    public enum DuplicateClassification
    {
        /// <summary>Every repeated record holds the same values.</summary>
        Exact = 0,

        /// <summary>The repeated records disagree on at least one field.</summary>
        Conflicting = 1
    }

    // Evidence for one duplicate group. Every participating row is streamed as
    // a separate DuplicateRowEvidence record; a conflicting group also names
    // every OHLCV field whose values differ across the rows.
    public sealed record DuplicateRecordEvidence
    {
        private static readonly string[] KnownFields = ["Open", "High", "Low", "Close", "Volume"];

        public DateTimeOffset SharedTimestampUtc { get; }
        public DuplicateClassification Classification { get; }
        public IReadOnlyList<string> DifferingFields { get; }

        public DuplicateRecordEvidence(
            DateTimeOffset sharedTimestampUtc,
            DuplicateClassification classification,
            IReadOnlyList<string>? differingFields = null)
        {
            if (sharedTimestampUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Shared timestamp must be UTC.", nameof(sharedTimestampUtc));
            }

            var fields = differingFields ?? Array.Empty<string>();
            if (fields.Any(field => !KnownFields.Contains(field)))
            {
                throw new ArgumentException("Differing fields must be established OHLCV field names.", nameof(differingFields));
            }

            if (classification == DuplicateClassification.Exact && fields.Count > 0)
            {
                throw new ArgumentException("An exact duplicate group has no differing fields.", nameof(differingFields));
            }

            if (classification == DuplicateClassification.Conflicting && fields.Count == 0)
            {
                throw new ArgumentException("A conflicting duplicate group names at least one differing field.", nameof(differingFields));
            }

            SharedTimestampUtc = sharedTimestampUtc;
            Classification = classification;
            DifferingFields = fields;
        }
    }
}