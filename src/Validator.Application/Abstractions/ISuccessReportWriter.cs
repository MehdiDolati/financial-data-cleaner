using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Reporting;

namespace Validator.Application.Abstractions
{
    public enum ReportRepresentation
    {
        ConciseText,
        DetailedText,
        JsonV1,
        JsonV2
    }

    // Writes one successful detailed validation report as a streamed UTF-8
    // document. Writers never build the whole report as a string and never
    // close the destination.
    public interface ISuccessReportWriter
    {
        ReportRepresentation Representation { get; }

        ValueTask WriteAsync(
            DetailedValidationReport report,
            Stream destination,
            CancellationToken cancellationToken = default);
    }

    public enum FatalRepresentation
    {
        Text,
        JsonV2
    }

    // Writes one fatal diagnostic to the standard-error stream. A v2 fatal
    // writer emits exactly one structured JSON document.
    public interface IFatalDiagnosticWriter
    {
        FatalRepresentation Representation { get; }

        ValueTask WriteAsync(
            FatalDiagnostic diagnostic,
            Stream standardError,
            CancellationToken cancellationToken = default);
    }
}