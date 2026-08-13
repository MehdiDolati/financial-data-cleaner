namespace Validator.Application.Abstractions
{
    public sealed record CandleDataStatistics(int TotalRows, int ValidRows, int MalformedRows);
}