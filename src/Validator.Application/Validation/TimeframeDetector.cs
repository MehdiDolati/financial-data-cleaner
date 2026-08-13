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

            if (deltas.Count == 0)
                return null;

            var modal = deltas
                .GroupBy(d => d)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .First();

            var detectedDelta = modal.Key;
            if (detectedDelta.TotalMinutes >= 1 && detectedDelta.TotalMinutes % 1 == 0)
            {
                if (detectedDelta.TotalMinutes == 1) return Timeframe.Parse("M1");
                if (detectedDelta.TotalMinutes == 60) return Timeframe.Parse("H1");
                if (detectedDelta.TotalDays == 1) return Timeframe.Parse("D1");
            }

            return null;
        }
    }
}