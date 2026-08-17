using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Validation;

namespace Validator.Application.Abstractions
{
    /// <summary>
    /// Everything one detailed validation run needs: what to read, how to read
    /// it, which calendar applies, and which checks to run.
    /// </summary>
    public sealed record DetailedValidationRequest(
        string SourceLabel,
        IPreparedCandleSource CandleSource,
        ValidationOptions Options,
        IMarketCalendar MarketCalendar,
        CsvInputOptions CsvOptions);

    // Detailed validation use case: produces either a complete, reconciled
    // detailed report or a fatal diagnostic. The use case never writes to the
    // console or a report file.
    public interface IDetailedValidationUseCase
    {
        /// <summary>
        /// Runs validation and returns either a complete reconciled report or the
        /// diagnostic explaining why one could not be produced.
        /// </summary>
        ValueTask<DetailedValidationOutcome> ExecuteAsync(
            DetailedValidationRequest request,
            CancellationToken cancellationToken = default);
    }
}