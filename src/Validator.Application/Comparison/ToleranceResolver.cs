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
        /// and defaults otherwise.
        /// </summary>
        /// <param name="userOverrides">User-supplied field overrides (may be null or empty for defaults).</param>
        /// <param name="benchmarkName">Name of the benchmark being compared against.</param>
        /// <returns>A ComparisonConfiguration with fully resolved tolerances.</returns>
        public static ComparisonConfiguration Resolve(
            IReadOnlyList<ComparedField>? userOverrides,
            string benchmarkName)
        {
            var fields = new List<ComparedField>();

            foreach (OhlcvField field in Enum.GetValues<OhlcvField>())
            {
                var userField = userOverrides?.FirstOrDefault(f => f.Field == field);
                fields.Add(ResolveField(field, userField));
            }

            return new ComparisonConfiguration(
                benchmarkName,
                fields,
                TimestampMode.Exact);
        }

        /// <summary>
        /// Resolves tolerances for a specific field using user overrides and defaults.
        /// </summary>
        public static ComparedField ResolveField(OhlcvField field, ComparedField? userOverride)
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
                resolvedAbsolute = isPrice ? DefaultPriceAbsoluteTolerance : DefaultVolumeAbsoluteTolerance;
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
        /// Expected format: {"Open": {"absolute": 0.00005}, "Volume": {"relative": 0.02}}
        /// </summary>
        /// <param name="jsonOverrides">JSON string with per-field tolerance overrides.</param>
        /// <returns>List of ComparedField instances from the overrides.</returns>
        public static IReadOnlyList<ComparedField> ParseOverrides(string jsonOverrides)
        {
            if (string.IsNullOrWhiteSpace(jsonOverrides))
                return Array.Empty<ComparedField>();

            var result = new List<ComparedField>();
            using var doc = System.Text.Json.JsonDocument.Parse(jsonOverrides);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var fieldName = property.Name;
                var field = ParseOhlcvField(fieldName);

                decimal? absolute = null;
                decimal? relative = null;

                if (property.Value.TryGetProperty("absolute", out var absElement))
                    absolute = absElement.GetDecimal();

                if (property.Value.TryGetProperty("relative", out var relElement))
                    relative = relElement.GetDecimal();

                // Validate non-negative tolerances (FR-019)
                if (absolute is < 0)
                    throw new ArgumentException($"Tolerance for {fieldName} must be non-negative.");
                if (relative is < 0)
                    throw new ArgumentException($"Tolerance for {fieldName} must be non-negative.");

                result.Add(new ComparedField(
                    field: field,
                    enabled: true,
                    absoluteTolerance: absolute,
                    relativeTolerance: relative,
                    resolvedAbsolute: 0, // Will be resolved by ResolveField
                    resolvedRelative: 0));
            }

            return result;
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
