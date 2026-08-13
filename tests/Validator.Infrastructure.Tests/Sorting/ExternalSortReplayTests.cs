using System;
using System.Linq;
using System.Threading.Tasks;
using Validator.Domain.Candles;
using Validator.Infrastructure.Sorting;

namespace Validator.Infrastructure.Tests.Sorting
{
    public class ExternalSortReplayTests
    {
        [Fact]
        public async Task SortAsync_OrdersCandlesAcrossMultipleChunks()
        {
            using var tempStorage = new TempStorage();
            var sorter = new ExternalMergeSort(tempStorage);

            var source = new[]
            {
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 3, 0, 0, TimeSpan.Zero), 1m, 1.5m, 0.8m, 1.2m, 100m),
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero), 1m, 1.6m, 0.9m, 1.4m, 150m),
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero), 1m, 1.7m, 0.7m, 1.5m, 120m),
                new PriceCandle(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 1m, 1.8m, 0.6m, 1.3m, 180m)
            };

            var ordered = await sorter.SortAsync(source, chunkSize: 2);

            Assert.Equal(4, ordered.Count);
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), ordered[0].Timestamp);
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero), ordered[1].Timestamp);
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero), ordered[2].Timestamp);
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 3, 0, 0, TimeSpan.Zero), ordered[3].Timestamp);
            Assert.Equal(source.Max(c => c.Volume), ordered.Max(c => c.Volume));
        }
    }
}