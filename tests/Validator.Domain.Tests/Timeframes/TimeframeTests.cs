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
    }
}
