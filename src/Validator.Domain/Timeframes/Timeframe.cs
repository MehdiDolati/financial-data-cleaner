using System;

namespace Validator.Domain.Timeframes
{
    public sealed record Timeframe
    {
        public char Unit { get; }
        public int Value { get; }

        private Timeframe(char unit, int value)
        {
            Unit = unit;
            Value = value;
        }

        public static Timeframe Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                throw new ArgumentException("Timeframe string is null or empty", nameof(s));

            s = s.Trim().ToUpperInvariant();
            // Expect format: M<n>, H<n>, D<n>
            var unit = s[0];
            var number = s.Substring(1);

            if (unit != 'M' && unit != 'H' && unit != 'D')
                throw new FormatException("Timeframe unit must be M, H or D.");

            if (!int.TryParse(number, out var val))
                throw new FormatException("Timeframe value must be an integer.");

            if (val <= 0)
                throw new ArgumentOutOfRangeException(nameof(s), "Timeframe value must be positive and non-zero.");

            return new Timeframe(unit, val);
        }

        public static bool TryParse(string s, out Timeframe? tf)
        {
            try
            {
                tf = Parse(s);
                return true;
            }
            catch
            {
                tf = null;
                return false;
            }
        }

        public override string ToString() => $"{Unit}{Value}";
    }
}