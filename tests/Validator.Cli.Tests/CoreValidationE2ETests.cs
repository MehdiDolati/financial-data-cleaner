using Xunit;
using Validator.Application.Abstractions;

namespace Validator.Cli.Tests
{
    public class CoreValidationE2ETests
    {
        [Fact]
        public void ReportFormat_Enum_ContainsTextAndJson()
        {
            Assert.Equal(2, Enum.GetValues<ReportFormat>().Length);
            Assert.Contains(ReportFormat.Text, Enum.GetValues<ReportFormat>());
            Assert.Contains(ReportFormat.Json, Enum.GetValues<ReportFormat>());
        }
    }
}