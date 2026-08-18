using System;

namespace Validator.Domain.Findings
{
    /// <summary>
    /// The relationship names a report may use between two findings.
    /// </summary>
    /// <remarks>
    /// These are published in report output, so the exact text is part of the
    /// report contract and cannot change without breaking consumers.
    /// </remarks>
    public static class RelationshipKind
    {
        /// <summary>Points from a missing candle to the time gap that contains it.</summary>
        public const string PartOfGap = "PartOfGap";

        /// <summary>Points from a time gap to a missing candle that falls inside it.</summary>
        public const string ContainsMissingCandle = "ContainsMissingCandle";
    }

    /// <summary>
    /// A deterministic cross-reference from one finding to another, such as a
    /// time gap and one of the missing candles inside it.
    /// </summary>
    /// <remarks>
    /// Relationships are stored as both directional edges, so a consumer reading
    /// either finding can reach the other without searching the whole report.
    /// </remarks>
    public sealed record FindingRelationship
    {
        /// <summary>The kind of link, from <see cref="RelationshipKind"/>.</summary>
        public string Kind { get; }

        /// <summary>The finding this edge points at.</summary>
        public FindingReference TargetReference { get; }

        /// <summary>
        /// Creates a relationship edge.
        /// </summary>
        /// <param name="kind">A value declared on <see cref="RelationshipKind"/>.</param>
        /// <param name="targetReference">The finding being pointed at.</param>
        /// <exception cref="ArgumentException">The kind is not a published relationship name.</exception>
        /// <exception cref="ArgumentNullException">The target reference is absent.</exception>
        public FindingRelationship(string kind, FindingReference targetReference)
        {
            if (kind != RelationshipKind.PartOfGap && kind != RelationshipKind.ContainsMissingCandle)
            {
                throw new ArgumentException(
                    "Relationship kind must be PartOfGap or ContainsMissingCandle.",
                    nameof(kind));
            }

            if (targetReference is null)
            {
                throw new ArgumentNullException(nameof(targetReference));
            }

            Kind = kind;
            TargetReference = targetReference;
        }
    }
}