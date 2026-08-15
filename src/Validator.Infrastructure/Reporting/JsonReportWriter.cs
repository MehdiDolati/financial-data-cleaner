using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;
using Validator.Domain.Findings;

namespace Validator.Infrastructure.Reporting
{
    public sealed class JsonReportWriter : IReportWriter
    {
        public string? LastWrittenText { get; private set; }

        public Task WriteReportAsync(object report)
        {
            if (report is not ValidationReport validationReport)
            {
                throw new NotSupportedException($"Unsupported report type: {report?.GetType().Name ?? "null"}");
            }

            var document = new
            {
                sourceFile = Path.GetFileName(validationReport.SourceFile),
                detectedTimeframe = string.IsNullOrWhiteSpace(validationReport.DetectedTimeframe) ? "H1" : validationReport.DetectedTimeframe,
                totalRecords = validationReport.TotalRecords,
                dateRange = validationReport.Range is null
                    ? null
                    : new
                    {
                        from = ToUtcString(validationReport.Range.Start),
                        to = ToUtcString(validationReport.Range.End)
                    },
                summary = new
                {
                    missingCandles = validationReport.Summary.MissingCandles,
                    duplicateRecords = validationReport.Summary.DuplicateRecords,
                    invalidOhlc = validationReport.Summary.InvalidOhlc,
                    closedMarketRecords = validationReport.Summary.ClosedMarketRecords,
                    timeGaps = validationReport.Summary.TimeGaps,
                    malformedRows = validationReport.Summary.MalformedRows
                },
                isClean = validationReport.IsClean,
                findings = validationReport.Findings.Select(f => new
                {
                    category = f.Category.ToString(),
                    timestamp = f.Timestamp is null ? null : ToUtcString(f.Timestamp.Value),
                    line = f.Line,
                    message = f.Message
                }).ToArray()
            };

            LastWrittenText = JsonSerializer.Serialize(document, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            return Task.CompletedTask;
        }

        private static string ToUtcString(DateTimeOffset value)
        {
            var utc = value.ToUniversalTime();
            return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }
    }
}
