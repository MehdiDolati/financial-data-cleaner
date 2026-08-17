using System;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;

namespace Validator.Application.Abstractions
{
    // Resolved CSV interpretation facts that materially affect validation
    // results, captured from the same handle that produced the data.
    public sealed record ResolvedCsvContext(
        char Delimiter,
        bool HasHeader,
        TimestampInterpretation Timestamp,
        DateRange? DateRange);

    public abstract record PreparedCandleDataResult
    {
        public sealed record Succeeded(
            IReplayableCandleData Data,
            SourceIdentity Source,
            ResolvedCsvContext Csv,
            ScanCoverage Coverage) : PreparedCandleDataResult;

        public sealed record Failed(
            FatalDiagnostic Diagnostic) : PreparedCandleDataResult;
    }

    // A candle source that can prepare replayable data with a source identity
    // and resolved context in one pass over stable source bytes.
    public interface IPreparedCandleSource : ICandleSource
    {
        ValueTask<PreparedCandleDataResult> PrepareAsync(
            CsvInputOptions options,
            CancellationToken cancellationToken = default);
    }
}