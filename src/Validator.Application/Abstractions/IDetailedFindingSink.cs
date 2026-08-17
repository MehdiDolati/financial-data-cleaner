using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Reporting;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;

namespace Validator.Application.Abstractions
{
    // Application-owned normalized finding catalog sink. Findings, location
    // lines, evidence records, and relationship edges are appended to bounded
    // temporary runs and later replayed through canonically ordered cursors.
    public interface IDetailedFindingSink : IAsyncDisposable
    {
        ValueTask AppendFindingAsync(
            DetailedFindingHeader finding,
            CancellationToken cancellationToken = default);

        ValueTask AppendLocationLineAsync(
            FindingReference finding,
            long sourceLine,
            CancellationToken cancellationToken = default);

        ValueTask AppendEvidenceAsync(
            FindingEvidenceRecord evidence,
            CancellationToken cancellationToken = default);

        ValueTask AppendRelationshipPairAsync(
            FindingRelationship forward,
            FindingRelationship reverse,
            CancellationToken cancellationToken = default);

        ValueTask<CompletedFindingCatalogResult> CompleteAsync(
            CancellationToken cancellationToken = default);
    }

    // Frozen, canonically ordered catalog produced by CompleteAsync. Readers
    // expose one finding at a time; a cursor's child sequences must be consumed
    // before advancing the parent enumerator.
    public interface ICompletedFindingCatalog : IAsyncDisposable
    {
        FindingCatalogStatistics Statistics { get; }

        IAsyncEnumerable<IDetailedFindingCursor> ReadCanonicalAsync(
            CancellationToken cancellationToken = default);
    }

    public interface IDetailedFindingCursor
    {
        DetailedFindingHeader Header { get; }

        IAsyncEnumerable<long> ReadSourceLinesAsync(
            CancellationToken cancellationToken = default);

        IAsyncEnumerable<FindingRelationship> ReadRelationshipsAsync(
            CancellationToken cancellationToken = default);

        IAsyncEnumerable<FindingEvidenceRecord> ReadEvidenceAsync(
            CancellationToken cancellationToken = default);
    }

    public abstract record CompletedFindingCatalogResult
    {
        public sealed record Succeeded(ICompletedFindingCatalog Catalog) : CompletedFindingCatalogResult;

        public sealed record Failed(FatalDiagnostic Diagnostic) : CompletedFindingCatalogResult;
    }
}