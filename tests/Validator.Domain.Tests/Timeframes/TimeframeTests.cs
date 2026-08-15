using System;
using Xunit;
using Validator.Domain.Timeframes;

namespace Validator.Domain.Tests.Timeframes
{
    public class TimeframeTests
    {
        [Theory]
        [InlineData("M1", 'M', 1)]
        [InlineData("h12", 'H', 12)]
        [InlineData("D7", 'D', 7)]
        public void Parse_ValidFormats_ReturnsExpected(string input, char unit, int value)
        {
            var tf = Timeframe.Parse(input);
            Assert.Equal(unit, tf.Unit);
            Assert.Equal(value, tf.Value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("X1")]
        [InlineData("M0")]
        [InlineData("M1.5")]
        [InlineData("M-1")]
        public void Parse_InvalidFormats_Throw(string input)
        {
            Assert.ThrowsAny<Exception>(() => Timeframe.Parse(input));
        }

        [Theory]
        [InlineData("M15", 15)]
        [InlineData("H4", 240)]
        [InlineData("D2", 2880)]
        public void Duration_And_ToString_AreCanonical(string input, double expectedMinutes)
        {
            var timeframe = Timeframe.Parse(input.ToLowerInvariant());

            Assert.Equal(expectedMinutes, timeframe.Duration.TotalMinutes);
            Assert.Equal(input, timeframe.ToString());
        }

        [Fact]
        public void TryParse_ReturnsValueOrFalseWithoutThrowing()
        {
            Assert.True(Timeframe.TryParse("H2", out var valid));
            Assert.Equal("H2", valid!.ToString());

            Assert.False(Timeframe.TryParse("X1", out var invalid));
            Assert.Null(invalid);
        }
    }
}
