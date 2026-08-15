using System;
using Validator.Infrastructure.Csv;

namespace Validator.Infrastructure.Tests.Csv
{
    public class HeaderLayoutTests
    {
        [Fact]
        public void Resolve_HandlesCaseInsensitiveAndReorderedColumns()
        {
            var headers = new[] { "Volume", "Close", "Timestamp", "Open", "High", "Low" };

            var result = HeaderLayoutResolver.Resolve(headers, "timestamp", "open", "high", "low", "close", "volume");

            Assert.Equal(2, result["timestamp"]);
            Assert.Equal(3, result["open"]);
            Assert.Equal(4, result["high"]);
            Assert.Equal(5, result["low"]);
            Assert.Equal(1, result["close"]);
            Assert.Equal(0, result["volume"]);
        }

        [Fact]
        public void Resolve_Throws_When_RequiredHeaderIsMissing()
        {
            var headers = new[] { "Open", "High", "Low", "Close" };

            Assert.Throws<InvalidOperationException>(() => HeaderLayoutResolver.Resolve(headers, "timestamp"));
        }
    }
}
