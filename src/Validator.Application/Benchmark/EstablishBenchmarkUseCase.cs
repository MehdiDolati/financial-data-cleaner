using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;

namespace Validator.Application.Benchmark
{
    /// <summary>
    /// Orchestrates the establishment of a validated dataset as a named immutable benchmark snapshot.
    /// Validates report completeness, builds snapshot, checks name collision, and saves via IBenchmarkStore.
    /// </summary>
    public sealed class EstablishBenchmarkUseCase
    {
        private readonly IBenchmarkStore _store;
        private readonly IApplicationClock _clock;

        public EstablishBenchmarkUseCase(IBenchmarkStore store)
            : this(store, SystemClock.Instance) { }

        public EstablishBenchmarkUseCase(IBenchmarkStore store, IApplicationClock clock)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// Establishes a benchmark from a completed validation report.
        /// </summary>
        /// <param name="report">The completed validation report with scores.</param>
        /// <param name="benchmarkName">User-supplied benchmark name.</param>
        /// <param name="sourceFilePath">Path to the original source file (will be copied into the benchmark).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The created benchmark snapshot.</returns>
        public async Task<BenchmarkSnapshot> ExecuteAsync(
            DetailedValidationReport report,
            string benchmarkName,
            string sourceFilePath,
            CancellationToken cancellationToken = default)
        {
            // Validate the report is complete enough for a benchmark (FR-004)
            var validationError = BenchmarkSnapshotValidator.Validate(report);
            if (validationError is not null)
                throw new InvalidOperationException(validationError);

            // Build the benchmark name
            var name = new BenchmarkName(benchmarkName);

            // Check for name collision (FR-003)
            if (await _store.ExistsAsync(name, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException(
                    $"Benchmark '{name}' already exists. Use a different name or delete the existing benchmark.");

            // Verify source file exists
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"Source file not found: {sourceFilePath}", sourceFilePath);

            // Build the snapshot from the report
            var snapshot = new BenchmarkSnapshot(
                name: name,
                establishedAtUtc: _clock.UtcNow,
                source: report.Source,
                context: report.Context,
                coverage: report.Coverage,
                checks: report.Checks,
                metrics: report.Score!.Metrics,
                dataset: report.Score.Dataset,
                weighting: report.Score.Weighting);

            // Save the snapshot and source bytes (FR-001, FR-002)
            await _store.SaveAsync(snapshot, sourceFilePath, cancellationToken).ConfigureAwait(false);

            return snapshot;
        }
    }
}
