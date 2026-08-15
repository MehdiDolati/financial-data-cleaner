using Xunit;
using Validator.Application.Abstractions;

namespace Validator.Infrastructure.Tests.Reporting
{
    public class TextReportWriterTests
    {
        [Fact]
        public void ReportWriteOptions_ForTextFormat_AreConfigured()
        {
            var options = new ReportWriteOptions(ReportFormat.Text, "report.txt", true);

            Assert.Equal(ReportFormat.Text, options.Format);
            Assert.Equal("report.txt", options.OutputPath);
            Assert.True(options.Verbose);
        }
    }
}