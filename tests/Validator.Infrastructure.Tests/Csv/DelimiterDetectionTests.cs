using System;
using Validator.Infrastructure.Csv;

namespace Validator.Infrastructure.Tests.Csv
{
    public class DelimiterDetectionTests
    {
        [Theory]
        [InlineData("open,high,low,close,volume", ",")]
        [InlineData("open;high;low;close;volume", ";")]
        [InlineData("open\thigh\tlow\tclose\tvolume", "\t")]
        public void Detect_ReturnsExpectedDelimiter(string sample, string expected)
        {
            Assert.Equal(expected, DelimiterDetector.Detect(sample).ToString());
        }

        [Fact]
        public void Detect_Throws_When_NoCandidateExists()
        {
            Assert.Throws<InvalidOperationException>(() => DelimiterDetector.Detect("alpha|beta|gamma"));
        }

        [Fact]
        public void Detect_IgnoresSupportedDelimitersInsideQuotedFields()
        {
            Assert.Equal(',', DelimiterDetector.Detect("timestamp,open,\"vendor;note;value\",close"));
        }
    }
}
