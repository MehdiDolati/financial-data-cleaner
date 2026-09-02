using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Web;

namespace Validator.Application.Abstractions
{
    /// <summary>
    /// Port: hand an accepted run to background execution. Accepting a run
    /// MUST have already persisted it as Pending, so a crash between persist
    /// and enqueue leaves a recoverable pending run rather than a lost one
    /// (contracts/web-integration-contract.md, research R3).
    /// </summary>
    public interface IWebRunQueue
    {
        /// <summary>
        /// Hands one durably-Pending run to the execution mechanism. Called
        /// only after the record is durably Pending.
        /// </summary>
        ValueTask EnqueueAsync(WebRunId id, CancellationToken ct = default);
    }
}