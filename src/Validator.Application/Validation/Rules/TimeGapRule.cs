using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Domain.Candles;
using Validator.Domain.Findings;

namespace Validator.Application.Validation.Rules
{
    public sealed class TimeGapRule
    {
        public ValidationFinding[] Evaluate(IEnumerable<PriceCandle> candles, TimeSpan interval)
        {
            var ordered = candles
                .OrderBy(c => c.Timestamp)
                .Select(c => c.Timestamp)
                .ToArray();

            if (ordered.Length < 2)
                return Array.Empty<ValidationFinding>();

            var gapRuns = 0;
            for (var i = 1; i < ordered.Length; i++)
            {
                var delta = ordered[i] - ordered[i - 1];
                if (delta > interval)
                {
                    gapRuns++;
                }
            }

            if (gapRuns == 0)
                return Array.Empty<ValidationFinding>();

            return new[]
            {
                new ValidationFinding(
                    FindingCategory.TimeGap,
                    gapRuns,
                    stableSequence: false,
                    $"Detected {gapRuns} time gap run(s)")
            };
        }
    }
}
