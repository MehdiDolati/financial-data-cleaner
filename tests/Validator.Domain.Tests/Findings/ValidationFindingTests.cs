using Xunit;
using Validator.Domain.Findings;

namespace Validator.Domain.Tests.Findings
{
    public class ValidationFindingTests
    {
        [Fact]
        public void ValidationFinding_Properties_AreSet()
        {
            var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var f = new ValidationFinding(FindingCategory.Major, 2, true, "Test")
            {
                Timestamp = timestamp,
                Line = 9,
                SourceLines = [9, 10]
            };
            Assert.Equal(FindingCategory.Major, f.Category);
            Assert.Equal(2, f.CountContribution);
            Assert.True(f.StableSequence);
            Assert.Equal("Test", f.Message);
            Assert.Equal(timestamp, f.Timestamp);
            Assert.Equal(9, f.Line);
            Assert.Equal(new long[] { 9, 10 }, f.SourceLines);
        }

        [Theory]
        [InlineData(0, "valid")]
        [InlineData(-1, "valid")]
        [InlineData(1, "")]
        [InlineData(1, "   ")]
        public void ValidationFinding_RejectsInvalidContributionOrMessage(int count, string message)
        {
            Assert.ThrowsAny<ArgumentException>(() =>
                new ValidationFinding(FindingCategory.Major, count, true, message));
        }

        [Fact]
        public void MalformedRow_Properties_AreSet()
        {
            var r = new MalformedRow(42, "raw,line", "bad format");
            Assert.Equal(42, r.LineNumber);
            Assert.Equal("raw,line", r.RawText);
            Assert.Equal("bad format", r.Reason);
        }

        [Fact]
        public void MalformedRow_AllowsUtcTimestampAndNormalizesNullStrings()
        {
            var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var row = new MalformedRow(1, null!, null!, timestamp);

            Assert.Equal(string.Empty, row.RawText);
            Assert.Equal(string.Empty, row.Reason);
            Assert.Equal(timestamp, row.ParsedTimestampUtc);
        }

        [Fact]
        public void MalformedRow_RejectsInvalidLineAndNonUtcTimestamp()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MalformedRow(0, "", "bad"));
            Assert.Throws<ArgumentException>(() => new MalformedRow(
                1,
                "",
                "bad",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1))));
        }
    }
}