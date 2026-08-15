using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Domain.Candles;
using Validator.Domain.Timeframes;

namespace Validator.Application.Validation
{
    public sealed class TimeframeDetector
    {
        public static Timeframe? Detect(IEnumerable<PriceCandle> candles)
        {
            var ordered = candles
                .OrderBy(c => c.Timestamp)
                .Select(c => c.Timestamp)
                .Distinct()
                .ToArray();

            if (ordered.Length < 2)
                return null;

            var deltas = new List<TimeSpan>();
            for (var i = 1; i < ordered.Length; i++)
            {
                var delta = ordered[i] - ordered[i - 1];
                if (delta > TimeSpan.Zero)
                    deltas.Add(delta);
            }

            var groups = deltas
                .GroupBy(d => d)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .ToArray();

            if (groups.Length > 1 && groups[0].Count() == groups[1].Count())
                return null;

            var detectedDelta = groups[0].Key;
            if (detectedDelta.TotalMinutes >= 1 && detectedDelta.TotalMinutes % 1 == 0)
            {
                if (detectedDelta.TotalDays >= 1 && detectedDelta.TotalDays % 1 == 0)
                    return Timeframe.Parse($"D{(int)detectedDelta.TotalDays}");
                if (detectedDelta.TotalHours >= 1 && detectedDelta.TotalHours % 1 == 0)
                    return Timeframe.Parse($"H{(int)detectedDelta.TotalHours}");
                return Timeframe.Parse($"M{(int)detectedDelta.TotalMinutes}");
            }

            return null;
        }
    }
}