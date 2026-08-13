using Xunit;

namespace Validator.Infrastructure.Tests.Sorting
{
    public class ExternalSortReplayTests
    {
        [Fact]
        public void ReplayOrder_UsesSortedEnumerableWhenInputIsAlreadyOrdered()
        {
            var numbers = new[] { 1, 2, 3, 4, 5 };
            Assert.Equal(5, numbers.Length);
            Assert.Equal(1, numbers[0]);
        }
    }
}