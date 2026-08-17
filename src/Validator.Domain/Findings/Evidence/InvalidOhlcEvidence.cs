using System;
using System.Collections.Generic;
using System.Linq;

namespace Validator.Domain.Findings.Evidence
{
    // Stable OHLC/volume rule codes used by the invalid-OHLC evidence.
    public enum OhlcViolationCode
    {
        HIGH_BELOW_OPEN = 0,
        HIGH_BELOW_CLOSE = 1,
        HIGH_BELOW_LOW = 2,
        LOW_ABOVE_OPEN = 3,
        LOW_ABOVE_CLOSE = 4,
        LOW_ABOVE_HIGH = 5,
        NON_POSITIVE_OPEN = 6,
        NON_POSITIVE_HIGH = 7,
        NON_POSITIVE_LOW = 8,
        NON_POSITIVE_CLOSE = 9,
        NEGATIVE_VOLUME = 10
    }

    // Observed OHLCV values of one invalid row.
    public sealed record OhlcValues
    {
        public decimal Open { get; }
        public decimal High { get; }
        public decimal Low { get; }
        public decimal Close { get; }
        public decimal Volume { get; }

        public OhlcValues(decimal open, decimal high, decimal low, decimal close, decimal volume)
        {
            Open = open;
            High = high;
            Low = low;
            Close = close;
            Volume = volume;
        }
    }

    // Evidence for one invalid-OHLC finding. Every violated stable rule code is
    // listed; the row still contributes exactly one to the category count.
    public sealed record InvalidOhlcEvidence
    {
        public OhlcValues Observed { get; }
        public IReadOnlyList<OhlcViolationCode> Violations { get; }

        public InvalidOhlcEvidence(OhlcValues observed, IReadOnlyList<OhlcViolationCode>? violations = null)
        {
            if (observed is null)
            {
                throw new ArgumentNullException(nameof(observed));
            }

            var codes = violations ?? Array.Empty<OhlcViolationCode>();
            if (codes.Count == 0)
            {
                throw new ArgumentException("At least one violated rule code is required.", nameof(violations));
            }

            if (codes.Distinct().Count() != codes.Count)
            {
                throw new ArgumentException("Violation codes must not repeat.", nameof(violations));
            }

            Observed = observed;
            Violations = codes;
        }
    }
}