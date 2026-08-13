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
                var clean = validationReport.IsClean ? "Clean" : "Issues found";
                var lines = new List<string>
                {
                    $"Status: {clean}",
                    $"TotalFindings: {validationReport.Summary.TotalFindings}",
                    $"MalformedRows: {validationReport.Summary.MalformedRows}",
                    $"ValidRows: {validationReport.Summary.ValidRows}",
                    $"DateRange: {validationReport.Range?.Start:O} -> {validationReport.Range?.End:O}",
                    $"Source: {validationReport.SourceFile}"
                };

                if (_verbose)
                {
                    foreach (var finding in validationReport.Findings)
                    {
                        lines.Add($"Finding: {finding.Category} | {finding.Message}");
                    }
                }

                LastWrittenText = string.Join(Environment.NewLine, lines);
                Console.WriteLine(LastWrittenText);
                return Task.CompletedTask;
            }

            if (report is ValidationSummary summary)
            {
                var lines = new[]
                {
                    $"Status: {(summary.IsClean ? "Clean" : "Issues found")}",
                    $"TotalFindings: {summary.TotalFindings}",
                    $"MalformedRows: {summary.MalformedRows}",
                    $"ValidRows: {summary.ValidRows}",
                    "DateRange: n/a",
                    "Source: n/a"
                };

                LastWrittenText = string.Join(Environment.NewLine, lines);
                Console.WriteLine(LastWrittenText);
                return Task.CompletedTask;
            }

            throw new NotSupportedException($"Unsupported report type: {report?.GetType().Name ?? "null"}");
        }
    }
}