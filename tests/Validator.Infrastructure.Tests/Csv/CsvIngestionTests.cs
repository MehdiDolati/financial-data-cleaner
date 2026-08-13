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
                TzOffset = TimeSpan.FromHours(2)
            };

            options.Validate();

            Assert.True(options.HasHeader);
            Assert.Equal(";", options.Delimiter);
            Assert.Equal("Timestamp", options.TimestampColumn);
            Assert.Equal(TimeSpan.FromHours(2), options.TzOffset);
        }
    }
}