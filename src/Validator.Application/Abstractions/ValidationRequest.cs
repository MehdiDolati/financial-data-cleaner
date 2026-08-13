namespace Validator.Application.Abstractions
{
    public sealed record ValidationRequest(string InputPath, string? Timeframe = null, ReportFormat Format = ReportFormat.Text, string? OutputPath = null, bool Verbose = false, IMarketCalendar? MarketCalendar = null);
}