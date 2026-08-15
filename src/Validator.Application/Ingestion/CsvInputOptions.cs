using System;

namespace Validator.Application.Ingestion
{
    public sealed record CsvInputOptions
    {
        public bool HasHeader { get; init; } = false;
        public string? Delimiter { get; init; }
        public string? DateFormat { get; init; }
        public string? TimeFormat { get; init; }
        public string? TimestampFormat { get; init; }
        public string? TimestampColumn { get; init; }
        public TimeSpan TzOffset { get; init; } = TimeSpan.FromHours(2);

        public void Validate()
        {
            if (Delimiter is { Length: 0 })
                throw new ArgumentException("Delimiter cannot be empty", nameof(Delimiter));

            var hasTimestampFormat = !string.IsNullOrWhiteSpace(TimestampFormat);
            var hasTimestampColumn = !string.IsNullOrWhiteSpace(TimestampColumn);
            if (hasTimestampFormat != hasTimestampColumn)
                throw new ArgumentException("TimestampFormat and TimestampColumn must be provided together.");

            if (hasTimestampColumn && !HasHeader &&
                (!int.TryParse(TimestampColumn, out var columnIndex) || columnIndex <= 0))
                throw new ArgumentException("A headerless TimestampColumn must be a positive one-based index.");

            var hasDateTimePair = !string.IsNullOrWhiteSpace(DateFormat) || !string.IsNullOrWhiteSpace(TimeFormat);

            if (hasDateTimePair && (!string.IsNullOrWhiteSpace(DateFormat) != !string.IsNullOrWhiteSpace(TimeFormat)))
                throw new ArgumentException("DateFormat and TimeFormat must be provided together.");

            if (!string.IsNullOrWhiteSpace(TimestampFormat) && hasDateTimePair)
                throw new ArgumentException("Specify either TimestampFormat OR DateFormat+TimeFormat, not both.");

            if (TzOffset.TotalHours < -14 || TzOffset.TotalHours > 14)
                throw new ArgumentOutOfRangeException(nameof(TzOffset), "TzOffset must be within +/-14 hours");
        }
    }
}