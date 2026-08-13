using System;

namespace Validator.Infrastructure.Csv
{
    public static class SourceOffsetConverter
    {
        public static DateTimeOffset NormalizeToUtc(DateTime value, TimeSpan offset)
        {
            if (offset.TotalHours < -14 || offset.TotalHours > 14)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be within ±14 hours.");
            }

            return new DateTimeOffset(value, offset).ToUniversalTime();
        }
    }
}
