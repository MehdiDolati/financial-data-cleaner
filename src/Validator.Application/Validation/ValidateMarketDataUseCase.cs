using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;
using Validator.Domain.Candles;
using Validator.Domain.Findings;
using Validator.Domain.Timeframes;
using Validator.Application.Validation.Rules;

namespace Validator.Application.Validation
{
    public sealed class ValidateMarketDataUseCase : IValidateMarketDataUseCase
    {
        private readonly ICandleSource _source;
        private readonly IReportWriter _reportWriter;

        public ValidateMarketDataUseCase(ICandleSource source, IReportWriter reportWriter)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _reportWriter = reportWriter ?? throw new ArgumentNullException(nameof(reportWriter));
        }

        public async Task<int> ExecuteAsync(object request)
        {
            var validationRequest = request as ValidationRequest ?? throw new ArgumentException("Request must be ValidationRequest", nameof(request));

            var candles = new List<PriceCandle>();
            await foreach (var candle in _source.ReadAllAsync())
            {
                candles.Add(candle);
            }

            var ordered = candles
                .OrderBy(candle => candle.Timestamp)
                .ThenBy(candle => candle.SourceLine)
                .ToArray();
            var calendar = validationRequest.MarketCalendar ?? new DefaultForexCalendar();
            var timeframe = ResolveTimeframe(validationRequest.Timeframe, ordered, calendar);
            var malformedRows = (_source as IMalformedRowSource)?.MalformedRows ?? [];

            var findings = new List<ValidationFinding>();
            findings.AddRange(new DuplicateRecordRule().Evaluate(ordered));
            findings.AddRange(new InvalidOhlcRule().Evaluate(ordered));
            findings.AddRange(new ClosedMarketRecordRule(calendar).Evaluate(ordered));
            findings.AddRange(CreateSequenceFindings(ordered, malformedRows, timeframe, calendar));
            findings.AddRange(malformedRows.Select(row => new ValidationFinding(
                FindingCategory.MalformedRow,
                1,
                stableSequence: true,
                row.Reason)
            {
                Timestamp = row.ParsedTimestampUtc,
                Line = checked((int)row.LineNumber),
                SourceLines = [row.LineNumber]
            }));

            var canonical = findings
                .OrderBy(finding => finding.Category)
                .ThenBy(finding => finding.Timestamp ?? DateTimeOffset.MaxValue)
                .ThenBy(finding => finding.Line ?? int.MaxValue)
                .ThenBy(finding => finding.Message, StringComparer.Ordinal)
                .ToList();
            var summary = new ValidationSummary(
                validRows: ordered.Length,
                missingCandles: Sum(FindingCategory.MissingCandle),
                duplicateRecords: Sum(FindingCategory.DuplicateRecord),
                invalidOhlc: Sum(FindingCategory.InvalidOhlc),
                closedMarketRecords: Sum(FindingCategory.ClosedMarketRecord),
                timeGaps: Sum(FindingCategory.TimeGap),
                malformedRows: Sum(FindingCategory.MalformedRow));
            var range = ordered.Length == 0
                ? null
                : new DateRange(ordered[0].Timestamp, ordered[^1].Timestamp);
            var report = new ValidationReport(summary, range, validationRequest.InputPath)
            {
                DetectedTimeframe = timeframe.ToString(),
                TotalRecords = ordered.Length,
                Findings = canonical
            };
            await _reportWriter.WriteReportAsync(report);
            return report.IsClean ? 0 : 1;

            int Sum(FindingCategory category) => canonical
                .Where(finding => finding.Category == category)
                .Sum(finding => finding.CountContribution);
        }

        private static Timeframe ResolveTimeframe(
            string? overrideCode,
            IReadOnlyList<PriceCandle> candles,
            IMarketCalendar calendar)
        {
            if (!string.IsNullOrWhiteSpace(overrideCode))
            {
                return Timeframe.Parse(overrideCode);
            }

            var openCandles = candles.Where(candle => calendar.IsOpen(candle.Timestamp));
            return TimeframeDetector.Detect(openCandles) ??
                throw new InvalidOperationException(
                    "Unable to infer a unique timeframe. Supply --timeframe M<n>, H<n>, or D<n>.");
        }

        private static IEnumerable<ValidationFinding> CreateSequenceFindings(
            IReadOnlyList<PriceCandle> candles,
            IReadOnlyList<MalformedRow> malformedRows,
            Timeframe timeframe,
            IMarketCalendar calendar)
        {
            var openTimestamps = candles
                .Where(candle => calendar.IsOpen(candle.Timestamp))
                .Select(candle => candle.Timestamp)
                .Distinct()
                .OrderBy(timestamp => timestamp)
                .ToArray();
            if (openTimestamps.Length < 2)
            {
                return [];
            }

            var occupied = candles.Select(candle => candle.Timestamp)
                .Concat(malformedRows
                    .Where(row => row.ParsedTimestampUtc.HasValue)
                    .Select(row => row.ParsedTimestampUtc!.Value))
                .ToHashSet();
            var findings = new List<ValidationFinding>();
            var inGap = false;
            for (var expected = openTimestamps[0]; expected <= openTimestamps[^1]; expected += timeframe.Duration)
            {
                if (!calendar.IsOpen(expected))
                {
                    inGap = false;
                    continue;
                }

                if (occupied.Contains(expected))
                {
                    inGap = false;
                    continue;
                }

                findings.Add(new ValidationFinding(
                    FindingCategory.MissingCandle,
                    1,
                    stableSequence: false,
                    $"Missing expected candle at {expected:O}")
                {
                    Timestamp = expected
                });

                if (!inGap)
                {
                    findings.Add(new ValidationFinding(
                        FindingCategory.TimeGap,
                        1,
                        stableSequence: false,
                        $"Time gap begins at {expected:O}")
                    {
                        Timestamp = expected
                    });
                    inGap = true;
                }
            }

            return findings;
        }

        private sealed class DefaultForexCalendar : IMarketCalendar
        {
            public Validator.Domain.Calendars.MarketProfile Profile =>
                Validator.Domain.Calendars.MarketProfile.Forex;

            public bool IsOpen(DateTimeOffset timestamp) =>
                !ClosedMarketRecordRule.IsClosedMarket(timestamp);
        }
    }
}