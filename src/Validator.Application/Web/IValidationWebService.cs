using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;

namespace Validator.Application.Web
{
    /// <summary>
    /// The outcome of a submission: accepted with the deterministic run id,
    /// or rejected with a fatal diagnostic before any dataset byte was
    /// interpreted (FR-007).
    /// </summary>
    public abstract record WebRunSubmission
    {
        /// <summary>
        /// The run is durably Pending. JoinedExistingRun is how idempotency
        /// is observable: a refresh or double submission of identical bytes
        /// and options returns the same id with the flag set (FR-010).
        /// </summary>
        public sealed record Accepted(WebRunId Id, bool JoinedExistingRun) : WebRunSubmission;

        /// <summary>The options were unusable; nothing was stored or queued.</summary>
        public sealed record Rejected(FatalDiagnostic Diagnostic) : WebRunSubmission;
    }

    /// <summary>
    /// The polling surface (FR-009). Cheap; never triggers work.
    /// </summary>
    public abstract record WebRunStatusResult
    {
        public sealed record Known(WebRunId Id, WebRunStatus Status) : WebRunStatusResult;

        /// <summary>The run does not exist (or has been removed); never an empty success (FR-032).</summary>
        public sealed record Unavailable(WebRunId Id, string Reason) : WebRunStatusResult;
    }

    /// <summary>
    /// The typed view of a terminal run, or an explicit not-ready /
    /// unavailable outcome - never an empty success (FR-032).
    /// </summary>
    public abstract record WebResultRetrieval
    {
        public sealed record Ready(WebResultView View) : WebResultRetrieval;

        /// <summary>Carries the real lifecycle status so a caller can distinguish states (FR-008).</summary>
        public sealed record NotReady(WebRunStatus Status) : WebResultRetrieval;

        public sealed record Unavailable(string Reason) : WebResultRetrieval;
    }

    /// <summary>
    /// The outcome of an export request. Export is offered only for a
    /// terminal success, so an incomplete or fatal run is never downloadable
    /// as if it were a complete report (FR-014).
    /// </summary>
    public abstract record WebExportResult
    {
        public sealed record Written(ReportRepresentation Representation) : WebExportResult;

        public sealed record NotAvailable(string Reason) : WebExportResult;
    }

    /// <summary>
    /// The single explicit boundary through which a website invokes the
    /// business use cases and receives typed outcomes without owning their
    /// rules (FR-021). The facade composes; it does not decide.
    /// </summary>
    public interface IValidationWebService
    {
        /// <summary>
        /// Validates options before any dataset byte is interpreted (FR-007),
        /// persists the run as Pending, and hands it to the queue.
        /// </summary>
        ValueTask<WebRunSubmission> SubmitAsync(
            WebRunRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>The polling surface backing the pending/progress state (FR-009).</summary>
        ValueTask<WebRunStatusResult> GetStatusAsync(
            WebRunId id,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// The typed view for a terminal run, or an explicit not-ready /
        /// unavailable outcome.
        /// </summary>
        ValueTask<WebResultRetrieval> GetResultAsync(
            WebRunId id,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Streams the machine-readable artifact using the existing report
        /// writers (FR-014); no new serializer is introduced.
        /// </summary>
        ValueTask<WebExportResult> ExportAsync(
            WebRunId id,
            ReportRepresentation representation,
            Stream destination,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs the only permitted Failed to Pending transition (FR-010).
        /// </summary>
        ValueTask<WebRunSubmission> RetryAsync(
            WebRunId id,
            CancellationToken cancellationToken = default);
    }
}