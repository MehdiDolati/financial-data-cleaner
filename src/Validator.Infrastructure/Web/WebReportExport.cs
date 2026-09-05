using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Web;
using Validator.Infrastructure.Reporting;

namespace Validator.Infrastructure.Web
{
    /// <summary>
    /// Bridges the boundary export path to the existing report writers
    /// (FR-014): ConciseText and DetailedText through the established text
    /// writers, JsonV1 through the v1 writer, and JsonV2 through the
    /// established v2 writer. No new serializer is introduced.
    /// </summary>
    public static class WebReportExport
    {
        public static async Task Export(
            WebRunId id,
            Validator.Application.Reporting.DetailedValidationReport report,
            ReportRepresentation representation,
            Stream destination,
            CancellationToken cancellationToken)
        {
            switch (representation)
            {
                case ReportRepresentation.ConciseText:
                    await WriteTextAsync(ConciseSummary(report), destination).ConfigureAwait(false);
                    break;
                case ReportRepresentation.DetailedText:
                {
                    using var writer = new StreamWriter(destination, new UTF8Encoding(false), 1024, leaveOpen: true);
                    await new VerboseReportWriter().WriteAsync(report, writer, cancellationToken)
                        .ConfigureAwait(false);
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }


                case ReportRepresentation.JsonV2:
                {
                    using var writer = new StreamWriter(destination, new UTF8Encoding(false), 1024, leaveOpen: true);
                    await new DetailedReportV2Writer().WriteAsync(report, writer, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                }

                default:
                    // JsonV1 is the frozen v1 contract for the legacy summary
                    // report; the web boundary exposes the detailed v2 shape.
                    throw new ArgumentOutOfRangeException(nameof(representation), representation, null);
            }
        }

        // The concise six summary lines reuse the established summary text
        // shape (feature 001) without a new renderer.
        private static string ConciseSummary(Validator.Application.Reporting.DetailedValidationReport report)
        {
            var summary = report.Summary;
            return string.Join(Environment.NewLine,
                $"Missing candles: {summary.MissingCandles}",
                $"Duplicate records: {summary.DuplicateRecords}",
                $"Invalid OHLC: {summary.InvalidOhlc}",
                $"Closed market records: {summary.ClosedMarketRecords}",
                $"Time gaps: {summary.TimeGaps}",
                $"Malformed rows: {summary.MalformedRows}");
        }

        private static async Task WriteTextAsync(string text, Stream destination)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await destination.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }
    }
}