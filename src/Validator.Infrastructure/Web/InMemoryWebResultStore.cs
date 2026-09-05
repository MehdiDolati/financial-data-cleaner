using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Web;

namespace Validator.Infrastructure.Web
{
    /// <summary>
    /// In-process result store: the executor persists the typed result (with
    /// its export delegate closing over the completed report) and the facade
    /// reads it back for views and exports. Artifacts live as long as the
    /// process does, matching the retain-until-deleted interim default within
    /// one deployment; a host may substitute durable storage behind the same
    /// port (research R4/R5).
    /// </summary>
    public sealed class InMemoryWebResultStore : IWebResultStore
    {
        private readonly ConcurrentDictionary<string, WebRunResult> _results = new();

        public Task SaveAsync(WebRunId id, WebRunResult result, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(result);
            _results[id.Value] = result;
            return Task.CompletedTask;
        }

        public Task<WebRunResult?> FindAsync(WebRunId id, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(id);
            return Task.FromResult(_results.TryGetValue(id.Value, out var result) ? result : null);
        }
    }
}