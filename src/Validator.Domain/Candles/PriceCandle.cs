using System;

namespace Validator.Domain.Candles
{
    // Immutable record representing a single OHLCV candle with a UTC timestamp
    public sealed record PriceCandle
    {
        public DateTimeOffset Timestamp { get; init; }
        public decimal Open { get; init; }
        public decimal High { get; init; }
        public decimal Low { get; init; }
        public decimal Close { get; init; }
        public decimal Volume { get; init; }
        public long SourceLine { get; init; }

        public PriceCandle(DateTimeOffset timestamp, decimal open, decimal high, decimal low, decimal close, decimal volume)
            : this(timestamp, open, high, low, close, volume, 1)
        {
        }

        public PriceCandle(
            DateTimeOffset timestamp,
            decimal open,
            decimal high,
            decimal low,
            decimal close,
            decimal volume,
            long sourceLine)
        {
            if (timestamp.Offset != TimeSpan.Zero)
                throw new ArgumentException("Timestamp must be in UTC (zero offset).", nameof(timestamp));

            if (sourceLine <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceLine), "Source line must be positive.");

            Timestamp = timestamp;
            Open = open;
            High = high;
            Low = low;
            Close = close;
            Volume = volume;
            SourceLine = sourceLine;
        }
    }
}