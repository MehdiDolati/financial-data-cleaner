using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Benchmark;
using Validator.Application.Comparison;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Application.Validation;
using DomainCandles = Validator.Domain.Candles;

namespace Validator.Application.Web
{
    /// <summary>
    /// The Application run executor invoked by the queue: transitions the run
    /// to Running, replays the stored upload through the existing use cases
    /// (validation, optionally scoring; establishment and comparison for the
    /// benchmark operations), persists the result artifact, and transitions
    /// to CompletedClean/CompletedWithFindings (guarded on the report's own
    /// cleanliness) or Failed with a fatal diagnostic. It never exposes
    /// partial counts (T031, FR-008, FR-011).
    /// </summary>
    public sealed class WebRunExecutor
    {
        private readonly IWebRunStore _runStore;
        private readonly IUploadedDatasetStore _uploadStore;
        private readonly IDetailedValidationUseCase _validationUseCase;
        private readonly IMarketCalendarFactory _calendarFactory;
        private readonly IApplicationClock _clock;
        private readonly IWebResultStore _resultStore;
        private readonly Func<WebRunId, DetailedValidationReport, ReportRepresentation, Stream, CancellationToken, Task>? _exportThrough;
        private readonly IBenchmarkStore? _benchmarkStore;
        private readonly Func<string, IReadOnlyList<DomainCandles.PriceCandle>>? _loadBenchmarkCandles;

        public WebRunExecutor(
            IWebRunStore runStore,
            IUploadedDatasetStore uploadStore,
            IDetailedValidationUseCase validationUseCase,
            IMarketCalendarFactory calendarFactory,
            IApplicationClock clock)
            : this(runStore, uploadStore, validationUseCase, calendarFactory, clock, null, null, null, null)
        {
            _ = _runStore; // no-op: every guard runs in the delegating target
        }

        internal WebRunExecutor(
            IWebRunStore runStore,
            IUploadedDatasetStore uploadStore,
            IDetailedValidationUseCase validationUseCase,
            IMarketCalendarFactory calendarFactory,
            IApplicationClock clock,
            IWebResultStore? resultStore,
            Func<WebRunId, DetailedValidationReport, ReportRepresentation, Stream, CancellationToken, Task>? exportThrough,
            IBenchmarkStore? benchmarkStore,
            Func<string, IReadOnlyList<DomainCandles.PriceCandle>>? loadBenchmarkCandles)
        {
            _runStore = runStore ?? throw new ArgumentNullException(nameof(runStore));
            _uploadStore = uploadStore ?? throw new ArgumentNullException(nameof(uploadStore));
            _validationUseCase = validationUseCase ?? throw new ArgumentNullException(nameof(validationUseCase));
            _calendarFactory = calendarFactory ?? throw new ArgumentNullException(nameof(calendarFactory));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _resultStore = resultStore!;
            _exportThrough = exportThrough;
            _benchmarkStore = benchmarkStore;
            _loadBenchmarkCandles = loadBenchmarkCandles;
        }

        /// <summary>
        /// Runs one durably-Pending run to a terminal state. The upload is
        /// located through the run record's source identity, so the bytes
        /// validated are the bytes that were hashed (SC-008).
        /// </summary>
        public async Task ExecuteAsync(WebRunId id, CancellationToken cancellationToken = default)
        {
            var record = await _runStore.FindAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Run '{id.Value}' does not exist; the queue may only execute durable pending runs.");

            // The executor is idempotent per lifecycle state: a duplicate
            // enqueue never re-runs Running or terminal work.
            if (record.Status != WebRunStatus.Pending)
            {
                return;
            }

            try
            {
                await _runStore.TransitionAsync(
                    id,
                    WebRunStatus.Running,
                    WebRunTransitionData.ForRunning(),
                    cancellationToken).ConfigureAwait(false);

                var options = record.ResolvedOptions;
                var scoreRequest = options.Score
                    ? (options.ScoreWeights is null
                        ? ScoreRequest.Default()
                        : new ScoreRequest(ScoreWeightParser.Parse(options.ScoreWeights)))
                    : null;

                var dataset = new UploadedDataset(record.Source, record.Source.Sha256 + ".csv");
                var source = await _uploadStore.OpenAsync(dataset, options.Csv, cancellationToken)
                    .ConfigureAwait(false);

                var outcome = await _validationUseCase.ExecuteAsync(
                    new DetailedValidationRequest(
                        record.Source.FileName,
                        source,
                        new ValidationOptions
                        {
                            TimeframeOverride = options.Timeframe,
                            Score = scoreRequest
                        },
                        _calendarFactory.Create(new LocalCalendarRequest(options.Market, options.CalendarReference)),
                        options.Csv),
                    cancellationToken).ConfigureAwait(false);

                if (outcome is DetailedValidationOutcome.Failed failed)
                {
                    await _runStore.TransitionAsync(
                        id,
                        WebRunStatus.Failed,
                        WebRunTransitionData.ForFailure(
                            WithSourceName(failed.Diagnostic, record.Source.FileName),
                            _clock.UtcNow),
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                var report = ((DetailedValidationOutcome.Succeeded)outcome).Report;
                var instrument = string.IsNullOrWhiteSpace(options.Instrument) ? "UNKNOWN" : options.Instrument!.Trim();
                var finalReport = report with { Instrument = instrument };

                // Persist the report's typed sections and the export callback
                // (through the existing writers) before the terminal
                // transition, so a Completed* state always has its result
                // (FR-013, FR-014).
                var availableExports = new[]
                {
                    ReportRepresentation.ConciseText,
                    ReportRepresentation.DetailedText,
                    ReportRepresentation.JsonV2
                };

                var validationSection = new WebValidationSection(
                    finalReport.Context,
                    finalReport.Coverage,
                    finalReport.Checks,
                    finalReport.Reconciliation,
                    finalReport.Summary,
                    finalReport.Findings,
                    instrument);

                var scoringSection = finalReport.Score is null
                    ? null
                    : new WebScoringSection(finalReport.Score);

                WebBenchmarkSection? benchmarkSection = null;
                WebComparisonSection? comparisonSection = null;
                // The record constructor guarantees a benchmark name for
                // EstablishBenchmark and Compare, so no null check is needed.
                if (record.Operation == WebRunOperation.EstablishBenchmark)
                {
                    // Establishment persists the validated dataset as a named
                    // immutable snapshot through the existing use case
                    // (FR-002, FR-004, SC-006). Establishment requires a score.
                    var establishStore = _benchmarkStore ?? throw new InvalidOperationException(
                        "A benchmark store is required for establish-benchmark runs.");
                    var establish = new EstablishBenchmarkUseCase(establishStore, _clock);
                    var snapshot = await establish.ExecuteAsync(
                        finalReport,
                        options.BenchmarkName!,
                        _uploadStore.ResolveContentPath(dataset),
                        cancellationToken).ConfigureAwait(false);

                    benchmarkSection = new WebBenchmarkSection(
                        snapshot.Name,
                        snapshot.Source,
                        snapshot.Context,
                        snapshot.Metrics,
                        snapshot.Dataset,
                        snapshot.Instrument);
                }
                else if (record.Operation == WebRunOperation.Compare)
                {
                    // Comparison matches the candidate against the named
                    // benchmark through the existing use case (FR-017, SC-006).
                    var compareStore = _benchmarkStore ?? throw new InvalidOperationException(
                        "A benchmark store is required for compare runs.");
                    var comparison = await RunComparisonAsync(compareStore, record, finalReport, dataset, options, cancellationToken)
                        .ConfigureAwait(false);
                    comparisonSection = new WebComparisonSection(comparison);
                }

                var result = new WebRunResult(
                    validationSection,
                    scoringSection,
                    benchmarkSection,
                    comparisonSection,
                    availableExports,
                    (representation, destination, token) => ExportAsync(id, finalReport, representation, destination, token));

                if (_resultStore is not null)
                {
                    await _resultStore.SaveAsync(id, result, cancellationToken).ConfigureAwait(false);
                }

                var resultReference = "results/" + id.Value + ".json";

                // The completion state is selected by the reconciled report's
                // own cleanliness - never inferred from the absence of an
                // error (SC-003).
                await _runStore.TransitionAsync(
                    id,
                    finalReport.Summary.IsClean ? WebRunStatus.CompletedClean : WebRunStatus.CompletedWithFindings,
                    WebRunTransitionData.ForSuccess(resultReference, _clock.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is
                InvalidOperationException or
                InvalidDataException or
                FormatException or
                System.Text.DecoderFallbackException or
                ArgumentException or
                IOException or
                UnauthorizedAccessException or
                KeyNotFoundException)
            {
                // An unusable dataset or environment failure ends the run as a
                // fatal diagnostic - never as a partial report.
                var diagnostic = ToFatalDiagnostic(exception, record.Source.FileName);
                await _runStore.TransitionAsync(
                    id,
                    WebRunStatus.Failed,
                    WebRunTransitionData.ForFailure(diagnostic, _clock.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Mirrors the CLI's comparison flow: load the benchmark and its
        /// recorded source candles, load the candidate candles from the
        /// stored upload, and compare through the established use case
        /// (parity by construction, T029/T057).
        /// </summary>
        private async Task<ComparisonReport> RunComparisonAsync(
            IBenchmarkStore store,
            WebRunRecord record,
            DetailedValidationReport report,
            UploadedDataset dataset,
            WebRunOptions options,
            CancellationToken cancellationToken)
        {
            var benchmark = await store.LoadAsync(options.BenchmarkName!, cancellationToken).ConfigureAwait(false);

            var benchmarkCandles = _loadBenchmarkCandles is not null
                ? _loadBenchmarkCandles(options.BenchmarkName!)
                : throw new InvalidOperationException(
                    "A benchmark candle loader is required for compare runs.");

            var candidateSource = await _uploadStore.OpenAsync(dataset, options.Csv, cancellationToken)
                .ConfigureAwait(false);
            var candidateCandles = new List<DomainCandles.PriceCandle>();
            await foreach (var candle in candidateSource.ReadAllAsync().ConfigureAwait(false))
            {
                candidateCandles.Add(candle);
            }

            candidateCandles.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            var identity = new CandidateIdentity(report.Source, report.Context, options.Instrument!);
            return new CompareDatasetsUseCase().Compare(
                benchmark,
                benchmarkCandles,
                candidateCandles,
                identity,
                options.ToleranceOverrides is null
                ? null
                : ToleranceResolver.ParseOverrides(options.ToleranceOverrides)) with
            {
                CandidateScore = report.Score
            };
        }

        /// <summary>
        /// Streams the stored report through the existing writers. The
        /// delegate is captured per run so the persisted result carries its
        /// own export path without introducing a new serializer (FR-014).
        /// </summary>
        private Task ExportAsync(
            WebRunId id,
            DetailedValidationReport report,
            ReportRepresentation representation,
            Stream destination,
            CancellationToken cancellationToken)
        {
            if (_exportThrough is not null)
            {
                return _exportThrough(id, report, representation, destination, cancellationToken);
            }

            // In-process default: no writer composition available (tests and
            // Application-level scenarios); the artifact path is recorded and
            // the concrete writer is bound by the Infrastructure composition.
            return Task.CompletedTask;
        }

        private static FatalDiagnostic WithSourceName(FatalDiagnostic diagnostic, string fileName) =>
            diagnostic.Source is null
                ? new FatalDiagnostic(
                    diagnostic.Code,
                    diagnostic.Reason,
                    diagnostic.Guidance,
                    new PartialSourceIdentity(fileName))
                : diagnostic;

        // Mirrors the CLI's established classification of ingestion failures
        // (ValidateCommand.ToFatalDiagnostic) without restating any rule.
        internal static FatalDiagnostic ToFatalDiagnostic(Exception exception, string fileName)
        {
            var (code, reason, guidance) = exception switch
            {
                System.Text.DecoderFallbackException => (
                    "INVALID_ENCODING",
                    "The source bytes are not valid text in the expected encoding.",
                    "Re-export the file as UTF-8 or ASCII without invalid byte sequences."),
                InvalidDataException data when data.Message.Contains(
                    "Invalid CSV", StringComparison.OrdinalIgnoreCase) => (
                    "INVALID_CSV",
                    "The source is not parsable as delimited text.",
                    data.Message),
                InvalidDataException data => (
                    "INVALID_STRUCTURE",
                    "The source does not expose the columns the active layout requires.",
                    data.Message),
                InvalidOperationException structure => (
                    "INVALID_STRUCTURE",
                    "The source does not expose the columns the active layout requires.",
                    structure.Message),
                IOException or UnauthorizedAccessException => (
                    "SOURCE_UNAVAILABLE",
                    "The validated source could not be read to completion.",
                    exception.Message),
                _ => (
                    "INVALID_ARGUMENT",
                    "The supplied options cannot be applied to this source.",
                    exception.Message)
            };

            return new FatalDiagnostic(code, reason, guidance, new PartialSourceIdentity(fileName));
        }
    }
}
