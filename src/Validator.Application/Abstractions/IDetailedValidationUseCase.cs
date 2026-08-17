using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Reporting;
using Validator.Application.Validation;

namespace Validator.Application.Abstractions
{
    public sealed record DetailedValidationRequest(
        string SourceLabel,
        IPreparedCandleSource CandleSource,
        ValidationOptions Options,
        IMarketCalendar MarketCalendar);

    // Detailed validation use case: produces either a complete, reconciled
    // detailed report or a fatal diagnostic. The use case never writes to the
    // console or a report file.
    public interface IDetailedValidationUseCase
    {
        ValueTask<DetailedValidationOutcome> ExecuteAsync(
            DetailedValidationRequest request,
            CancellationToken cancellationToken = default);
    }
}