using System;
using Validator.Domain.Timeframes;

namespace Validator.Application.Validation
{
    public sealed record ValidationOptions
    {
        public string? TimeframeOverride { get; init; }
        public bool Verbose { get; init; } = false;

        public Timeframe? GetParsedTimeframe()
        {
            if (string.IsNullOrWhiteSpace(TimeframeOverride)) return null;
            if (Timeframe.TryParse(TimeframeOverride!, out var tf)) return tf;
            throw new FormatException("Invalid timeframe override");
        }
    }
}