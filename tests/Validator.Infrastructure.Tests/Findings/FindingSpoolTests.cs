using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Validator.Domain.Findings;
using Validator.Infrastructure.Findings;
using Validator.Infrastructure.Sorting;

namespace Validator.Infrastructure.Tests.Findings
{
    public class FindingSpoolTests
    {
        [Fact]
        public async Task AppendAsync_AndReadAllAsync_RoundTripsFindingRecords()
        {
            using var tempStorage = new TempStorage();
            using var store = new SpoolingFindingStore(tempStorage);

            var first = new ValidationFinding(FindingCategory.Major, 1, true, "first issue");
            var second = new ValidationFinding(FindingCategory.Critical, 2, false, "second issue");

            await store.AppendAsync(first);
            await store.AppendAsync(second);

            var results = new List<ValidationFinding>();
            await foreach (var finding in store.ReadAllAsync())
            {
                results.Add(finding);
            }

            Assert.Equal(2, results.Count);
            Assert.Equal(first.Message, results[0].Message);
            Assert.Equal(second.Message, results[1].Message);
        }
    }
}
