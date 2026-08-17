using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Reporting;

namespace Validator.Application.Abstractions
{
    /// <summary>
    /// The forms a successful report can be published in.
    /// </summary>
    /// <remarks>
    /// The v1 JSON form is kept so existing consumers keep working unchanged
    /// while v2 carries the detailed findings.
    /// </remarks>
    public enum ReportRepresentation
    {
        /// <summary>Human-readable summary text.</summary>
        ConciseText,

        /// <summary>Human-readable text including per-finding detail.</summary>
        DetailedText,

        /// <summary>The original JSON shape, retained for existing consumers.</summary>
        JsonV1,

        /// <summary>The detailed JSON shape introduced with this report contract.</summary>
        JsonV2
    }

    // Writes one successful detailed validation report as a streamed UTF-8
    // document. Writers never build the whole report as a string and never
    // close the destination.
    public interface ISuccessReportWriter
    {
        /// <summary>The form this writer produces.</summary>
        ReportRepresentation Representation { get; }

        /// <summary>Streams the report to the destination without closing it.</summary>
        ValueTask WriteAsync(
            DetailedValidationReport report,
            Stream destination,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The forms a fatal diagnostic can be reported in.
    /// </summary>
    public enum FatalRepresentation
    {
        /// <summary>Human-readable text.</summary>
        Text,

        /// <summary>A single structured JSON document.</summary>
        JsonV2
    }

    // Writes one fatal diagnostic to the standard-error stream. A v2 fatal
    // writer emits exactly one structured JSON document.
    public interface IFatalDiagnosticWriter
    {
        /// <summary>The form this writer produces.</summary>
        FatalRepresentation Representation { get; }

        /// <summary>Writes the diagnostic to the error stream without closing it.</summary>
        ValueTask WriteAsync(
            FatalDiagnostic diagnostic,
            Stream standardError,
            CancellationToken cancellationToken = default);
    }
}