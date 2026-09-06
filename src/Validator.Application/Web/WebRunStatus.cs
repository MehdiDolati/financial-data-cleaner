using System;
using System.Collections.Generic;

namespace Validator.Application.Web
{
    /// <summary>
    /// The lifecycle state of one web run (FR-008).
    /// </summary>
    public enum WebRunStatus
    {
        /// <summary>Accepted and durably persisted; work not started.</summary>
        Pending = 0,

        /// <summary>Work in progress.</summary>
        Running = 1,

        /// <summary>Reconciled report exists and every category count is zero.</summary>
        CompletedClean = 2,

        /// <summary>Reconciled report exists and at least one category count is non-zero.</summary>
        CompletedWithFindings = 3,

        /// <summary>No trustworthy report; a fatal diagnostic explains why.</summary>
        Failed = 4
    }

    /// <summary>
    /// The exact transition table of the run-lifecycle contract. Rejections
    /// surface as failures, never as coerced states (Principle V applied to
    /// the lifecycle itself).
    /// </summary>
    public static class WebRunStatusGuard
    {
        private static readonly IReadOnlyDictionary<WebRunStatus, WebRunStatus[]> Allowed =
            new Dictionary<WebRunStatus, WebRunStatus[]>
            {
                [WebRunStatus.Pending] = [WebRunStatus.Running, WebRunStatus.Failed],
                [WebRunStatus.Running] =
                [
                    WebRunStatus.CompletedClean,
                    WebRunStatus.CompletedWithFindings,
                    WebRunStatus.Failed
                ],
                [WebRunStatus.Failed] = [WebRunStatus.Pending],
                [WebRunStatus.CompletedClean] = [],
                [WebRunStatus.CompletedWithFindings] = []
            };

        /// <summary>
        /// Throws InvalidOperationException when the transition is outside
        /// the contract table; does nothing when it is allowed.
        /// </summary>
        public static void EnsureTransition(WebRunStatus from, WebRunStatus to)
        {
            if (!IsAllowed(from, to))
            {
                throw new InvalidOperationException(
                    $"The web run transition {from} -> {to} is not permitted by the run lifecycle contract.");
            }
        }

        /// <summary>Whether the transition is allowed by the contract table.</summary>
        public static bool IsAllowed(WebRunStatus from, WebRunStatus to) =>
            Allowed.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

        /// <summary>Whether the state is terminal: no further transition may leave it.</summary>
        public static bool IsTerminal(WebRunStatus status) =>
            status is WebRunStatus.CompletedClean
                or WebRunStatus.CompletedWithFindings
                or WebRunStatus.Failed;

        /// <summary>
        /// Whether the state may be presented as a clean result. Only
        /// CompletedClean - which requires an existing reconciled report -
        /// ever reads as clean (SC-003, SC-007).
        /// </summary>
        public static bool ReadsAsClean(WebRunStatus status) => status == WebRunStatus.CompletedClean;
    }
}