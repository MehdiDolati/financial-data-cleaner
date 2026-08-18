using System;
using System.Globalization;

namespace Validator.Application.Ingestion
{
    /// <summary>
    /// How a source expresses the instant of a record.
    /// </summary>
    public enum TimestampMode
    {
        /// <summary>The date and the time of day are in separate columns.</summary>
        SeparateDateTime = 0,

        /// <summary>A single column holds the whole timestamp.</summary>
        CombinedTimestamp = 1
    }

    // Resolved timestamp interpretation. Exactly the fields relevant to the
    // active mode are populated; the source offset is canonical +HH:mm/-HH:mm.
    public sealed record TimestampInterpretation
    {
        public TimestampMode Mode { get; }
        public string? DateFormat { get; }
        public string? TimeFormat { get; }
        public string? TimestampFormat { get; }
        public string? TimestampColumn { get; }
        public string SourceOffset { get; }

        private TimestampInterpretation(
            TimestampMode mode,
            string? dateFormat,
            string? timeFormat,
            string? timestampFormat,
            string? timestampColumn,
            string sourceOffset)
        {
            Mode = mode;
            DateFormat = dateFormat;
            TimeFormat = timeFormat;
            TimestampFormat = timestampFormat;
            TimestampColumn = timestampColumn;
            SourceOffset = sourceOffset;
        }

        public static TimestampInterpretation CreateSeparate(
            string dateFormat,
            string timeFormat,
            string sourceOffset)
        {
            if (string.IsNullOrWhiteSpace(dateFormat))
            {
                throw new ArgumentException("Date format must be a non-empty value.", nameof(dateFormat));
            }

            if (string.IsNullOrWhiteSpace(timeFormat))
            {
                throw new ArgumentException("Time format must be a non-empty value.", nameof(timeFormat));
            }

            RequireCanonicalOffset(sourceOffset);

            return new TimestampInterpretation(TimestampMode.SeparateDateTime, dateFormat, timeFormat, null, null, sourceOffset);
        }

        public static TimestampInterpretation CreateCombined(
            string timestampFormat,
            string timestampColumn,
            string sourceOffset)
        {
            if (string.IsNullOrWhiteSpace(timestampFormat))
            {
                throw new ArgumentException("Timestamp format must be a non-empty value.", nameof(timestampFormat));
            }

            if (string.IsNullOrWhiteSpace(timestampColumn))
            {
                throw new ArgumentException("Timestamp column must be a non-empty value.", nameof(timestampColumn));
            }

            RequireCanonicalOffset(sourceOffset);

            return new TimestampInterpretation(TimestampMode.CombinedTimestamp, null, null, timestampFormat, timestampColumn, sourceOffset);
        }

        private static void RequireCanonicalOffset(string sourceOffset)
        {
            if (sourceOffset is null || sourceOffset.Length != 6)
            {
                throw new ArgumentException(
                    "Source offset must be a canonical fixed +HH:mm or -HH:mm value within +/-14:00.",
                    nameof(sourceOffset));
            }

            var sign = sourceOffset[0];
            if (sign is not ('+' or '-') || sourceOffset[3] != ':')
            {
                throw new ArgumentException(
                    "Source offset must be a canonical fixed +HH:mm or -HH:mm value within +/-14:00.",
                    nameof(sourceOffset));
            }

            if (!int.TryParse(sourceOffset.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
                !int.TryParse(sourceOffset.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
                hours > 14 ||
                minutes > 59 ||
                (hours == 14 && minutes != 0))
            {
                throw new ArgumentException(
                    "Source offset must be a canonical fixed +HH:mm or -HH:mm value within +/-14:00.",
                    nameof(sourceOffset));
            }
        }
    }
}