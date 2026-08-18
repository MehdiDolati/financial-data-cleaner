using System;

namespace Validator.Domain.Findings.Evidence
{
    // One participating row of a duplicate group with its observed OHLCV
    // values and original timestamp text when recovered from the source.
    public sealed record DuplicateRowEvidence
    {
        public long SourceLine { get; }
        public string? OriginalTimestampText { get; }
        public decimal Open { get; }
        public decimal High { get; }
        public decimal Low { get; }
        public decimal Close { get; }
        public decimal Volume { get; }

        public DuplicateRowEvidence(
            long sourceLine,
            string? originalTimestampText,
            decimal open,
            decimal high,
            decimal low,
            decimal close,
            decimal volume)
        {
            if (sourceLine <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceLine), "Source line must be positive.");
            }

            SourceLine = sourceLine;
            OriginalTimestampText = originalTimestampText;
            Open = open;
            High = high;
            Low = low;
            Close = close;
            Volume = volume;
        }
    }
}