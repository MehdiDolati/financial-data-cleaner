using Xunit;
using Validator.Domain.Findings;

namespace Validator.Domain.Tests.Findings
{
    public class FindingCategoryTests
    {
        [Fact]
        public void Enum_Order_IsSeverityAscending()
        {
            Assert.True((int)FindingCategory.Informational < (int)FindingCategory.Minor);
            Assert.True((int)FindingCategory.Minor < (int)FindingCategory.Major);
            Assert.True((int)FindingCategory.Major < (int)FindingCategory.Critical);
        }
    }
}