using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Web;

namespace Validator.Infrastructure.Web
{
    /// <summary>
    /// File-backed IWebRunStore following the proven FileBenchmarkStore
    /// pattern: one JSON record per run under a configurable root, atomic
    /// temp-file writes, create-if-absent guarded by the deterministic id,
    /// and transitions that reject rather than coerce (research R4 interim
    /// default).
    /// </summary>
    public sealed class FileWebRunStore : IWebRunStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _root;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public FileWebRunStore(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("The web run store root must not be empty.", nameof(root));
            }

            _root = root;
        }

        public async ValueTask<WebRunRecord?> FindAsync(WebRunId id, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(id);

            var path = RecordPath(id);
            if (!File.Exists(path))
            {
                return null;
            }

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await ReadAsync(path, ct).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask<bool> TryCreateAsync(WebRunRecord record, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(record);

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(Path.Combine(_root, "runs"));
                var path = RecordPath(record.Id);
                if (File.Exists(path))
                {
                    return false;
                }

                await WriteAtomicallyAsync(path, StoredRecord.From(record), ct).ConfigureAwait(false);
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask TransitionAsync(
            WebRunId id,
            WebRunStatus target,
            WebRunTransitionData data,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(data);

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var path = RecordPath(id);
                if (!File.Exists(path))
                {
                    throw new InvalidOperationException(
                        $"Run '{id.Value}' does not exist; it cannot transition to {target}.");
                }

                var current = await ReadAsync(path, ct).ConfigureAwait(false);

                // Apply enforces the lifecycle table and the record invariants;
                // a rejected transition throws and the stored record is untouched.
                var next = current.Apply(target, data);
                await WriteAtomicallyAsync(path, StoredRecord.From(next), ct).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private string RecordPath(WebRunId id) => Path.Combine(_root, "runs", id.Value + ".json");

        private static async Task<WebRunRecord> ReadAsync(string path, CancellationToken ct)
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var stored = await JsonSerializer.DeserializeAsync<StoredRecord>(stream, SerializerOptions, ct)
                .ConfigureAwait(false);
            return stored is null
                ? throw new InvalidOperationException($"The run record '{path}' is empty.")
                : stored.ToDomain();
        }

        private static async Task WriteAtomicallyAsync(string path, StoredRecord record, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(record, SerializerOptions);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, json, ct).ConfigureAwait(false);
                File.Move(temporary, path, overwrite: false);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        /// <summary>
        /// The serialization shape of a stored run record. Kept private so the
        /// persisted form stays an adapter concern; round-tripping rebuilds
        /// through the domain constructors so every record-level invariant is
        /// re-established on load.
        /// </summary>
        private sealed record StoredRecord(
            string Id,
            string Operation,
            string Status,
            StoredSource Source,
            StoredOptions Options,
            string? BenchmarkName,
            string? ResultReference,
            StoredDiagnostic? Diagnostic,
            DateTimeOffset SubmittedAtUtc,
            DateTimeOffset? TerminalAtUtc,
            string? SubmittedBy)
        {
            public static StoredRecord From(WebRunRecord record)
            {
                var csv = record.ResolvedOptions.Csv;
                return new StoredRecord(
                    record.Id.Value,
                    record.Operation.ToString(),
                    record.Status.ToString(),
                    new StoredSource(record.Source.FileName, record.Source.ByteSize, record.Source.Sha256),
                    new StoredOptions(
                        record.ResolvedOptions.Timeframe,
                        record.ResolvedOptions.Market.ToString(),
                        record.ResolvedOptions.CalendarReference,
                        new StoredCsv(
                            csv.HasHeader,
                            csv.Delimiter,
                            csv.DateFormat,
                            csv.TimeFormat,
                            csv.TimestampFormat,
                            csv.TimestampColumn,
                            csv.TzOffset.ToString("c", CultureInfo.InvariantCulture)),
                        record.ResolvedOptions.ReportVersion,
                        record.ResolvedOptions.Score,
                        record.ResolvedOptions.ScoreWeights,
                        record.ResolvedOptions.Instrument,
                        record.ResolvedOptions.BenchmarkName,
                        record.ResolvedOptions.ToleranceOverrides),
                    record.BenchmarkName,
                    record.ResultReference,
                    StoredDiagnostic.From(record.Diagnostic),
                    record.SubmittedAtUtc,
                    record.TerminalAtUtc,
                    record.SubmittedBy);
            }

            public WebRunRecord ToDomain()
            {
                var options = new WebRunOptions(
                    Options.Timeframe,
                    Enum.Parse<Domain.Calendars.MarketProfile>(Options.Market),
                    Options.CalendarReference,
                    Options.Csv.ToDomain(),
                    Options.ReportVersion,
                    Options.Score,
                    Options.ScoreWeights,
                    Options.Instrument,
                    Options.BenchmarkName,
                    Options.ToleranceOverrides);

                var record = new WebRunRecord(
                    WebRunId.Parse(Id),
                    Enum.Parse<WebRunOperation>(Operation),
                    new SourceIdentity(Source.FileName, Source.ByteSize, Source.Sha256),
                    options,
                    SubmittedAtUtc.ToUniversalTime(),
                    SubmittedBy,
                    BenchmarkName);

                var status = Enum.Parse<WebRunStatus>(Status);
                if (status == WebRunStatus.Pending)
                {
                    return record;
                }

                if (status == WebRunStatus.Failed)
                {
                    return record.ToFailed(Diagnostic!.ToDomain(), TerminalAtUtc!.Value);
                }

                if (status == WebRunStatus.Running)
                {
                    return record.ToRunning();
                }

                return record.ToRunning()
                    .ToCompleted(ResultReference!, status == WebRunStatus.CompletedClean, TerminalAtUtc!.Value);
            }
        }

        private sealed record StoredSource(string FileName, long ByteSize, string Sha256);

        private sealed record StoredCsv(
            bool HasHeader,
            string? Delimiter,
            string? DateFormat,
            string? TimeFormat,
            string? TimestampFormat,
            string? TimestampColumn,
            string TzOffset)
        {
            public CsvInputOptions ToDomain() => new()
            {
                HasHeader = HasHeader,
                Delimiter = Delimiter,
                DateFormat = DateFormat,
                TimeFormat = TimeFormat,
                TimestampFormat = TimestampFormat,
                TimestampColumn = TimestampColumn,
                TzOffset = TimeSpan.Parse(TzOffset, CultureInfo.InvariantCulture)
            };
        }

        private sealed record StoredOptions(
            string? Timeframe,
            string Market,
            string? CalendarReference,
            StoredCsv Csv,
            int ReportVersion,
            bool Score,
            string? ScoreWeights,
            string? Instrument,
            string? BenchmarkName,
            string? ToleranceOverrides);

        private sealed record StoredDiagnostic(
            string Code,
            string Reason,
            string Guidance,
            StoredPartialSource? Source,
            StoredFailureLocation? Location)
        {
            public static StoredDiagnostic? From(FatalDiagnostic? diagnostic) => diagnostic is null
                ? null
                : new StoredDiagnostic(
                    diagnostic.Code,
                    diagnostic.Reason,
                    diagnostic.Guidance,
                    diagnostic.Source is null
                        ? null
                        : new StoredPartialSource(
                            diagnostic.Source.FileName,
                            diagnostic.Source.ByteSize,
                            diagnostic.Source.Sha256),
                    diagnostic.Location is null
                        ? null
                        : new StoredFailureLocation(
                            diagnostic.Location.SourceLine,
                            diagnostic.Location.TimestampUtc,
                            diagnostic.Location.Field));

            public FatalDiagnostic ToDomain() => new(
                Code,
                Reason,
                Guidance,
                Source is null ? null : new PartialSourceIdentity(Source.FileName, Source.ByteSize, Source.Sha256),
                Location is null
                    ? null
                    : new FailureLocation(Location.SourceLine, Location.TimestampUtc, Location.Field));
        }

        private sealed record StoredPartialSource(string FileName, long? ByteSize, string? Sha256);

        private sealed record StoredFailureLocation(long? SourceLine, DateTimeOffset? TimestampUtc, string? Field);
    }
}