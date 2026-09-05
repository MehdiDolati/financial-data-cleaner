using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Validation;
using Validator.Application.Web;
using Validator.Infrastructure.Benchmark;
using Validator.Infrastructure.Calendars;
using Validator.Infrastructure.Csv;
using Validator.Infrastructure.Findings;
using Validator.Infrastructure.Reporting;
using Validator.Infrastructure.Sorting;

namespace Validator.Infrastructure.Web
{
    /// <summary>
    /// Composition-root helper for host websites (T033): registers the web
    /// integration boundary over the established use cases with a configurable
    /// storage root. Thin wiring only - it owns no rule (Constitution II
    /// adapter exemption; FR-023).
    /// </summary>
    public static class ValidatorWebIntegrationExtensions
    {
        /// <summary>
        /// Registers IValidationWebService, the file-backed run and upload
        /// stores, the inline queue, the in-process result store, the
        /// deterministic clock, and the full validation pipeline composed
        /// exactly as the CLI composes it (parity by construction, R2).
        /// </summary>
        /// <param name="storageRoot">
        /// Root folder for run records, uploaded datasets, and benchmarks.
        /// Created on demand; retained until explicitly deleted (research R5
        /// interim default).
        /// </param>
        public static IServiceCollection AddValidatorWebIntegration(
            this IServiceCollection services,
            string storageRoot)
        {
            if (string.IsNullOrWhiteSpace(storageRoot))
            {
                throw new ArgumentException("The validator storage root must not be empty.", nameof(storageRoot));
            }

            services.TryAddSingleton<IApplicationClock>(SystemClock.Instance);
            services.TryAddSingleton<IMarketCalendarFactory, MarketCalendarFactory>();

            services.TryAddSingleton<IWebRunStore>(_ => new FileWebRunStore(storageRoot));
            services.TryAddSingleton<IUploadedDatasetStore>(_ => new FileUploadedDatasetStore(storageRoot));
            services.TryAddSingleton<IWebResultStore, InMemoryWebResultStore>();

            // Benchmark operations resolve through the established file
            // benchmark store under the same root (R4 interim default).
            services.TryAddSingleton<Validator.Application.Benchmark.IBenchmarkStore>(
                _ => new FileBenchmarkStore(Path.Combine(storageRoot, "benchmarks")));

            services.TryAddSingleton<DetailedValidationOrchestrator>(static _ => new DetailedValidationOrchestrator(
                () => new FindingCatalog(
                    () => new SpoolWriter(new TempStorage()),
                    path => new SpoolReader(path, path + ".complete"),
                    new ExternalMergeSpool(new TempStorage()))));

            services.TryAddSingleton<IDetailedValidationUseCase>(provider =>
            {
                var orchestrator = provider.GetRequiredService<DetailedValidationOrchestrator>();
                return new OrchestratorAdapter(orchestrator);
            });

            services.TryAddSingleton<WebRunExecutor>(provider => new WebRunExecutor(
                provider.GetRequiredService<IWebRunStore>(),
                provider.GetRequiredService<IUploadedDatasetStore>(),
                provider.GetRequiredService<IDetailedValidationUseCase>(),
                provider.GetRequiredService<IMarketCalendarFactory>(),
                provider.GetRequiredService<IApplicationClock>(),
                provider.GetRequiredService<IWebResultStore>(),
                (id, report, representation, destination, token) =>
                    WebReportExport.Export(id, report, representation, destination, token),
                provider.GetRequiredService<Validator.Application.Benchmark.IBenchmarkStore>(),
                name => LoadBenchmarkCandles(storageRoot, name)));

            services.TryAddSingleton<IWebRunQueue>(provider =>
                new InlineWebRunQueue(id => provider.GetRequiredService<WebRunExecutor>().ExecuteAsync(id)));

            services.TryAddSingleton<IValidationWebService, ValidationWebService>();

            return services;
        }

        /// <summary>
        /// Loads a benchmark's recorded source candles exactly as the CLI
        /// does: the benchmark's own source.csv with its recorded CSV context
        /// (T075, parity by construction).
        /// </summary>
        private static IReadOnlyList<Validator.Domain.Candles.PriceCandle> LoadBenchmarkCandles(
            string storageRoot,
            string benchmarkName)
        {
            var benchmarkDir = Path.Combine(storageRoot, "benchmarks");
            var safeName = new Validator.Application.Benchmark.BenchmarkName(benchmarkName).Safe;
            var sourcePath = Path.Combine(benchmarkDir, safeName, "source.csv");
            var snapshotJson = File.ReadAllText(Path.Combine(benchmarkDir, safeName, "benchmark.json"));
            var context = System.Text.Json.JsonSerializer.Deserialize<BenchmarkContextDto>(snapshotJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var options = new CsvInputOptions
            {
                HasHeader = context?.CsvHasHeader ?? true,
                Delimiter = context?.CsvDelimiter ?? "comma"
            };

            var candles = new List<Validator.Domain.Candles.PriceCandle>();
            var source = new CsvCandleSource(sourcePath, options);
            foreach (var candle in source.ReadAllAsync().ToBlockingEnumerable())
            {
                candles.Add(candle);
            }

            candles.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return candles;
        }

        private sealed class BenchmarkContextDto
        {
            public string? CsvDelimiter { get; set; }

            public bool CsvHasHeader { get; set; }
        }

        /// <summary>
        /// Bridges the concrete orchestrator (a class, not the interface) to
        /// IDetailedValidationUseCase without changing either.
        /// </summary>
        private sealed class OrchestratorAdapter : IDetailedValidationUseCase
        {
            private readonly DetailedValidationOrchestrator _orchestrator;

            public OrchestratorAdapter(DetailedValidationOrchestrator orchestrator)
            {
                _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            }

            public ValueTask<DetailedValidationOutcome> ExecuteAsync(
                DetailedValidationRequest request,
                CancellationToken cancellationToken = default) =>
                _orchestrator.ExecuteAsync(request, cancellationToken);
        }
    }
}