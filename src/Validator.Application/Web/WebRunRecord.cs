using System;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;

namespace Validator.Application.Web
{
    /// <summary>
    /// The guarded terminal payload of one transition: exactly one of a
    /// result reference (terminal success) or a fatal diagnostic (failure).
    /// Partial success is unrepresentable rather than discouraged
    /// (FR-011, data-model.md).
    /// </summary>
    public sealed record WebRunTransitionData
    {
        /// <summary>Reference to the stored result artifact; terminal success only.</summary>
        public string? ResultReference { get; }

        /// <summary>Diagnostic explaining the failure; Failed only.</summary>
        public FatalDiagnostic? FatalDiagnostic { get; }

        /// <summary>Utc timestamp applied by the store at transition time; terminal states only.</summary>
        public DateTimeOffset? TerminalAtUtc { get; }

        private WebRunTransitionData(string? resultReference, FatalDiagnostic? fatalDiagnostic, DateTimeOffset? terminalAtUtc)
        {
            if (resultReference is not null && fatalDiagnostic is not null)
            {
                throw new ArgumentException(
                    "A transition may carry a result reference or a fatal diagnostic, never both.");
            }

            if (resultReference is null && fatalDiagnostic is null && terminalAtUtc is null)
            {
                throw new ArgumentException(
                    "A transition must carry a result reference, a fatal diagnostic, or retry semantics.");
            }

            ResultReference = resultReference;
            FatalDiagnostic = fatalDiagnostic;
            TerminalAtUtc = terminalAtUtc;
        }

        /// <summary>The payload for a Pending to Running transition.</summary>
        public static WebRunTransitionData ForRunning() => new(null, null, DateTimeOffset.UnixEpoch);

        /// <summary>The payload for a terminal success.</summary>
        public static WebRunTransitionData ForSuccess(string resultReference, DateTimeOffset terminalAtUtc) =>
            new(resultReference, null, terminalAtUtc);

        /// <summary>The payload for a transition into Failed.</summary>
        public static WebRunTransitionData ForFailure(FatalDiagnostic diagnostic, DateTimeOffset terminalAtUtc = default) =>
            new(null, diagnostic, terminalAtUtc == default ? DateTimeOffset.UnixEpoch : terminalAtUtc);

        /// <summary>The payload for the explicit Failed to Pending retry transition.</summary>
        public static WebRunTransitionData ForRetry() => new(null, null, null);
    }

    /// <summary>
    /// The audit aggregate of one run (FR-026). This is the persisted entity;
    /// it is not the view model. Transitions return new immutable records and
    /// enforce the lifecycle table and the record-level invariants at every
    /// step.
    /// </summary>
    public sealed record WebRunRecord
    {
        public WebRunId Id { get; }

        public WebRunOperation Operation { get; }

        public WebRunStatus Status { get; }

        /// <summary>Safe base name, byte size, and SHA-256 of the uploaded source.</summary>
        public SourceIdentity Source { get; }

        /// <summary>The exact resolved options applied - not the raw submission.</summary>
        public WebRunOptions ResolvedOptions { get; }

        /// <summary>Required for EstablishBenchmark and Compare; null otherwise.</summary>
        public string? BenchmarkName { get; }

        /// <summary>Reference to the stored result artifact; terminal success only.</summary>
        public string? ResultReference { get; }

        /// <summary>Non-null exactly when Status is Failed.</summary>
        public FatalDiagnostic? Diagnostic { get; }

        /// <summary>Audit metadata from IApplicationClock; never a computed input (Principle IV).</summary>
        public DateTimeOffset SubmittedAtUtc { get; }

        /// <summary>Set once on reaching a terminal state.</summary>
        public DateTimeOffset? TerminalAtUtc { get; }

        /// <summary>Opaque host correlation; never interpreted, never authorization (research R6).</summary>
        public string? SubmittedBy { get; }

        public WebRunRecord(
            WebRunId id,
            WebRunOperation operation,
            SourceIdentity source,
            WebRunOptions resolvedOptions,
            DateTimeOffset submittedAtUtc,
            string? submittedBy = null,
            string? benchmarkName = null)
        {
            if (operation is WebRunOperation.EstablishBenchmark or WebRunOperation.Compare)
            {
                if (string.IsNullOrWhiteSpace(benchmarkName))
                {
                    throw new ArgumentException(
                        "EstablishBenchmark and Compare require a benchmark name.",
                        nameof(benchmarkName));
                }
            }
            else if (!string.IsNullOrWhiteSpace(benchmarkName))
            {
                throw new ArgumentException(
                    "A plain validation run must not carry a benchmark name.",
                    nameof(benchmarkName));
            }

            if (submittedAtUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("SubmittedAtUtc must be UTC-normalized.", nameof(submittedAtUtc));
            }

            Id = id ?? throw new ArgumentNullException(nameof(id));
            Operation = operation;
            Status = WebRunStatus.Pending;
            Source = source ?? throw new ArgumentNullException(nameof(source));
            ResolvedOptions = resolvedOptions ?? throw new ArgumentNullException(nameof(resolvedOptions));
            SubmittedAtUtc = submittedAtUtc;
            SubmittedBy = submittedBy;
            BenchmarkName = benchmarkName;
            ResultReference = null;
            Diagnostic = null;
            TerminalAtUtc = null;
        }

        private WebRunRecord(
            WebRunId id,
            WebRunOperation operation,
            WebRunStatus status,
            SourceIdentity source,
            WebRunOptions resolvedOptions,
            string? benchmarkName,
            string? resultReference,
            FatalDiagnostic? diagnostic,
            DateTimeOffset submittedAtUtc,
            DateTimeOffset? terminalAtUtc,
            string? submittedBy)
        {
            Id = id;
            Operation = operation;
            Status = status;
            Source = source;
            ResolvedOptions = resolvedOptions;
            BenchmarkName = benchmarkName;
            ResultReference = resultReference;
            Diagnostic = diagnostic;
            SubmittedAtUtc = submittedAtUtc;
            TerminalAtUtc = terminalAtUtc;
            SubmittedBy = submittedBy;
        }

        /// <summary>Pending to Running (queue picks the run up).</summary>
        public WebRunRecord ToRunning()
        {
            WebRunStatusGuard.EnsureTransition(Status, WebRunStatus.Running);
            return With(state: WebRunStatus.Running);
        }

        /// <summary>Running (or Pending) to Failed with a required diagnostic.</summary>
        public WebRunRecord ToFailed(FatalDiagnostic diagnostic, DateTimeOffset terminalAtUtc)
        {
            WebRunStatusGuard.EnsureTransition(Status, WebRunStatus.Failed);
            if (diagnostic is null)
            {
                throw new ArgumentException("A failed run requires a diagnostic.", nameof(diagnostic));
            }

            return With(
                state: WebRunStatus.Failed,
                diagnostic: diagnostic,
                resultReference: null,
                terminalAtUtc: terminalAtUtc.ToUniversalTime());
        }

        /// <summary>
        /// Running to CompletedClean or CompletedWithFindings, selected by the
        /// reconciled report's own cleanliness - never inferred from the
        /// absence of an error (SC-003).
        /// </summary>
        public WebRunRecord ToCompleted(string resultReference, bool isClean, DateTimeOffset terminalAtUtc)
        {
            var target = isClean ? WebRunStatus.CompletedClean : WebRunStatus.CompletedWithFindings;
            WebRunStatusGuard.EnsureTransition(Status, target);

            if (string.IsNullOrWhiteSpace(resultReference))
            {
                throw new ArgumentException("A completed run requires a result reference.", nameof(resultReference));
            }

            return With(
                state: target,
                resultReference: resultReference,
                diagnostic: null,
                terminalAtUtc: terminalAtUtc.ToUniversalTime());
        }

        /// <summary>
        /// The only permitted Failed to Pending transition: an explicit user
        /// retry, never an automatic one (FR-010).
        /// </summary>
        public WebRunRecord ToPendingRetry()
        {
            WebRunStatusGuard.EnsureTransition(Status, WebRunStatus.Pending);
            return With(
                state: WebRunStatus.Pending,
                diagnostic: null,
                resultReference: null,
                terminalAtUtc: null);
        }

        /// <summary>
        /// Applies a transition payload carried by the store, returning the
        /// next immutable record. Rejected transitions throw; nothing is
        /// coerced.
        /// </summary>
        public WebRunRecord Apply(WebRunStatus target, WebRunTransitionData data)
        {
            ArgumentNullException.ThrowIfNull(data);

            if (target == WebRunStatus.Failed)
            {
                if (data.FatalDiagnostic is null)
                {
                    throw new ArgumentException(
                        "A transition into Failed requires a fatal diagnostic.", nameof(data));
                }

                return ToFailed(data.FatalDiagnostic, data.TerminalAtUtc ?? DateTimeOffset.UnixEpoch);
            }

            if (target is WebRunStatus.CompletedClean or WebRunStatus.CompletedWithFindings)
            {
                if (data.ResultReference is null)
                {
                    throw new ArgumentException(
                        "A transition into a completed state requires a result reference.", nameof(data));
                }

                return ToCompleted(
                    data.ResultReference,
                    target == WebRunStatus.CompletedClean,
                    data.TerminalAtUtc ?? DateTimeOffset.UnixEpoch);
            }

            if (target == WebRunStatus.Running)
            {
                if (data.ResultReference is not null || data.FatalDiagnostic is not null)
                {
                    throw new ArgumentException(
                        "A transition into Running carries no terminal payload.", nameof(data));
                }

                return ToRunning();
            }

            if (target == WebRunStatus.Pending)
            {
                return ToPendingRetry();
            }

            throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown target state.");
        }

        private WebRunRecord With(
            WebRunStatus state,
            FatalDiagnostic? diagnostic = null,
            string? resultReference = null,
            DateTimeOffset? terminalAtUtc = null)
        {
            if (state == WebRunStatus.Failed && diagnostic is null)
            {
                throw new InvalidOperationException("A failed run requires a diagnostic.");
            }

            if (state != WebRunStatus.Failed && diagnostic is not null)
            {
                throw new InvalidOperationException("Only a failed run may carry a diagnostic.");
            }

            if (resultReference is not null &&
                state is not (WebRunStatus.CompletedClean or WebRunStatus.CompletedWithFindings))
            {
                throw new InvalidOperationException("A result reference is allowed only on a terminal success.");
            }

            return new WebRunRecord(
                Id,
                Operation,
                state,
                Source,
                ResolvedOptions,
                BenchmarkName,
                resultReference,
                diagnostic,
                SubmittedAtUtc,
                terminalAtUtc,
                SubmittedBy);
        }
    }
}