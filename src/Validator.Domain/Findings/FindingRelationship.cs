using System;

namespace Validator.Domain.Findings
{
    public static class RelationshipKind
    {
        public const string PartOfGap = "PartOfGap";
        public const string ContainsMissingCandle = "ContainsMissingCandle";
    }

    // Deterministic cross-reference between two findings, such as a time gap
    // and one of its missing candles. Both directional edges are always stored.
    public sealed record FindingRelationship
    {
        public string Kind { get; }
        public FindingReference TargetReference { get; }

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