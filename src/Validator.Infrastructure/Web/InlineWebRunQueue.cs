using System;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Web;

namespace Validator.Infrastructure.Web
{
    /// <summary>
    /// The simplest safe default queue (research R3): an accepted run executes
    /// synchronously through the injected Application run executor, which
    /// persists the terminal state. A host may substitute a real background
    /// worker without touching Application code.
    /// </summary>
    public sealed class InlineWebRunQueue : IWebRunQueue
    {
        private readonly Func<WebRunId, Task> _runExecutor;

        public InlineWebRunQueue(Func<WebRunId, Task> runExecutor)
        {
            _runExecutor = runExecutor ?? throw new ArgumentNullException(nameof(runExecutor));
        }

        public async ValueTask EnqueueAsync(WebRunId id, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(id);
            ct.ThrowIfCancellationRequested();

            await _runExecutor(id).ConfigureAwait(false);
        }
    }
}