using System;
using Validator.Application.Scoring;
using Validator.Domain.Timeframes;

namespace Validator.Application.Validation
{
    public sealed record ValidationOptions
    {
        public string? TimeframeOverride { get; init; }
        public bool Verbose { get; init; } = false;

        // The opt-in scoring request. Null means scoring was not requested, so
        // the run behaves exactly as before and the report carries no score.
        public ScoreRequest? Score { get; init; }

        public Timeframe? GetParsedTimeframe()
        {
            if (string.IsNullOrWhiteSpace(TimeframeOverride)) return null;
            if (Timeframe.TryParse(TimeframeOverride!, out var tf)) return tf;
            throw new FormatException("Invalid timeframe override");
        }
    }
}


