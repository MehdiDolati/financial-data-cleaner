namespace Validator.Application.Abstractions
{
    public sealed record ReportWriteOptions(ReportFormat Format, string? OutputPath = null, bool Verbose = false);
}