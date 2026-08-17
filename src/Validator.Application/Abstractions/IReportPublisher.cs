using System;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Reporting;

namespace Validator.Application.Abstractions
{
    // Report publication target. A file destination is replaced atomically
    // only after the report rendered completely; the absence of a path means
    // the staged artifact is copied to stdout.
    public sealed record ReportDestination
    {
        public string? FilePath { get; }

        public bool IsFile => !string.IsNullOrWhiteSpace(FilePath);

        public static ReportDestination None { get; } = new((string?)null);

        public ReportDestination(string? filePath)
        {
            FilePath = filePath;
        }
    }

    /// <summary>
    /// The outcome of publishing a rendered report.
    /// </summary>
    public abstract record ReportPublicationResult
    {
        /// <summary>The report reached its destination in full.</summary>
        public sealed record Succeeded : ReportPublicationResult;

        /// <summary>Publication failed, so no complete report exists at the destination.</summary>
        public sealed record Failed(FatalDiagnostic Diagnostic) : ReportPublicationResult;
    }

    /// <summary>
    /// Publishes a finished report to its destination.
    /// </summary>
    /// <remarks>
    /// A file destination is only replaced once the report has rendered
    /// completely, so an interrupted run cannot leave a truncated report that
    /// looks whole.
    /// </remarks>
    public interface IReportPublisher
    {
        /// <summary>Renders the report with the given writer and publishes it.</summary>
        ValueTask<ReportPublicationResult> PublishAsync(
            ISuccessReportWriter writer,
            DetailedValidationReport report,
            ReportDestination destination,
            CancellationToken cancellationToken = default);
    }
}