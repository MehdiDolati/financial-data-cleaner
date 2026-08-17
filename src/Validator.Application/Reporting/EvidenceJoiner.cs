using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;

namespace Validator.Application.Reporting
{
    // One finding's evidence after the normalized child records have been joined
    // back to their owning finding. The header record carries the category
    // evidence; children are the repeated rows, references, violations, field
    // errors, and skipped checks in deterministic child order.
    public sealed record JoinedEvidence
    {
        public FindingReference Finding { get; }
        public EvidenceKind Kind { get; }
        public FindingEvidenceRecord Header { get; }
        public IReadOnlyList<FindingEvidenceRecord> Children { get; }
        public IReadOnlyList<FindingRelationship> Relationships { get; }

        public JoinedEvidence(
            FindingReference finding,
            EvidenceKind kind,
            FindingEvidenceRecord header,
            IReadOnlyList<FindingEvidenceRecord> children,
            IReadOnlyList<FindingRelationship> relationships)
        {
            Finding = finding ?? throw new ArgumentNullException(nameof(finding));
            Kind = kind;
            Header = header ?? throw new ArgumentNullException(nameof(header));
            Children = children ?? throw new ArgumentNullException(nameof(children));
            Relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
        }

        public IEnumerable<TRecord> ChildrenOf<TRecord>()
            where TRecord : FindingEvidenceRecord =>
            Children.OfType<TRecord>();
    }

    // Joins the normalized evidence records of one finding into a single
    // renderable view. The joiner never invents evidence: a finding whose
    // required header record is absent, or whose records belong to another
    // finding, is a validation defect rather than a partially rendered report.
    public static class EvidenceJoiner
    {
        public static async ValueTask<JoinedEvidence> JoinAsync(
            IDetailedFindingCursor cursor,
            CancellationToken cancellationToken = default)
        {
            if (cursor is null)
            {
                throw new ArgumentNullException(nameof(cursor));
            }

            var records = new List<FindingEvidenceRecord>();
            await foreach (var record in cursor.ReadEvidenceAsync(cancellationToken).ConfigureAwait(false))
            {
                records.Add(record);
            }

            var relationships = new List<FindingRelationship>();
            await foreach (var relationship in cursor.ReadRelationshipsAsync(cancellationToken).ConfigureAwait(false))
            {
                relationships.Add(relationship);
            }

            return Join(cursor.Header, records, relationships);
        }

        public static JoinedEvidence Join(
            DetailedFindingHeader header,
            IReadOnlyList<FindingEvidenceRecord> records,
            IReadOnlyList<FindingRelationship>? relationships = null)
        {
            if (header is null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            if (records is null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            foreach (var record in records)
            {
                if (!string.Equals(OwnerOf(record).Value, header.Reference.Value, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Evidence record owned by '{OwnerOf(record).Value}' cannot be joined to finding '{header.Reference.Value}'.");
                }
            }

            var headerRecord = records.FirstOrDefault(record => IsHeaderRecord(record, header.EvidenceKind))
                ?? throw new InvalidOperationException(
                    $"Finding '{header.Reference.Value}' is missing its required {header.EvidenceKind} evidence record.");

            var children = records
                .Where(record => !ReferenceEquals(record, headerRecord))
                .OrderBy(ChildOrderOf)
                .ThenBy(record => record.Kind, StringComparer.Ordinal)
                .ToArray();

            return new JoinedEvidence(
                header.Reference,
                header.EvidenceKind,
                headerRecord,
                children,
                ExpandRelationships(relationships ?? Array.Empty<FindingRelationship>()));
        }

        // Relationship edges are expanded in a deterministic order (kind, then
        // target reference) and duplicates collapse, so two runs over the same
        // findings expose identical relationship sequences.
        public static IReadOnlyList<FindingRelationship> ExpandRelationships(
            IEnumerable<FindingRelationship> relationships)
        {
            if (relationships is null)
            {
                throw new ArgumentNullException(nameof(relationships));
            }

            var expanded = new List<FindingRelationship>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var relationship in relationships
                .OrderBy(relationship => relationship.Kind, StringComparer.Ordinal)
                .ThenBy(relationship => relationship.TargetReference.Value, StringComparer.Ordinal))
            {
                if (seen.Add($"{relationship.Kind}|{relationship.TargetReference.Value}"))
                {
                    expanded.Add(relationship);
                }
            }

            return expanded;
        }

        public static FindingReference OwnerOf(FindingEvidenceRecord evidence) => evidence switch
        {
            FindingEvidenceRecord.MissingCandle record => record.Finding,
            FindingEvidenceRecord.TimeGapHeader record => record.Finding,
            FindingEvidenceRecord.TimeGapMissingReference record => record.Finding,
            FindingEvidenceRecord.DuplicateHeader record => record.Finding,
            FindingEvidenceRecord.DuplicateDifferingField record => record.Finding,
            FindingEvidenceRecord.DuplicateRow record => record.Finding,
            FindingEvidenceRecord.InvalidOhlcValues record => record.Finding,
            FindingEvidenceRecord.InvalidOhlcViolation record => record.Finding,
            FindingEvidenceRecord.ClosedMarket record => record.Finding,
            FindingEvidenceRecord.MalformedHeader record => record.Finding,
            FindingEvidenceRecord.MalformedFieldErrorRecord record => record.Finding,
            FindingEvidenceRecord.MalformedSkippedCheck record => record.Finding,
            null => throw new ArgumentNullException(nameof(evidence)),
            _ => throw new ArgumentOutOfRangeException(nameof(evidence))
        };

        public static long ChildOrderOf(FindingEvidenceRecord evidence) => evidence switch
        {
            FindingEvidenceRecord.MissingCandle record => record.ChildOrder,
            FindingEvidenceRecord.TimeGapHeader record => record.ChildOrder,
            FindingEvidenceRecord.TimeGapMissingReference record => record.ChildOrder,
            FindingEvidenceRecord.DuplicateHeader record => record.ChildOrder,
            FindingEvidenceRecord.DuplicateDifferingField record => record.ChildOrder,
            FindingEvidenceRecord.DuplicateRow record => record.ChildOrder,
            FindingEvidenceRecord.InvalidOhlcValues record => record.ChildOrder,
            FindingEvidenceRecord.InvalidOhlcViolation record => record.ChildOrder,
            FindingEvidenceRecord.ClosedMarket record => record.ChildOrder,
            FindingEvidenceRecord.MalformedHeader record => record.ChildOrder,
            FindingEvidenceRecord.MalformedFieldErrorRecord record => record.ChildOrder,
            FindingEvidenceRecord.MalformedSkippedCheck record => record.ChildOrder,
            null => throw new ArgumentNullException(nameof(evidence)),
            _ => throw new ArgumentOutOfRangeException(nameof(evidence))
        };

        private static bool IsHeaderRecord(FindingEvidenceRecord record, EvidenceKind kind) => kind switch
        {
            EvidenceKind.MissingCandle => record is FindingEvidenceRecord.MissingCandle,
            EvidenceKind.TimeGap => record is FindingEvidenceRecord.TimeGapHeader,
            EvidenceKind.DuplicateRecord => record is FindingEvidenceRecord.DuplicateHeader,
            EvidenceKind.InvalidOhlc => record is FindingEvidenceRecord.InvalidOhlcValues,
            EvidenceKind.ClosedMarketRecord => record is FindingEvidenceRecord.ClosedMarket,
            EvidenceKind.MalformedRow => record is FindingEvidenceRecord.MalformedHeader,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
