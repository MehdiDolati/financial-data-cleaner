using System;
using Xunit;
using Validator.Application.Ingestion;

namespace Validator.Infrastructure.Tests.Csv
{
    public class CsvIngestionTests
    {
        [Fact]
        public void CsvInputOptions_AllowHeaderAndTimezoneOverrides()
        {
            var options = new CsvInputOptions
            {
                HasHeader = true,
                Delimiter = ";",
                TimestampColumn = "Timestamp",
                TimestampFormat = "yyyy-MM-dd HH:mm:ss",
                TzOffset = TimeSpan.FromHours(2)
            };

            options.Validate();

            Assert.True(options.HasHeader);
            Assert.Equal(";", options.Delimiter);
            Assert.Equal("Timestamp", options.TimestampColumn);
            Assert.Equal("yyyy-MM-dd HH:mm:ss", options.TimestampFormat);
            Assert.Equal(TimeSpan.FromHours(2), options.TzOffset);
        }
    }
}