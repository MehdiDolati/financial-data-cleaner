using System;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;

namespace Validator.Infrastructure.Reporting
{
    public sealed class TextReportWriter : IReportWriter
    {
        public Task WriteReportAsync(object report)
        {
            if (report is ValidationReport validationReport)
            {
                var clean = validationReport.IsClean ? "Clean" : "Issues found";
                var lines = new[]
                {
                    $"Status: {clean}",
                    $"TotalFindings: {validationReport.Summary.TotalFindings}",
                    $"MalformedRows: {validationReport.Summary.MalformedRows}",
                    $"ValidRows: {validationReport.Summary.ValidRows}",
                    $"DateRange: {validationReport.Range?.Start:O} -> {validationReport.Range?.End:O}",
                    $"Source: {validationReport.SourceFile}"
                };

                Console.WriteLine(string.Join(Environment.NewLine, lines));
                return Task.CompletedTask;
            }

            if (report is ValidationSummary summary)
            {
                Console.WriteLine($"Status: {(summary.IsClean ? "Clean" : "Issues found")}");
                Console.WriteLine($"TotalFindings: {summary.TotalFindings}");
                Console.WriteLine($"MalformedRows: {summary.MalformedRows}");
                Console.WriteLine($"ValidRows: {summary.ValidRows}");
                Console.WriteLine("DateRange: n/a");
                Console.WriteLine("Source: n/a");
                return Task.CompletedTask;
            }

            throw new NotSupportedException($"Unsupported report type: {report?.GetType().Name ?? "null"}");
        }
    }
}