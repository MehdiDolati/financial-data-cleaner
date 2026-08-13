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

            foreach (var group in candles.GroupBy(c => new { c.Timestamp, c.Open, c.High, c.Low, c.Close, c.Volume }))
            {
                var duplicateCount = group.Count() - 1;
                if (duplicateCount > 0)
                {
                    findings.Add(new ValidationFinding(
                        FindingCategory.Major,
                        duplicateCount,
                        stableSequence: true,
                        $"Duplicate candle at {group.Key.Timestamp:O} ({duplicateCount} duplicates)"));
                }
            }

            return findings.ToArray();
        }
    }
}