using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Domain.Candles;
using Validator.Domain.Findings;

namespace Validator.Application.Validation.Rules
{
    public sealed class MissingCandleRule
    {
        public ValidationFinding[] Evaluate(IEnumerable<PriceCandle> candles, TimeSpan interval)
        {
            var ordered = candles
                .OrderBy(c => c.Timestamp)
                .Select(c => c.Timestamp)
                .ToList();

            if (ordered.Count <= 1)
                return Array.Empty<ValidationFinding>();

            var missing = new List<DateTimeOffset>();
            for (var i = 1; i < ordered.Count; i++)
            {
                var previous = ordered[i - 1];
                var current = ordered[i];
                var expected = previous + interval;
                while (expected < current)
                {
                    missing.Add(expected);
                    expected += interval;
                }
            }

            if (missing.Count == 0)
                return Array.Empty<ValidationFinding>();

            return missing.Select(timestamp => new ValidationFinding(
                FindingCategory.MissingCandle,
                1,
                stableSequence: false,
                $"Missing expected candle at {timestamp:O}")
            {
                Timestamp = timestamp
            }).ToArray();
        }
    }
}