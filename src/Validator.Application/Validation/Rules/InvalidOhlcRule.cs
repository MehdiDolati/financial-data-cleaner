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
                var violations = new List<string>();
                if (candle.High < candle.Open) violations.Add("High < Open");
                if (candle.High < candle.Close) violations.Add("High < Close");
                if (candle.High < candle.Low) violations.Add("High < Low");
                if (candle.Low > candle.Open) violations.Add("Low > Open");
                if (candle.Low > candle.Close) violations.Add("Low > Close");
                if (candle.Open <= 0m) violations.Add("Open <= 0");
                if (candle.High <= 0m) violations.Add("High <= 0");
                if (candle.Low <= 0m) violations.Add("Low <= 0");
                if (candle.Close <= 0m) violations.Add("Close <= 0");
                if (candle.Volume < 0m) violations.Add("Volume < 0");

                if (violations.Count > 0)
                {
                    findings.Add(new ValidationFinding(
                        FindingCategory.InvalidOhlc,
                        1,
                        stableSequence: true,
                        $"{string.Join("; ", violations)}; O={candle.Open}; H={candle.High}; L={candle.Low}; C={candle.Close}; V={candle.Volume}")
                    {
                        Timestamp = candle.Timestamp,
                        Line = checked((int)candle.SourceLine),
                        SourceLines = [candle.SourceLine]
                    });
                }
            }

            return findings.ToArray();
        }
    }
}