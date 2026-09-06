using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Web;

namespace Validator.Application.Abstractions
{
    /// <summary>
    /// Port: persist and retrieve <see cref="WebRunRecord"/> by
    /// <see cref="WebRunId"/>, applying guarded status transitions. A rejected
    /// transition MUST fail rather than silently coerce the state (Principle
    /// III; contracts/web-integration-contract.md).
    /// </summary>
    public interface IWebRunStore
    {
        /// <summary>The stored record, or null when the id is unknown or removed.</summary>
        ValueTask<WebRunRecord?> FindAsync(WebRunId id, CancellationToken ct = default);

        /// <summary>
        /// Creates the record if the deterministic id is absent. Returns false
        /// when it already exists — the duplicate-submission guard, not an
        /// error (FR-010).
        /// </summary>
        ValueTask<bool> TryCreateAsync(WebRunRecord record, CancellationToken ct = default);

        /// <summary>
        /// Applies a guarded transition. Throws when the transition is outside
        /// the lifecycle table; never coerces.
        /// </summary>
        ValueTask TransitionAsync(
            WebRunId id,
            WebRunStatus target,
            WebRunTransitionData data,
            CancellationToken ct = default);
    }
}