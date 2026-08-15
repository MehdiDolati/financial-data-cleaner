using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;

namespace Validator.Infrastructure.Reporting
{
    public sealed class TextReportWriter : IReportWriter
    {
        private readonly bool _verbose;

        public TextReportWriter(bool verbose = false)
        {
            _verbose = verbose;
        }

        public string? LastWrittenText { get; private set; }

        public Task WriteReportAsync(object report)
        {
            if (report is ValidationReport validationReport)
            {
                var lines = new List<string>
                {
                    $"Missing candles: {validationReport.Summary.MissingCandles}",
                    $"Duplicate records: {validationReport.Summary.DuplicateRecords}",
                    $"Invalid OHLC: {validationReport.Summary.InvalidOhlc}",
                    $"Closed-market records: {validationReport.Summary.ClosedMarketRecords}",
                    $"Time gaps: {validationReport.Summary.TimeGaps}",
                    $"Malformed rows: {validationReport.Summary.MalformedRows}"
                };

                if (_verbose && validationReport.Findings.Count > 0)
                {
                    lines.Add(string.Empty);
                    lines.Add("Findings:");
                    foreach (var finding in validationReport.Findings)
                    {
                        var timestamp = finding.Timestamp?.ToUniversalTime().ToString("O") ?? "n/a";
                        var line = finding.Line?.ToString() ?? "n/a";
                        lines.Add($"{finding.Category}: timestamp={timestamp}; line={line}; {finding.Message}");
                    }
                }

                LastWrittenText = string.Join(Environment.NewLine, lines);
                return Task.CompletedTask;
            }

            if (report is ValidationSummary summary)
            {
                var lines = new[]
                {
                    $"Missing candles: {summary.MissingCandles}",
                    $"Duplicate records: {summary.DuplicateRecords}",
                    $"Invalid OHLC: {summary.InvalidOhlc}",
                    $"Closed-market records: {summary.ClosedMarketRecords}",
                    $"Time gaps: {summary.TimeGaps}",
                    $"Malformed rows: {summary.MalformedRows}"
                };

                LastWrittenText = string.Join(Environment.NewLine, lines);
                return Task.CompletedTask;
            }

            throw new NotSupportedException($"Unsupported report type: {report?.GetType().Name ?? "null"}");
        }
    }
}