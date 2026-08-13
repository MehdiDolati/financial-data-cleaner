using System;

namespace Validator.Application.Ingestion
{
    public sealed record CsvInputOptions
    {
        public bool HasHeader { get; init; } = false;
        public string Delimiter { get; init; } = ",";
        public string? DateFormat { get; init; }
        public string? TimeFormat { get; init; }
        public string? TimestampFormat { get; init; }
        public string? TimestampColumn { get; init; }
        public TimeSpan? TzOffset { get; init; }

        public void Validate()
        {
            if (string.IsNullOrEmpty(Delimiter))
                throw new ArgumentException("Delimiter cannot be empty", nameof(Delimiter));

            if (!string.IsNullOrWhiteSpace(TimestampColumn) && !HasHeader)
                throw new ArgumentException("TimestampColumn provided but HasHeader is false");

            var hasDateTimePair = !string.IsNullOrWhiteSpace(DateFormat) || !string.IsNullOrWhiteSpace(TimeFormat);

            if (hasDateTimePair && (!string.IsNullOrWhiteSpace(DateFormat) != !string.IsNullOrWhiteSpace(TimeFormat)))
                throw new ArgumentException("DateFormat and TimeFormat must be provided together.");

            if (!string.IsNullOrWhiteSpace(TimestampFormat) && hasDateTimePair)
                throw new ArgumentException("Specify either TimestampFormat OR DateFormat+TimeFormat, not both.");

            if (TzOffset is not null)
            {
                if (TzOffset.Value.TotalHours < -14 || TzOffset.Value.TotalHours > 14)
                    throw new ArgumentOutOfRangeException(nameof(TzOffset), "TzOffset must be within ±14 hours");
            }
        }
    }
}