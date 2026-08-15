using System;
using Validator.Application.Ingestion;

namespace Validator.Application.Tests.Options
{
    public class CsvOptionValidationTests
    {
        [Fact]
        public void Validate_Rejects_TimestampColumn_Without_Explicit_Format()
        {
            var option = new CsvInputOptions
            {
                HasHeader = true,
                TimestampColumn = "Timestamp"
            };

            Assert.Throws<ArgumentException>(() => option.Validate());
        }

        [Fact]
        public void Validate_Throws_When_Date_Or_Time_Format_IsProvided_Without_Its_Pair()
        {
            var option = new CsvInputOptions
            {
                HasHeader = true,
                DateFormat = "yyyy-MM-dd"
            };

            Assert.Throws<ArgumentException>(() => option.Validate());
        }

        [Fact]
        public void Validate_Allows_Valid_DateTime_And_Offset_Combinations()
        {
            var option = new CsvInputOptions
            {
                HasHeader = true,
                TimestampColumn = "Timestamp",
                TimestampFormat = "yyyy-MM-dd HH:mm:ss",
                TzOffset = TimeSpan.FromHours(2)
            };

            option.Validate();

            Assert.Equal("Timestamp", option.TimestampColumn);
            Assert.Equal(TimeSpan.FromHours(2), option.TzOffset);
        }
    }
}
