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

    public abstract record ReportPublicationResult
    {
        public sealed record Succeeded : ReportPublicationResult;

        public sealed record Failed(FatalDiagnostic Diagnostic) : ReportPublicationResult;
    }

    public interface IReportPublisher
    {
        ValueTask<ReportPublicationResult> PublishAsync(
            ISuccessReportWriter writer,
            DetailedValidationReport report,
            ReportDestination destination,
            CancellationToken cancellationToken = default);
    }
}