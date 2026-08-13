using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Domain.Candles;
using Validator.Domain.Findings;

namespace Validator.Application.Validation.Rules
{
    public sealed class ClosedMarketRecordRule
    {
        public ValidationFinding[] Evaluate(IEnumerable<PriceCandle> candles)
        {
            var findings = new List<ValidationFinding>();

            foreach (var candle in candles.Where(c => IsClosedMarket(c.Timestamp)))
            {
                findings.Add(new ValidationFinding(
                    FindingCategory.Major,
                    1,
                    stableSequence: true,
                    $"Candle fell outside allowed forex session at {candle.Timestamp:O}"));
            }

            return findings.ToArray();
        }

        public static bool IsClosedMarket(DateTimeOffset timestamp)
        {
            var utc = timestamp.ToUniversalTime();
            var day = utc.DayOfWeek;
            var time = utc.TimeOfDay;

            // Forex session is [Sunday 22:00, Friday 22:00) UTC
            if (day == DayOfWeek.Saturday) return true;
            if (day == DayOfWeek.Sunday) return time < TimeSpan.FromHours(22);
            if (day == DayOfWeek.Friday) return time >= TimeSpan.FromHours(22);

            return false;
        }
    }
}