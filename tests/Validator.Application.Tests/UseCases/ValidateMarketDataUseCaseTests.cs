using Xunit;
using Validator.Application.Abstractions;

namespace Validator.Application.Tests.UseCases
{
    public class ValidateMarketDataUseCaseTests
    {
        [Fact]
        public void ValidationRequest_StoresRequiredExecutionProperties()
        {
            var request = new ValidationRequest("input.csv", "H1", ReportFormat.Text, "report.txt", true);

            Assert.Equal("input.csv", request.InputPath);
            Assert.Equal("H1", request.Timeframe);
            Assert.Equal(ReportFormat.Text, request.Format);
            Assert.Equal("report.txt", request.OutputPath);
            Assert.True(request.Verbose);
        }
    }
}