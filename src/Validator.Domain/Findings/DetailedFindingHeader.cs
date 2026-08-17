using System;
using Validator.Domain.Findings.Evidence;

namespace Validator.Domain.Findings
{
    // Common fields of one detailed finding. Evidence and relationships are
    // stored separately and joined by reference, so one duplicate group or gap
    // never creates an unbounded in-memory object.
    public sealed record DetailedFindingHeader
    {
        public FindingReference Reference { get; }
        public FindingCategory Category { get; }
        public string Title { get; }
        public string Explanation { get; }
        public long CountContribution { get; }
        public FindingLocation Location { get; }
        public EvidenceKind EvidenceKind { get; }
        public string SuggestedAction { get; }

        public DetailedFindingHeader(
            FindingReference reference,
            FindingCategory category,
            string title,
            string explanation,
            long countContribution,
            FindingLocation location,
            EvidenceKind evidenceKind,
            string suggestedAction)
        {
            if (reference is null)
            {
                throw new ArgumentNullException(nameof(reference));
            }

            if (!IsEstablishedCategory(category))
            {
                throw new ArgumentException("Finding category must be one of the six established categories.", nameof(category));
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentException("Finding title must be a non-empty value.", nameof(title));
            }

            if (string.IsNullOrWhiteSpace(explanation))
            {
                throw new ArgumentException("Finding explanation must be a non-empty value.", nameof(explanation));
            }

            if (countContribution <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(countContribution), "Count contribution must be positive.");
            }

            if (location is null)
            {
                throw new ArgumentNullException(nameof(location));
            }

            if (EvidenceKindOf(category) != evidenceKind)
            {
                throw new ArgumentException("Evidence kind must correspond to the finding category.", nameof(evidenceKind));
            }

            if (string.IsNullOrWhiteSpace(suggestedAction))
            {
                throw new ArgumentException("Suggested action must be a non-empty value.", nameof(suggestedAction));
            }

            Reference = reference;
            Category = category;
            Title = title;
            Explanation = explanation;
            CountContribution = countContribution;
            Location = location;
            EvidenceKind = evidenceKind;
            SuggestedAction = suggestedAction;
        }

        public static bool IsEstablishedCategory(FindingCategory category) =>
            category is >= FindingCategory.MissingCandle and <= FindingCategory.MalformedRow;

        public static EvidenceKind EvidenceKindOf(FindingCategory category) => category switch
        {
            FindingCategory.MissingCandle => EvidenceKind.MissingCandle,
            FindingCategory.DuplicateRecord => EvidenceKind.DuplicateRecord,
            FindingCategory.InvalidOhlc => EvidenceKind.InvalidOhlc,
            FindingCategory.ClosedMarketRecord => EvidenceKind.ClosedMarketRecord,
            FindingCategory.TimeGap => EvidenceKind.TimeGap,
            FindingCategory.MalformedRow => EvidenceKind.MalformedRow,
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
    }
}