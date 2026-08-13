using System.Collections.Generic;
using Validator.Domain.Candles;
using Validator.Domain.Findings;

namespace Validator.Application.Validation.Rules
{
    public sealed class InvalidOhlcRule
    {
        public ValidationFinding[] Evaluate(IEnumerable<PriceCandle> candles)
        {
            var findings = new List<ValidationFinding>();

            foreach (var candle in candles)
            {
                var invalid = candle.High < candle.Low
                    || candle.High == candle.Low
                    || candle.Open <= 0m
                    || candle.High <= 0m
                    || candle.Low <= 0m
                    || candle.Close <= 0m
                    || candle.Volume < 0m;

                if (invalid)
                {
                    findings.Add(new ValidationFinding(
                        FindingCategory.Critical,
                        1,
                        stableSequence: true,
                        $"Invalid OHLCV values at {candle.Timestamp:O}"));
                }
            }

            return findings.ToArray();
        }
    }
}