using Xunit;
using Validator.Domain.Findings;

namespace Validator.Domain.Tests.Findings
{
    public class ValidationFindingTests
    {
        [Fact]
        public void ValidationFinding_Properties_AreSet()
        {
            var f = new ValidationFinding(FindingCategory.Major, 2, true, "Test");
            Assert.Equal(FindingCategory.Major, f.Category);
            Assert.Equal(2, f.CountContribution);
            Assert.True(f.StableSequence);
            Assert.Equal("Test", f.Message);
        }

        [Fact]
        public void MalformedRow_Properties_AreSet()
        {
            var r = new MalformedRow(42, "raw,line", "bad format");
            Assert.Equal(42, r.LineNumber);
            Assert.Equal("raw,line", r.RawText);
            Assert.Equal("bad format", r.Reason);
        }
    }
}