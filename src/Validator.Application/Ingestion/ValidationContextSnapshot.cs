using System;
using System.Globalization;
using Validator.Application.Reporting;

namespace Validator.Application.Ingestion
{
    // Snapshot of every resolved input fact that materially affects results.
    // No field depends on host locale, absolute path, current time, or
    // environment.
    public sealed record ValidationContextSnapshot
    {
        private static readonly string[] KnownDelimiters = ["comma", "semicolon", "tab"];

        public string Timeframe { get; }
        public CalendarContext Calendar { get; }
        public TimestampInterpretation Timestamp { get; }
        public string Delimiter { get; }
        public bool HasHeader { get; }
        public DateRange? DateRange { get; }

        public ValidationContextSnapshot(
            string timeframe,
            CalendarContext calendar,
            TimestampInterpretation timestamp,
            string delimiter,
            bool hasHeader,
            DateRange? dateRange)
        {
            if (!IsTimeframeCode(timeframe))
            {
                throw new ArgumentException("Timeframe must be a canonical M<n>, H<n>, or D<n> code.", nameof(timeframe));
            }

            if (calendar is null)
            {
                throw new ArgumentNullException(nameof(calendar));
            }

            if (timestamp is null)
            {
                throw new ArgumentNullException(nameof(timestamp));
            }

            if (!KnownDelimiters.Contains(delimiter))
            {
                throw new ArgumentException("Delimiter must be comma, semicolon, or tab.", nameof(delimiter));
            }

            Timeframe = timeframe;
            Calendar = calendar;
            Timestamp = timestamp;
            Delimiter = delimiter;
            HasHeader = hasHeader;
            DateRange = dateRange;
        }

        internal static bool IsTimeframeCode(string timeframe) =>
            timeframe is { Length: >= 2 } &&
            timeframe[0] is 'M' or 'H' or 'D' &&
            int.TryParse(timeframe.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
            value >= 1;
    }
}