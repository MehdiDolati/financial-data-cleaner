using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Abstractions;
using Validator.Domain.Candles;
using Validator.Domain.Findings;

namespace Validator.Application.Validation.Rules
{
    public sealed class ClosedMarketRecordRule
    {
        private readonly IMarketCalendar? _calendar;

        public ClosedMarketRecordRule(IMarketCalendar? calendar = null)
        {
            _calendar = calendar;
        }

        public ValidationFinding[] Evaluate(IEnumerable<PriceCandle> candles)
        {
            var findings = new List<ValidationFinding>();

            foreach (var candle in candles)
            {
                var isClosed = _calendar != null ? !_calendar.IsOpen(candle.Timestamp) : IsClosedMarket(candle.Timestamp);
                if (isClosed)
                {
                    findings.Add(new ValidationFinding(
                        FindingCategory.ClosedMarketRecord,
                        1,
                        stableSequence: true,
                        $"Candle fell outside the active market session at {candle.Timestamp:O}")
                    {
                        Timestamp = candle.Timestamp,
                        Line = checked((int)candle.SourceLine),
                        SourceLines = [candle.SourceLine]
                    });
                }
            }

            return findings.ToArray();
        }

        public static bool IsClosedMarket(DateTimeOffset timestamp)
        {
            var utc = timestamp.ToUniversalTime();
            var day = utc.DayOfWeek;
            var time = utc.TimeOfDay;

            // Default forex session is [Sunday 22:00, Friday 22:00) UTC
            if (day == DayOfWeek.Saturday) return true;
            if (day == DayOfWeek.Sunday) return time < TimeSpan.FromHours(22);
            if (day == DayOfWeek.Friday) return time >= TimeSpan.FromHours(22);

            return false;
        }
    }
}
