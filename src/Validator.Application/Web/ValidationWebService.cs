using System;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;

namespace Validator.Application.Web
{
    /// <summary>
    /// The facade implementation (T030, FR-021). It composes the ports; it
    /// does not decide. Options are validated before any dataset byte is
    /// interpreted (FR-007); the run is durably Pending before it is queued;
    /// polling never triggers work (FR-009); retrieval returns the typed view
    /// or an explicit not-ready/unavailable outcome (FR-032).
    /// </summary>
    public sealed class ValidationWebService : IValidationWebService
    {
        private readonly IWebRunStore _runStore;
        private readonly IUploadedDatasetStore _uploadStore;
        private readonly IWebRunQueue _queue;
        private readonly IWebResultStore _resultStore;
        private readonly IApplicationClock _clock;

        public ValidationWebService(
            IWebRunStore runStore,
            IUploadedDatasetStore uploadStore,
            IWebRunQueue queue,
            IWebResultStore resultStore,
            IApplicationClock clock)
        {
            _runStore = runStore ?? throw new ArgumentNullException(nameof(runStore));
            _uploadStore = uploadStore ?? throw new ArgumentNullException(nameof(uploadStore));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _resultStore = resultStore ?? throw new ArgumentNullException(nameof(resultStore));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public async ValueTask<WebRunSubmission> SubmitAsync(
            WebRunRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // Pre-read option validation: a rejected configuration stores no
            // byte and queues no work (FR-007, SC-003).
            var rejection = WebRunOptionsValidator.Validate(request.Operation, request.Options);
            if (rejection is not null)
            {
                return new WebRunSubmission.Rejected(rejection);
            }

            // Retain the uploaded bytes content-addressed; the recorded
            // identity is the one the run and its id derive from.
            var dataset = await _uploadStore.StoreAsync(
                SafeName(request.SubmittedFileName),
                request.Content,
                cancellationToken).ConfigureAwait(false);

            var id = WebRunId.Derive(dataset.Identity, request.Options, request.Operation);
            var record = new WebRunRecord(
                id,
                request.Operation,
                dataset.Identity,
                request.Options,
                submittedAtUtc: _clock.UtcNow,
                submittedBy: request.SubmittedBy,
                benchmarkName: request.Options.BenchmarkName);

            // Create-if-absent with the deterministic id is the
            // duplicate-submission guard (FR-010): the loser joins the
            // existing run and no second run is created or queued.
            if (!await _runStore.TryCreateAsync(record, cancellationToken).ConfigureAwait(false))
            {
                return new WebRunSubmission.Accepted(id, JoinedExistingRun: true);
            }

            // Only a durably-Pending run is queued, so a crash between the
            // two leaves a recoverable pending run rather than a lost one.
            await _queue.EnqueueAsync(id, cancellationToken).ConfigureAwait(false);
            return new WebRunSubmission.Accepted(id, JoinedExistingRun: false);
        }

        public async ValueTask<WebRunStatusResult> GetStatusAsync(
            WebRunId id,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(id);

            var record = await _runStore.FindAsync(id, cancellationToken).ConfigureAwait(false);
            return record is null
                ? new WebRunStatusResult.Unavailable(
                    id,
                    "The run does not exist or is no longer retained.")
                : new WebRunStatusResult.Known(id, record.Status);
        }

        public async ValueTask<WebResultRetrieval> GetResultAsync(
            WebRunId id,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(id);

            var record = await _runStore.FindAsync(id, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                return new WebResultRetrieval.Unavailable(
                    "The run does not exist or is no longer retained.");
            }

            if (record.Status == WebRunStatus.Failed)
            {
                return new WebResultRetrieval.Ready(WebResultView.Failure(record));
            }

            if (record.Status is not (WebRunStatus.CompletedClean or WebRunStatus.CompletedWithFindings))
            {
                // NotReady carries the real lifecycle status so a caller can
                // distinguish still-running from gone from failed (FR-008).
                return new WebResultRetrieval.NotReady(record.Status);
            }

            var result = await _resultStore.FindAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Run '{id.Value}' is terminal-success but has no stored result.");

            var view = WebResultView.Success(
                record,
                result.Validation,
                result.Scoring,
                result.Benchmark,
                result.Comparison,
                result.AvailableExports);
            return new WebResultRetrieval.Ready(view);
        }

        public async ValueTask<WebExportResult> ExportAsync(
            WebRunId id,
            ReportRepresentation representation,
            System.IO.Stream destination,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(destination);

            var record = await _runStore.FindAsync(id, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                return new WebExportResult.NotAvailable(
                    "The run does not exist or is no longer retained.");
            }

            // Export is offered only for a terminal success, so an incomplete
            // or fatal run is never downloadable as a complete report
            // (FR-014, spec US2 scenario 5).
            if (record.Status is not (WebRunStatus.CompletedClean or WebRunStatus.CompletedWithFindings))
            {
                return new WebExportResult.NotAvailable(
                    $"The run is {record.Status}; export requires a terminal-success run.");
            }

            var result = await _resultStore.FindAsync(id, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                return new WebExportResult.NotAvailable(
                    "The run's result artifact is not available.");
            }

            if (!result.AvailableExports.Contains(representation))
            {
                return new WebExportResult.NotAvailable(
                    $"The representation {representation} is not available for this run.");
            }

            await result.Export(representation, destination, cancellationToken)
                .ConfigureAwait(false);
            return new WebExportResult.Written(representation);
        }

        public async ValueTask<WebRunSubmission> RetryAsync(
            WebRunId id,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(id);

            var record = await _runStore.FindAsync(id, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                return new WebRunSubmission.Rejected(new FatalDiagnostic(
                    "INVALID_ARGUMENT",
                    "The run to retry does not exist.",
                    "Check the run id; a removed or unknown run cannot be retried."));
            }

            if (record.Status == WebRunStatus.Failed)
            {
                // The only permitted Failed -> Pending transition: an explicit
                // user retry that reuses the same record and id (FR-010).
                await _runStore.TransitionAsync(
                    id,
                    WebRunStatus.Pending,
                    WebRunTransitionData.ForRetry(),
                    cancellationToken).ConfigureAwait(false);
                await _queue.EnqueueAsync(id, cancellationToken).ConfigureAwait(false);
                return new WebRunSubmission.Accepted(id, JoinedExistingRun: false);
            }

            return new WebRunSubmission.Rejected(new FatalDiagnostic(
                "INVALID_ARGUMENT",
                $"The run is {record.Status}; only a failed run can be retried.",
                "Wait for the run to finish, or retry a failed run explicitly."));
        }

        // SourceIdentity enforces the safe-base-name rule; this helper maps a
        // submitted display name to the safest base name without restating
        // the rule (FR-030).
        private static string SafeName(string submittedFileName)
        {
            var name = submittedFileName.Trim();
            var lastSeparator = name.LastIndexOfAny(['/', '\\']);
            if (lastSeparator >= 0)
            {
                name = name[(lastSeparator + 1)..];
            }

            return string.IsNullOrWhiteSpace(name) ? "upload.csv" : name;
        }
    }
}