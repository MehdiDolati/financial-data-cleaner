using System.Collections.Generic;
using System.Linq;
using Validator.Domain.Candles;
using Validator.Domain.Findings;

namespace Validator.Application.Validation.Rules
{
    public sealed class DuplicateRecordRule
    {
        public ValidationFinding[] Evaluate(IEnumerable<PriceCandle> candles)
        {
            var findings = new List<ValidationFinding>();

            foreach (var group in candles.GroupBy(c => c.Timestamp).OrderBy(group => group.Key))
            {
                var duplicateCount = group.Count() - 1;
                if (duplicateCount > 0)
                {
                    var rows = group.OrderBy(candle => candle.SourceLine).ToArray();
                    var exact = rows.Skip(1).All(candle =>
                        candle.Open == rows[0].Open &&
                        candle.High == rows[0].High &&
                        candle.Low == rows[0].Low &&
                        candle.Close == rows[0].Close &&
                        candle.Volume == rows[0].Volume);
                    findings.Add(new ValidationFinding(
                        FindingCategory.DuplicateRecord,
                        duplicateCount,
                        stableSequence: true,
                        $"{(exact ? "Exact" : "Conflicting")} duplicate at {group.Key:O}; lines={string.Join(',', rows.Select(candle => candle.SourceLine))}")
                    {
                        Timestamp = group.Key,
                        Line = checked((int)rows[0].SourceLine),
                        SourceLines = rows.Select(candle => candle.SourceLine).ToArray()
                    });
                }
            }

            return findings.ToArray();
        }
    }
}