using System;

namespace Validator.Application.Reporting
{
    public sealed record ValidationReport
    {
        public ValidationSummary Summary { get; init; }
        public DateRange? Range { get; init; }
        public string SourceFile { get; init; } = string.Empty;

        public bool IsClean => Summary?.IsClean ?? false;

        public ValidationReport(ValidationSummary summary, DateRange? range, string sourceFile)
        {
            Summary = summary;
            Range = range;
            SourceFile = sourceFile ?? string.Empty;
        }
    }
}