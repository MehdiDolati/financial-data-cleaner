using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;

namespace Validator.Application.Validation
{
    // One physical row participating in a duplicate group, as observed during
    // the single forward scan.
    public sealed record DuplicateCandidateRow(
        long SourceLine,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        decimal Volume,
        string? OriginalTimestampText = null);

    // The complete normalized evidence of one duplicate group: the group header,
    // its differing-field child records, and one child record per participating
    // row. Rows are streamed rather than nested, so an arbitrarily large group
    // never creates an unbounded in-memory object.
    public sealed record DuplicateGroupEvidenceSet(
        FindingReference Reference,
        DuplicateRecordEvidence Header,
        IReadOnlyList<FindingEvidenceRecord> Records,
        IReadOnlyList<long> SourceLines);

    // Builds duplicate-group evidence from every row that shares one timestamp.
    // A group contributes exactly one to the duplicate-records count regardless
    // of how many rows participate, every participating row stays traceable to
    // its physical source line, and a conflicting group names every OHLCV field
    // whose values differ across the group.
    public static class DuplicateGroupProcessor
    {
        private static readonly string[] FieldOrder = ["Open", "High", "Low", "Close", "Volume"];

        public static DuplicateGroupEvidenceSet Build(
            DateTimeOffset sharedTimestampUtc,
            IReadOnlyList<DuplicateCandidateRow> rows)
        {
            if (rows is null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            if (rows.Count < 2)
            {
                throw new ArgumentException("A duplicate group requires at least two participating rows.", nameof(rows));
            }

            var ordered = rows.OrderBy(row => row.SourceLine).ToArray();
            if (ordered.Select(row => row.SourceLine).Distinct().Count() != ordered.Length)
            {
                throw new ArgumentException("Participating rows must have distinct source lines.", nameof(rows));
            }

            var differingFields = DifferingFields(ordered);
            var classification = differingFields.Count == 0
                ? DuplicateClassification.Exact
                : DuplicateClassification.Conflicting;

            var reference = FindingReferenceFactory.DuplicateRecord(sharedTimestampUtc, ordered[0].SourceLine);
            var header = new DuplicateRecordEvidence(sharedTimestampUtc, classification, differingFields);

            var records = new List<FindingEvidenceRecord>
            {
                new FindingEvidenceRecord.DuplicateHeader(reference, header)
            };

            var childOrder = 1L;
            foreach (var field in differingFields)
            {
                records.Add(new FindingEvidenceRecord.DuplicateDifferingField(reference, field, childOrder++));
            }

            foreach (var row in ordered)
            {
                records.Add(new FindingEvidenceRecord.DuplicateRow(
                    reference,
                    new DuplicateRowEvidence(
                        row.SourceLine,
                        row.OriginalTimestampText,
                        row.Open,
                        row.High,
                        row.Low,
                        row.Close,
                        row.Volume),
                    childOrder++));
            }

            return new DuplicateGroupEvidenceSet(
                reference,
                header,
                records,
                ordered.Select(row => row.SourceLine).ToArray());
        }

        // Every OHLCV field whose observed values are not identical across the
        // group, reported in canonical Open, High, Low, Close, Volume order.
        public static IReadOnlyList<string> DifferingFields(IReadOnlyList<DuplicateCandidateRow> rows)
        {
            if (rows is null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            var selectors = new Func<DuplicateCandidateRow, decimal>[]
            {
                row => row.Open,
                row => row.High,
                row => row.Low,
                row => row.Close,
                row => row.Volume
            };

            var differing = new List<string>();
            for (var index = 0; index < FieldOrder.Length; index++)
            {
                var selector = selectors[index];
                var first = selector(rows[0]);
                if (rows.Any(row => selector(row) != first))
                {
                    differing.Add(FieldOrder[index]);
                }
            }

            return differing;
        }

        public static DetailedFindingHeader HeaderFor(DuplicateGroupEvidenceSet group)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            var conflicting = group.Header.Classification == DuplicateClassification.Conflicting;
            var explanation = conflicting
                ? $"{group.SourceLines.Count} rows share timestamp {FindingReferenceFactory.UtcKey(group.Header.SharedTimestampUtc)} with differing values in {string.Join(", ", group.Header.DifferingFields)}."
                : $"{group.SourceLines.Count} rows share timestamp {FindingReferenceFactory.UtcKey(group.Header.SharedTimestampUtc)} with identical values.";

            var suggestedAction = conflicting
                ? "Reconcile the conflicting rows against the authoritative source before removing duplicates."
                : "Remove the redundant identical rows and keep one record per timestamp.";

            return new DetailedFindingHeader(
                group.Reference,
                FindingCategory.DuplicateRecord,
                conflicting ? "Conflicting duplicate records" : "Exact duplicate records",
                explanation,
                countContribution: 1,
                new FindingLocation(group.SourceLines, group.Header.SharedTimestampUtc),
                EvidenceKind.DuplicateRecord,
                suggestedAction);
        }
    }
}
