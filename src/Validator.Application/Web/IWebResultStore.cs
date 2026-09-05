using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;

namespace Validator.Application.Web
{
    /// <summary>
    /// The durable payload of one terminal-success run: the report sections
    /// the view projects and the export callback that streams the stored
    /// artifact through the existing writers (FR-013, FR-014).
    /// </summary>
    public sealed record WebRunResult(
        WebValidationSection Validation,
        WebScoringSection? Scoring,
        WebBenchmarkSection? Benchmark,
        WebComparisonSection? Comparison,
        IReadOnlyList<ReportRepresentation> AvailableExports,
        Func<ReportRepresentation, Stream, CancellationToken, Task> Export);

    /// <summary>
    /// Port: persist and retrieve the result payload of a terminal-success
    /// run. The artifact is written once by the executor and read many times
    /// by the facade; it is never recomputed (FR-024).
    /// </summary>
    public interface IWebResultStore
    {
        /// <summary>Stores the result payload for one run id.</summary>
        Task SaveAsync(WebRunId id, WebRunResult result, CancellationToken ct = default);

        /// <summary>The stored payload, or null when the run has no result.</summary>
        Task<WebRunResult?> FindAsync(WebRunId id, CancellationToken ct = default);
    }
}