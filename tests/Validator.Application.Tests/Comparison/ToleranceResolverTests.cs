using Validator.Application.Comparison;
using Validator.Domain.Candles;
using Validator.Domain.Comparison;
using Xunit;

namespace Validator.Application.Tests.Comparison
{
    public class ToleranceResolverTests
    {
        [Fact]
        public void Resolve_NoOverrides_UsesDefaults()
        {
            var config = ToleranceResolver.Resolve(null, "test-benchmark");

            Assert.Equal("test-benchmark", config.BenchmarkName);
            Assert.Equal(5, config.Fields.Count);
            Assert.Equal(TimestampMode.Exact, config.TimestampMode);

            // Price fields get default price tolerances
            var open = config.Fields.First(f => f.Field == OhlcvField.Open);
            Assert.Equal(0.0001m, open.ResolvedAbsolute);
            Assert.Equal(0.0001m, open.ResolvedRelative);
            Assert.Null(open.AbsoluteTolerance);
            Assert.Null(open.RelativeTolerance);
            Assert.True(open.Enabled);

            // Volume gets default volume tolerances
            var volume = config.Fields.First(f => f.Field == OhlcvField.Volume);
            Assert.Equal(0m, volume.ResolvedAbsolute);
            Assert.Equal(0.05m, volume.ResolvedRelative);
        }

        [Fact]
        public void Resolve_EmptyOverrides_UsesDefaults()
        {
            var config = ToleranceResolver.Resolve(Array.Empty<ComparedField>(), "test");

            Assert.Equal(5, config.Fields.Count);
            var high = config.Fields.First(f => f.Field == OhlcvField.High);
            Assert.Equal(0.0001m, high.ResolvedAbsolute);
            Assert.Equal(0.0001m, high.ResolvedRelative);
        }

        [Fact]
        public void Resolve_CustomAbsoluteOverride_Applied()
        {
            var overrides = new[]
            {
                new ComparedField(OhlcvField.Open, true, 0.00005m, null, 0, 0)
            };

            var config = ToleranceResolver.Resolve(overrides, "test");

            var open = config.Fields.First(f => f.Field == OhlcvField.Open);
            Assert.Equal(0.00005m, open.ResolvedAbsolute);
            Assert.Equal(0.0001m, open.ResolvedRelative); // Default relative
            Assert.Equal(0.00005m, open.AbsoluteTolerance);
        }

        [Fact]
        public void Resolve_CustomRelativeOverride_Applied()
        {
            var overrides = new[]
            {
                new ComparedField(OhlcvField.Volume, true, null, 0.02m, 0, 0)
            };

            var config = ToleranceResolver.Resolve(overrides, "test");

            var volume = config.Fields.First(f => f.Field == OhlcvField.Volume);
            Assert.Equal(0m, volume.ResolvedAbsolute); // Default absolute
            Assert.Equal(0.02m, volume.ResolvedRelative);
        }

        [Fact]
        public void Resolve_BothOverrides_Applied()
        {
            var overrides = new[]
            {
                new ComparedField(OhlcvField.Close, true, 0.00020m, 0.0002m, 0, 0)
            };

            var config = ToleranceResolver.Resolve(overrides, "test");

            var close = config.Fields.First(f => f.Field == OhlcvField.Close);
            Assert.Equal(0.00020m, close.ResolvedAbsolute);
            Assert.Equal(0.0002m, close.ResolvedRelative);
        }

        [Fact]
        public void Resolve_FieldDisabled()
        {
            var overrides = new[]
            {
                new ComparedField(OhlcvField.Volume, false, null, null, 0, 0)
            };

            var config = ToleranceResolver.Resolve(overrides, "test");

            var volume = config.Fields.First(f => f.Field == OhlcvField.Volume);
            Assert.False(volume.Enabled);
        }

        [Fact]
        public void Resolve_AllFieldsPresent_InCanonicalOrder()
        {
            var config = ToleranceResolver.Resolve(null, "test");

            Assert.Equal(OhlcvField.Open, config.Fields[0].Field);
            Assert.Equal(OhlcvField.High, config.Fields[1].Field);
            Assert.Equal(OhlcvField.Low, config.Fields[2].Field);
            Assert.Equal(OhlcvField.Close, config.Fields[3].Field);
            Assert.Equal(OhlcvField.Volume, config.Fields[4].Field);
        }

        [Fact]
        public void ParseOverrides_ValidJson_ParsesCorrectly()
        {
            var json = """{"Open": {"absolute": 0.00005}, "Volume": {"relative": 0.02}}""";
            var overrides = ToleranceResolver.ParseOverrides(json);

            Assert.Equal(2, overrides.Count);

            var open = overrides.First(f => f.Field == OhlcvField.Open);
            Assert.Equal(0.00005m, open.AbsoluteTolerance);
            Assert.Null(open.RelativeTolerance);

            var volume = overrides.First(f => f.Field == OhlcvField.Volume);
            Assert.Null(volume.AbsoluteTolerance);
            Assert.Equal(0.02m, volume.RelativeTolerance);
        }

        [Fact]
        public void ParseOverrides_BothAbsoluteAndRelative()
        {
            var json = """{"High": {"absolute": 0.0002, "relative": 0.0005}}""";
            var overrides = ToleranceResolver.ParseOverrides(json);

            Assert.Single(overrides);
            var high = overrides.First();
            Assert.Equal(OhlcvField.High, high.Field);
            Assert.Equal(0.0002m, high.AbsoluteTolerance);
            Assert.Equal(0.0005m, high.RelativeTolerance);
        }

        [Fact]
        public void ParseOverrides_EmptyString_ReturnsEmpty()
        {
            var overrides = ToleranceResolver.ParseOverrides("");
            Assert.Empty(overrides);
        }

        [Fact]
        public void ParseOverrides_Whitespace_ReturnsEmpty()
        {
            var overrides = ToleranceResolver.ParseOverrides("   ");
            Assert.Empty(overrides);
        }

        [Fact]
        public void ParseOverrides_NegativeAbsolute_Throws()
        {
            var json = """{"Open": {"absolute": -0.001}}""";
            Assert.Throws<ArgumentException>(() => ToleranceResolver.ParseOverrides(json));
        }

        [Fact]
        public void ParseOverrides_NegativeRelative_Throws()
        {
            var json = """{"Volume": {"relative": -0.05}}""";
            Assert.Throws<ArgumentException>(() => ToleranceResolver.ParseOverrides(json));
        }

        [Fact]
        public void ParseOverrides_UnknownField_Throws()
        {
            var json = """{"Unknown": {"absolute": 0.001}}""";
            Assert.Throws<ArgumentException>(() => ToleranceResolver.ParseOverrides(json));
        }

        [Fact]
        public void ResolveField_FieldMismatch_Throws()
        {
            var overrideField = new ComparedField(OhlcvField.High, true, 0.001m, null, 0, 0);
            Assert.Throws<ArgumentException>(() => ToleranceResolver.ResolveField(OhlcvField.Open, overrideField));
        }

        [Fact]
        public void ResolveField_ZeroPrice_AbsoluteApplies()
        {
            // Zero price edge case: relative tolerance is unstable, only absolute applies
            var result = ToleranceResolver.ResolveField(OhlcvField.Open, null);
            Assert.Equal(0.0001m, result.ResolvedAbsolute);
            Assert.Equal(0.0001m, result.ResolvedRelative);
        }

        [Fact]
        public void Resolve_InvalidAbsoluteOverride_Throws()
        {
            // The ComparedField constructor should reject negative tolerances
            Assert.Throws<ArgumentOutOfRangeException>(() => new ComparedField(
                OhlcvField.Open, true, -0.001m, null, 0, 0));
        }

        [Fact]
        public void Resolve_BenchmarkName_Preserved()
        {
            var config = ToleranceResolver.Resolve(null, "my-audusd-benchmark");
            Assert.Equal("my-audusd-benchmark", config.BenchmarkName);
        }

        [Fact]
        public void ResolveField_Volume_DefaultsApplied()
        {
            var result = ToleranceResolver.ResolveField(OhlcvField.Volume, null);
            Assert.Equal(0m, result.ResolvedAbsolute);
            Assert.Equal(0.05m, result.ResolvedRelative);
            Assert.True(result.Enabled);
        }

        [Fact]
        public void Resolve_NullOverrides_UsesDefaults()
        {
            var config = ToleranceResolver.Resolve(null, "test");
            Assert.Equal(5, config.Fields.Count);
            var open = config.Fields.First(f => f.Field == OhlcvField.Open);
            Assert.Equal(0.0001m, open.ResolvedAbsolute);
        }

        [Fact]
        public void ParseOverrides_MultipleFields_AllParsed()
        {
            var json = """{"Open": {"absolute": 0.00005}, "High": {"relative": 0.0002}, "Volume": {"absolute": 100}}""";
            var overrides = ToleranceResolver.ParseOverrides(json);
            Assert.Equal(3, overrides.Count);
        }

        // --- T060: Inferred fractional-step tests ---

        [Fact]
        public void Resolve_WithBenchmarkCandles_InfersFractionalStep()
        {
            // 5-digit forex prices (e.g. 0.63421) → fractional step = 0.00001
            var candles = CreateForexCandles(50); // enough for inference (> 10)
            var config = ToleranceResolver.Resolve(null, "test", candles);

            var open = config.Fields.First(f => f.Field == OhlcvField.Open);
            Assert.Equal(0.00001m, open.ResolvedAbsolute);
        }

        [Fact]
        public void Resolve_WithFewCandles_UsesDefault()
        {
            // Fewer than 10 candles → cannot infer, falls back to default
            var candles = CreateForexCandles(5);
            var config = ToleranceResolver.Resolve(null, "test", candles);

            var open = config.Fields.First(f => f.Field == OhlcvField.Open);
            Assert.Equal(0.0001m, open.ResolvedAbsolute); // default
        }

        [Fact]
        public void Resolve_NullCandles_UsesDefault()
        {
            var config = ToleranceResolver.Resolve(null, "test", null);

            var open = config.Fields.First(f => f.Field == OhlcvField.Open);
            Assert.Equal(0.0001m, open.ResolvedAbsolute); // default
        }

        [Fact]
        public void Resolve_UserOverrideOverridesInferred()
        {
            // Even with inference, explicit user override takes precedence
            var candles = CreateForexCandles(50);
            var overrides = new[] { new ComparedField(OhlcvField.Open, true, 0.00050m, null, 0, 0) };
            var config = ToleranceResolver.Resolve(overrides, "test", candles);

            var open = config.Fields.First(f => f.Field == OhlcvField.Open);
            Assert.Equal(0.00050m, open.ResolvedAbsolute); // user override wins
        }

        [Fact]
        public void InferFractionalStep_FiveDigitForex()
        {
            var candles = CreateForexCandles(20);
            var step = ToleranceResolver.InferFractionalStep(candles);
            Assert.Equal(0.00001m, step); // 5-digit → 10^(-5)
        }

        [Fact]
        public void InferFractionalStep_TwoDigitCrypto()
        {
            // Prices like 65000.50 → 2 decimal places → fractional step = 0.01
            var candles = CreateCryptoCandles(20);
            var step = ToleranceResolver.InferFractionalStep(candles);
            Assert.Equal(0.01m, step);
        }

        // --- T061: Field disabling and incomplete override tests ---

        [Fact]
        public void ParseOverrides_DisabledField_Parsed()
        {
            var json = """{"Open": {"enabled": false}}""";
            var overrides = ToleranceResolver.ParseOverrides(json);

            Assert.Single(overrides);
            Assert.False(overrides[0].Enabled);
            Assert.Equal(OhlcvField.Open, overrides[0].Field);
        }

        [Fact]
        public void ParseOverrides_DisabledWithTolerance_Parsed()
        {
            var json = """{"Volume": {"enabled": false, "relative": 0.10}}""";
            var overrides = ToleranceResolver.ParseOverrides(json);

            Assert.Single(overrides);
            Assert.False(overrides[0].Enabled);
            Assert.Equal(0.10m, overrides[0].RelativeTolerance);
        }

        [Fact]
        public void ParseOverrides_EmptyObject_Throws()
        {
            // An entry with no tolerance values or enabled flag is ambiguous
            var json = """{"Open": {}}""";
            Assert.Throws<ArgumentException>(() => ToleranceResolver.ParseOverrides(json));
        }

        [Fact]
        public void ParseOverrides_OnlyAbsoluteEnabled_Parsed()
        {
            // Entry with just absolute + enabled is valid
            var json = """{"Open": {"absolute": 0.001, "enabled": true}}""";
            var overrides = ToleranceResolver.ParseOverrides(json);
            Assert.Single(overrides);
            Assert.True(overrides[0].Enabled);
            Assert.Equal(0.001m, overrides[0].AbsoluteTolerance);
        }

        [Fact]
        public void ResolveField_DisabledField_SkippedInComparison()
        {
            var overrideField = new ComparedField(OhlcvField.Open, false, null, null, 0, 0);
            var result = ToleranceResolver.ResolveField(OhlcvField.Open, overrideField);
            Assert.False(result.Enabled);
        }

        #region Test Helpers

        private static List<PriceCandle> CreateForexCandles(int count)
        {
            var candles = new List<PriceCandle>();
            var baseDate = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
            for (int i = 0; i < count; i++)
            {
                candles.Add(new PriceCandle(
                    baseDate.AddDays(i),
                    0.63421m + i * 0.00010m,
                    0.63580m + i * 0.00010m,
                    0.63310m + i * 0.00010m,
                    0.63502m + i * 0.00010m,
                    125000m + i * 100));
            }
            return candles;
        }

        private static List<PriceCandle> CreateCryptoCandles(int count)
        {
            var candles = new List<PriceCandle>();
            var baseDate = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
            for (int i = 0; i < count; i++)
            {
                candles.Add(new PriceCandle(
                    baseDate.AddDays(i),
                    65000.50m + i * 0.50m,
                    65100.75m + i * 0.50m,
                    64900.25m + i * 0.50m,
                    65050.60m + i * 0.50m,
                    1000m + i * 10));
            }
            return candles;
        }

        #endregion
    }
}
