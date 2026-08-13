using System;
using Xunit;
using Validator.Application.Reporting;

namespace Validator.Application.Tests.Reporting
{
    public class ValidationReportTests
    {
        [Fact]
        public void ValidationSummary_IsClean_Derived()
        {
            var s = new ValidationSummary(0, 0, 100);
            Assert.True(s.IsClean);

            var s2 = new ValidationSummary(1, 0, 99);
            Assert.False(s2.IsClean);
        }

        [Fact]
        public void ValidationReport_IsClean_Reflects_Summary()
        {
            var s = new ValidationSummary(0, 0, 10);
            var range = new DateRange(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);
            var r = new ValidationReport(s, range, "file.csv");
            Assert.True(r.IsClean);
        }
    }
}
