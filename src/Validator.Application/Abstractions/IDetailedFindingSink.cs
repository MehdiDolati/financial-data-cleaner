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
        /// <summary>Appends one finding's header.</summary>
        ValueTask AppendFindingAsync(
            DetailedFindingHeader finding,
            CancellationToken cancellationToken = default);

        /// <summary>Appends one more source line to a finding already appended.</summary>
        ValueTask AppendLocationLineAsync(
            FindingReference finding,
            long sourceLine,
            CancellationToken cancellationToken = default);

        /// <summary>Appends one evidence record belonging to a finding.</summary>
        ValueTask AppendEvidenceAsync(
            FindingEvidenceRecord evidence,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Appends both directions of a relationship together, so a report can
        /// never contain an edge that points only one way.
        /// </summary>
        ValueTask AppendRelationshipPairAsync(
            FindingRelationship forward,
            FindingRelationship reverse,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finishes writing and returns the frozen catalog, or a diagnostic if the
        /// catalog could not be completed.
        /// </summary>
        ValueTask<CompletedFindingCatalogResult> CompleteAsync(
            CancellationToken cancellationToken = default);
    }

    // Frozen, canonically ordered catalog produced by CompleteAsync. Readers
    // expose one finding at a time; a cursor's child sequences must be consumed
    // before advancing the parent enumerator.
    public interface ICompletedFindingCatalog : IAsyncDisposable
    {
        /// <summary>The counts the report's summary is derived from.</summary>
        FindingCatalogStatistics Statistics { get; }

        /// <summary>
        /// Replays every finding in canonical order, one at a time.
        /// </summary>
        IAsyncEnumerable<IDetailedFindingCursor> ReadCanonicalAsync(
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One finding being replayed, together with its child sequences.
    /// </summary>
    /// <remarks>
    /// A cursor's child sequences must be consumed before the parent enumerator
    /// advances, because the underlying runs are read forward only.
    /// </remarks>
    public interface IDetailedFindingCursor
    {
        /// <summary>The finding's header.</summary>
        DetailedFindingHeader Header { get; }

        /// <summary>The source lines this finding covers, in ascending order.</summary>
        IAsyncEnumerable<long> ReadSourceLinesAsync(
            CancellationToken cancellationToken = default);

        /// <summary>The edges from this finding to related findings.</summary>
        IAsyncEnumerable<FindingRelationship> ReadRelationshipsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>This finding's evidence records, in their stored child order.</summary>
        IAsyncEnumerable<FindingEvidenceRecord> ReadEvidenceAsync(
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The outcome of completing a catalog: either a readable catalog or the
    /// diagnostic explaining why one could not be produced.
    /// </summary>
    public abstract record CompletedFindingCatalogResult
    {
        /// <summary>The catalog was completed and can be replayed.</summary>
        public sealed record Succeeded(ICompletedFindingCatalog Catalog) : CompletedFindingCatalogResult;

        /// <summary>The catalog could not be completed.</summary>
        public sealed record Failed(FatalDiagnostic Diagnostic) : CompletedFindingCatalogResult;
    }
}