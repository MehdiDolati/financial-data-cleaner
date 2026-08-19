using System;

namespace Validator.Application.Scoring
{
    // A caller's opt-in scoring request. It carries only the resolved weighting
    // to apply — the default equal weighting, or a caller-supplied one covering
    // all six metrics. Weight validation happens before the request is built, so
    // by the time it reaches the run the weighting is already known good.
    public sealed record ScoreRequest
    {
        public ScoreWeighting Weighting { get; }

        public ScoreRequest(ScoreWeighting weighting)
        {
            Weighting = weighting ?? throw new ArgumentNullException(nameof(weighting));
        }

        // The default request: score every metric with equal weight.
        public static ScoreRequest Default() => new(ScoreWeightResolver.Default());
    }
}
