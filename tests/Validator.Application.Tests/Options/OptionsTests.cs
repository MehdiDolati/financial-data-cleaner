using System;
using Xunit;
using Validator.Application.Ingestion;
using Validator.Application.Validation;

namespace Validator.Application.Tests.Options
{
    public class OptionsTests
    {
        [Fact]
        public void CsvInputOptions_Defaults_AreCorrect()
        {
            var opt = new CsvInputOptions();
            Assert.False(opt.HasHeader);
            Assert.Null(opt.Delimiter);
            Assert.Equal(TimeSpan.FromHours(2), opt.TzOffset);
        }

        [Fact]
        public void CsvInputOptions_Validate_Throws_On_Invalid_TimestampColumn()
        {
            var opt = new CsvInputOptions { HasHeader = false, TimestampColumn = "Timestamp" };
            Assert.Throws<ArgumentException>(() => opt.Validate());
        }

        [Fact]
        public void ValidationOptions_Parse_Valid_Timeframe()
        {
            var v = new ValidationOptions { TimeframeOverride = "M1" };
            var tf = v.GetParsedTimeframe();
            Assert.NotNull(tf);
            Assert.Equal('M', tf!.Unit);
            Assert.Equal(1, tf.Value);
        }
    }
}
