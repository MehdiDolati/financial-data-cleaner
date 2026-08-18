using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Domain.Candles;
using Validator.Domain.Findings;

namespace Validator.Infrastructure.Csv
{
    // Infrastructure adapter that establishes everything a detailed report must
    // state about its input in one preparation pass: a SHA-256 identity over the
    // exact source bytes, the resolved CSV interpretation actually applied, the
    // row-level scan coverage, and replayable candle data. Expected input
    // problems are returned as classified fatal diagnostics so the Application
    // layer never has to interpret Infrastructure exceptions.
    public sealed class PreparedCsvCandleSource : IPreparedCandleSource, IMalformedRowSource
    {
        private readonly string _path;
        private readonly CsvInputOptions _options;
        private readonly CsvCandleSource _inner;
        private List<PriceCandle> _candles = [];

        public PreparedCsvCandleSource(string path, CsvInputOptions? options = null)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _options = options ?? new CsvInputOptions();
            _inner = new CsvCandleSource(_path, _options);
        }

        public IReadOnlyList<MalformedRow> MalformedRows => _inner.MalformedRows;

        public IAsyncEnumerable<PriceCandle> ReadAllAsync() => _inner.ReadAllAsync();

        public async ValueTask<PreparedCandleDataResult> PrepareAsync(
            CsvInputOptions options,
            CancellationToken cancellationToken = default)
        {
            var fileName = SafeBaseName(_path);

            SourceIdentity identity;
            try
            {
                await using var bytes = new FileStream(
                    _path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                identity = await new SourceIdentityProvider()
                    .ComputeAsync(bytes, fileName, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is FileNotFoundException or
                DirectoryNotFoundException or
                UnauthorizedAccessException or
                IOException)
            {
                return Fail(
                    "SOURCE_UNAVAILABLE",
                    "The validated source could not be opened for reading.",
                    exception.Message,
                    fileName);
            }

            var candles = new List<PriceCandle>();
            try
            {
                await foreach (var candle in _inner.ReadAllAsync().WithCancellation(cancellationToken))
                {
                    candles.Add(candle);
                }
            }
            catch (DecoderFallbackException exception)
            {
                return Fail(
                    "INVALID_ENCODING",
                    "The source bytes are not valid UTF-8 text.",
                    exception.Message,
                    fileName,
                    identity);
            }
            catch (InvalidDataException exception)
            {
                return Fail(
                    "INVALID_STRUCTURE",
                    "The source rows do not match a usable OHLCV column layout.",
                    exception.Message,
                    fileName,
                    identity);
            }
            catch (ArgumentException exception)
            {
                return Fail(
                    "AMBIGUOUS_DELIMITER",
                    "The source delimiter could not be resolved unambiguously.",
                    exception.Message,
                    fileName,
                    identity);
            }
            catch (FileNotFoundException exception)
            {
                return Fail(
                    "SOURCE_UNAVAILABLE",
                    "The validated source could not be opened for reading.",
                    exception.Message,
                    fileName,
                    identity);
            }

            _candles = candles;

            var malformed = _inner.MalformedRows.Count;
            var examined = _inner.PhysicalRowsExamined;
            var accepted = examined - malformed;
            if (accepted < 0)
            {
                return Fail(
                    "INVALID_CSV",
                    "The scan coverage could not be reconciled against the examined rows.",
                    "Re-run validation; accepted rows must never exceed examined rows.",
                    fileName,
                    identity);
            }

            var coverage = new ScanCoverage(examined, accepted, malformed);
            var csv = ResolveContext(candles);

            return new PreparedCandleDataResult.Succeeded(
                new ReplayableCandles(candles),
                identity,
                csv,
                coverage);
        }

        // Reports exactly the interpretation the reader applied, so a consumer
        // can reproduce the run from the report alone.
        private ResolvedCsvContext ResolveContext(List<PriceCandle> candles)
        {
            var offset = FormatOffset(_options.TzOffset);
            var timestamp = _inner.ResolvedCombinedTimestamp
                ? TimestampInterpretation.CreateCombined(
                    _inner.ResolvedTimestampFormat ?? _options.TimestampFormat ?? "yyyy-MM-dd HH:mm:ss",
                    _inner.ResolvedTimestampColumn ?? _options.TimestampColumn ?? "1",
                    offset)
                : TimestampInterpretation.CreateSeparate(
                    _inner.ResolvedDateFormat ?? _options.DateFormat ?? "yyyy.MM.dd",
                    _inner.ResolvedTimeFormat ?? _options.TimeFormat ?? "HH:mm",
                    offset);

            DateRange? range = null;
            if (candles.Count > 0)
            {
                var earliest = candles[0].Timestamp;
                var latest = candles[0].Timestamp;
                foreach (var candle in candles)
                {
                    if (candle.Timestamp < earliest)
                    {
                        earliest = candle.Timestamp;
                    }

                    if (candle.Timestamp > latest)
                    {
                        latest = candle.Timestamp;
                    }
                }

                range = new DateRange(earliest, latest);
            }

            return new ResolvedCsvContext(
                _inner.ResolvedDelimiter ?? ',',
                _inner.ResolvedHasHeader,
                timestamp,
                range);
        }

        private static PreparedCandleDataResult Fail(
            string code,
            string reason,
            string guidance,
            string fileName,
            SourceIdentity? identity = null)
        {
            var source = identity is null
                ? new PartialSourceIdentity(fileName)
                : new PartialSourceIdentity(identity.FileName, identity.ByteSize, identity.Sha256);
            return new PreparedCandleDataResult.Failed(
                new FatalDiagnostic(code, reason, guidance, source));
        }

        private static string FormatOffset(TimeSpan offset)
        {
            var sign = offset < TimeSpan.Zero ? '-' : '+';
            var absolute = offset < TimeSpan.Zero ? offset.Negate() : offset;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{sign}{absolute.Hours:D2}:{absolute.Minutes:D2}");
        }

        private static string SafeBaseName(string path)
        {
            var normalized = path.Replace('\\', '/');
            var lastSeparator = normalized.LastIndexOf('/');
            var baseName = lastSeparator >= 0 ? normalized[(lastSeparator + 1)..] : normalized;
            return string.IsNullOrWhiteSpace(baseName) ? "source.csv" : baseName;
        }

        // Replay reads the candles established by the single preparation pass,
        // so the report can never disagree with the bytes that were hashed.
        private sealed class ReplayableCandles : IReplayableCandleData
        {
            private readonly List<PriceCandle> _candles;

            public ReplayableCandles(List<PriceCandle> candles)
            {
                _candles = candles;
            }

            public async IAsyncEnumerable<PriceCandle> ReplayAsync()
            {
                foreach (var candle in _candles)
                {
                    yield return candle;
                }

                await Task.CompletedTask.ConfigureAwait(false);
            }
        }
    }
}
