using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Domain.Comparison;

namespace Validator.Application.Comparison
{
    /// <summary>
    /// Resolves per-field tolerances from user overrides and defaults.
    /// Default price tolerance: max(one fractional quote-unit step, 0.01% of benchmark value).
    /// Default volume tolerance: 5% of benchmark value.
    /// OR-logic acceptance (FR-017). Rejects invalid config before data read (FR-019).
    /// </summary>
    public static class ToleranceResolver
    {
        // Default tolerances for price fields (Open, High, Low, Close)
        private const decimal DefaultPriceAbsoluteTolerance = 0.0001m;  // 1 pip for 5-digit forex
        private const decimal DefaultPriceRelativeTolerance = 0.0001m;  // 0.01%

        // Default tolerances for volume
        private const decimal DefaultVolumeAbsoluteTolerance = 0m;
        private const decimal DefaultVolumeRelativeTolerance = 0.05m;   // 5%

        /// <summary>
        /// Resolves tolerances for all OHLCV fields, applying user overrides where provided
        /// and defaults otherwise. When benchmarkCandles are provided, infers the fractional-step
        /// tolerance from the observed OHLC precision (FR-015, Q5).
        /// </summary>
        /// <param name="userOverrides">User-supplied field overrides (may be null or empty for defaults).</param>
        /// <param name="benchmarkName">Name of the benchmark being compared against.</param>
        /// <param name="benchmarkCandles">Optional benchmark candles for precision inference.</param>
        /// <returns>A ComparisonConfiguration with fully resolved tolerances.</returns>
        public static ComparisonConfiguration Resolve(
            IReadOnlyList<ComparedField>? userOverrides,
            string benchmarkName,
            IReadOnlyList<Validator.Domain.Candles.PriceCandle>? benchmarkCandles = null)
        {
            var inferredFractionalStep = benchmarkCandles is not null && benchmarkCandles.Count > 0
                ? InferFractionalStep(benchmarkCandles)
                : DefaultPriceAbsoluteTolerance;

            var fields = new List<ComparedField>();

            foreach (OhlcvField field in Enum.GetValues<OhlcvField>())
            {
                var userField = userOverrides?.FirstOrDefault(f => f.Field == field);
                fields.Add(ResolveField(field, userField, inferredFractionalStep));
            }

            return new ComparisonConfiguration(
                benchmarkName,
                fields,
                TimestampMode.Exact);
        }

        /// <summary>
        /// Infers the fractional-step (minimum price increment) from benchmark OHLC observations.
        /// Examines the number of decimal places across all OHLC values and returns 10^(-N)
        /// where N is the maximum observed precision.
        /// </summary>
        /// <param name="candles">The benchmark candles to analyze.</param>
        /// <returns>The inferred fractional step (e.g. 0.00001 for 5-digit precision).</returns>
        public static decimal InferFractionalStep(IReadOnlyList<Validator.Domain.Candles.PriceCandle> candles)
        {
            var maxPrecision = 0;

            foreach (var candle in candles)
            {
                maxPrecision = Math.Max(maxPrecision, GetDecimalPlaces(candle.Open));
                maxPrecision = Math.Max(maxPrecision, GetDecimalPlaces(candle.High));
                maxPrecision = Math.Max(maxPrecision, GetDecimalPlaces(candle.Low));
                maxPrecision = Math.Max(maxPrecision, GetDecimalPlaces(candle.Close));
            }

            // Integral observations represent a unit step of one. No fixed pip
            // fallback is applied when the benchmark contains whole-unit prices.
            if (maxPrecision <= 0)
                return 1m;

            return PowerOfTen(-maxPrecision);
        }

        /// <summary>
        /// Computes 10^n as a decimal value using pure integer arithmetic.
        /// Supports negative exponents for fractional results.
        /// </summary>
        private static decimal PowerOfTen(int exponent)
        {
            if (exponent >= 0)
            {
                var result = 1m;
                for (var i = 0; i < exponent; i++)
                    result *= 10m;
                return result;
            }
            else
            {
                // For negative exponents, divide: 10^(-n) = 1 / 10^n
                var denominator = 1m;
                for (var i = 0; i < -exponent; i++)
                    denominator *= 10m;
                return 1m / denominator;
            }
        }

        private static int GetDecimalPlaces(decimal value)
        {
            var bits = decimal.GetBits(value);
            return (bits[3] >> 16) & 0x7F;
        }

        /// <summary>
        /// Resolves tolerances for a specific field using user overrides and defaults.
        /// </summary>
        public static ComparedField ResolveField(OhlcvField field, ComparedField? userOverride, decimal? inferredFractionalStep = null)
        {
            if (userOverride is not null && userOverride.Field != field)
                throw new ArgumentException($"User override field mismatch: expected {field}, got {userOverride.Field}.");

            var enabled = userOverride?.Enabled ?? true;
            var isPrice = field != OhlcvField.Volume;

            // Get user-specified tolerances (null means "use default")
            var userAbsolute = userOverride?.AbsoluteTolerance;
            var userRelative = userOverride?.RelativeTolerance;

            // Resolve absolute tolerance
            decimal resolvedAbsolute;
            if (userAbsolute.HasValue)
            {
                resolvedAbsolute = userAbsolute.Value;
            }
            else
            {
                // For price fields, use the inferred fractional step if available,
                // falling back to the constant default (FR-015, Q5)
                var defaultPriceAbsolute = inferredFractionalStep ?? DefaultPriceAbsoluteTolerance;
                resolvedAbsolute = isPrice ? defaultPriceAbsolute : DefaultVolumeAbsoluteTolerance;
            }

            // Resolve relative tolerance
            decimal resolvedRelative;
            if (userRelative.HasValue)
            {
                resolvedRelative = userRelative.Value;
            }
            else
            {
                resolvedRelative = isPrice ? DefaultPriceRelativeTolerance : DefaultVolumeRelativeTolerance;
            }

            return new ComparedField(
                field: field,
                enabled: enabled,
                absoluteTolerance: userAbsolute,
                relativeTolerance: userRelative,
                resolvedAbsolute: resolvedAbsolute,
                resolvedRelative: resolvedRelative);
        }

        /// <summary>
        /// Parses a JSON-like tolerance override string into ComparedField instances.
        /// Expected format: {"Open": {"absolute": 0.00005}, "Volume": {"relative": 0.02, "enabled": false}}
        /// An entry with no tolerance values is rejected (FR-019). Unknown fields are rejected.
        /// </summary>
        /// <param name="jsonOverrides">JSON string with per-field tolerance overrides.</param>
        /// <returns>List of ComparedField instances from the overrides.</returns>
        public static IReadOnlyList<ComparedField> ParseOverrides(string jsonOverrides)
        {
            if (string.IsNullOrWhiteSpace(jsonOverrides))
                return Array.Empty<ComparedField>();

            var result = new List<ComparedField>();
            System.Text.Json.JsonDocument doc;
            try
            {
                doc = System.Text.Json.JsonDocument.Parse(jsonOverrides);
            }
            catch (System.Text.Json.JsonException exception)
            {
                throw new ArgumentException($"Tolerance configuration is not valid JSON: {exception.Message}", nameof(jsonOverrides), exception);
            }
            using (doc)
            {
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw new ArgumentException("Tolerance configuration must be a JSON object.", nameof(jsonOverrides));
            if (!doc.RootElement.EnumerateObject().Any())
                throw new ArgumentException("Tolerance configuration must contain at least one field override.", nameof(jsonOverrides));

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var fieldName = property.Name;
                var field = ParseOhlcvField(fieldName);

                if (property.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
                    throw new ArgumentException($"Override for {fieldName} must be a JSON object.", nameof(jsonOverrides));

                // Validate: each entry must have at least one tolerance or an enabled flag
                var hasAbsolute = property.Value.TryGetProperty("absolute", out var absElement);
                var hasRelative = property.Value.TryGetProperty("relative", out var relElement);
                var hasEnabled = property.Value.TryGetProperty("enabled", out var enabledElement);

                foreach (var member in property.Value.EnumerateObject())
                {
                    if (member.Name is not ("absolute" or "relative" or "enabled"))
                        throw new ArgumentException($"Unknown tolerance property '{member.Name}' for {fieldName}.", nameof(jsonOverrides));
                }

                if (!hasAbsolute && !hasRelative && !hasEnabled)
                    throw new ArgumentException(
                        $"Field '{fieldName}' override must specify at least one of 'absolute', 'relative', or 'enabled'. " +
                        $"An override with no values is ambiguous (FR-019).", nameof(jsonOverrides));

                decimal? absolute = null;
                decimal? relative = null;
                var enabled = true;

                if (hasAbsolute)
                {
                    if (absElement.ValueKind != System.Text.Json.JsonValueKind.Number || !absElement.TryGetDecimal(out var parsedAbsolute))
                        throw new ArgumentException($"'absolute' for {fieldName} must be a decimal number.", nameof(jsonOverrides));
                    absolute = parsedAbsolute;
                }

                if (hasRelative)
                {
                    if (relElement.ValueKind != System.Text.Json.JsonValueKind.Number || !relElement.TryGetDecimal(out var parsedRelative))
                        throw new ArgumentException($"'relative' for {fieldName} must be a decimal number.", nameof(jsonOverrides));
                    relative = parsedRelative;
                }

                if (hasEnabled)
                {
                    if (enabledElement.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
                        throw new ArgumentException($"'enabled' for {fieldName} must be a boolean.", nameof(jsonOverrides));
                    enabled = enabledElement.GetBoolean();
                }

                if (enabled && !hasAbsolute && !hasRelative)
                    throw new ArgumentException($"Enabled override for {fieldName} must specify an absolute or relative tolerance.", nameof(jsonOverrides));
                if (!enabled && (hasAbsolute || hasRelative))
                    throw new ArgumentException($"Disabled field {fieldName} cannot also specify tolerances.", nameof(jsonOverrides));

                // Validate non-negative tolerances (FR-019)
                if (absolute is < 0)
                    throw new ArgumentException($"Tolerance for {fieldName} must be non-negative.");
                if (relative is < 0)
                    throw new ArgumentException($"Tolerance for {fieldName} must be non-negative.");

                result.Add(new ComparedField(
                    field: field,
                    enabled: enabled,
                    absoluteTolerance: absolute,
                    relativeTolerance: relative,
                    resolvedAbsolute: 0, // Will be resolved by ResolveField
                    resolvedRelative: 0));
            }

            // Validate no duplicate fields (FR-019)
            var duplicateFields = result
                .GroupBy(f => f.Field)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicateFields.Count > 0)
                throw new ArgumentException(
                    $"Duplicate field overrides detected: {string.Join(", ", duplicateFields)}. " +
                    $"Each field may appear at most once (FR-019).", nameof(jsonOverrides));

            return result;
            }
        }

        private static OhlcvField ParseOhlcvField(string name) => name.Trim().ToLowerInvariant() switch
        {
            "open" => OhlcvField.Open,
            "high" => OhlcvField.High,
            "low" => OhlcvField.Low,
            "close" => OhlcvField.Close,
            "volume" => OhlcvField.Volume,
            _ => throw new ArgumentException($"Unknown OHLCV field '{name}'. Use Open, High, Low, Close, or Volume.")
        };
    }
}
