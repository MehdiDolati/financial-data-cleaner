using Validator.Application.Comparison;
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
    }
}
