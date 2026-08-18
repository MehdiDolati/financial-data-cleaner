using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Infrastructure.Findings;
using Validator.Infrastructure.Sorting;

namespace Validator.Infrastructure.Tests.Findings
{
    public class ExternalMergeSpoolTests
    {
        private static async Task<(SpoolWriter Writer, List<string> Lines)> BuildSpoolAsync(
            TempStorage tempStorage,
            params string[] lines)
        {
            var writer = new SpoolWriter(tempStorage);
            foreach (var line in lines)
            {
                await writer.AppendLineAsync(line);
            }

            await writer.CompleteAsync();
            return (writer, lines.ToList());
        }

        private static ISpoolReader ReaderFor(string path) => new SpoolReader(path, path + ".complete");

        [Fact]
        public async Task PrepareAsync_OrdersLinesByReferenceAcrossChunks()
        {
            using var tempStorage = new TempStorage();
            var (writer, _) = await BuildSpoolAsync(
                tempStorage,
                "missing-candle:20240801T1100000000000Z|11",
                "missing-candle:20240801T1000000000000Z|10",
                "invalid-ohlc:line-5|5",
                "time-gap:20240801T1000000000000Z:20240801T1200000000000Z|12");
            await using (writer)
            {
                var sorter = new ExternalMergeSpool(tempStorage, chunkSize: 2);
                var run = await sorter.PrepareAsync(ReaderFor(writer.Path));

                var ordered = new List<string>();
                await foreach (var line in run.ReplayAsync())
                {
                    ordered.Add(line);
                }

                await run.DisposeAsync();

                Assert.Equal(
                    new[]
                    {
                        "invalid-ohlc:line-5|5",
                        "missing-candle:20240801T1000000000000Z|10",
                        "missing-candle:20240801T1100000000000Z|11",
                        "time-gap:20240801T1000000000000Z:20240801T1200000000000Z|12"
                    },
                    ordered);
            }
        }

        [Fact]
        public async Task PrepareAsync_IsReplayableUntilDisposed()
        {
            using var tempStorage = new TempStorage();
            var (writer, _) = await BuildSpoolAsync(
                tempStorage,
                "b|1",
                "a|1",
                "c|1");
            await using (writer)
            {
                var sorter = new ExternalMergeSpool(tempStorage, chunkSize: 2);
                var run = await sorter.PrepareAsync(ReaderFor(writer.Path));

                var first = new List<string>();
                await foreach (var line in run.ReplayAsync())
                {
                    first.Add(line);
                }

                var second = new List<string>();
                await foreach (var line in run.ReplayAsync())
                {
                    second.Add(line);
                }

                await run.DisposeAsync();
                Assert.Equal(first, second);
                Assert.Equal(new[] { "a|1", "b|1", "c|1" }, second);
            }
        }

        [Fact]
        public async Task PrepareAsync_PreservesAppendOrderWithinOneReference()
        {
            using var tempStorage = new TempStorage();
            var duplicateLines = Enumerable.Range(1, 250).Select(i => $"duplicate-record:20240801T1000000000000Z:line-1|{i}").ToArray();
            var (writer, _) = await BuildSpoolAsync(tempStorage, duplicateLines);
            await using (writer)
            {
                var sorter = new ExternalMergeSpool(tempStorage, chunkSize: 7);
                var run = await sorter.PrepareAsync(ReaderFor(writer.Path));

                var ordered = new List<string>();
                await foreach (var line in run.ReplayAsync())
                {
                    ordered.Add(line);
                }

                await run.DisposeAsync();

                Assert.Equal(250, ordered.Count);
                Assert.Equal(ordered, duplicateLines);
            }
        }

        [Fact]
        public async Task PrepareAsync_RespectsConfiguredChunkBuffer()
        {
            using var tempStorage = new TempStorage();
            var lines = Enumerable.Range(0, 10_000)
                .Select(i => $"finding-{i % 37}|{i}")
                .ToArray();
            var (writer, _) = await BuildSpoolAsync(tempStorage, lines);
            await using (writer)
            {
                var sorter = new ExternalMergeSpool(tempStorage, chunkSize: 250, mergeFanIn: 4);
                var run = await sorter.PrepareAsync(ReaderFor(writer.Path));

                var ordered = new List<string>();
                await foreach (var line in run.ReplayAsync())
                {
                    ordered.Add(line);
                }

                await run.DisposeAsync();

                Assert.Equal(10_000, ordered.Count);
                Assert.Equal(250, sorter.LastRunStatistics.MaxChunkRecords);
                Assert.True(sorter.LastRunStatistics.MaxMergeCursors <= 4);
                Assert.True(sorter.LastRunStatistics.InitialRunCount > 1);
                var references = ordered.Select(line => line.Split('|')[0]).ToArray();
                Assert.Equal(references.OrderBy(reference => reference, StringComparer.Ordinal), references);
            }
        }

        [Fact]
        public async Task PrepareAsync_EmptySource_ProducesEmptyReplay()
        {
            using var tempStorage = new TempStorage();
            var (writer, _) = await BuildSpoolAsync(tempStorage);
            await using (writer)
            {
                var sorter = new ExternalMergeSpool(tempStorage);
                var run = await sorter.PrepareAsync(ReaderFor(writer.Path));

                var ordered = new List<string>();
                await foreach (var line in run.ReplayAsync())
                {
                    ordered.Add(line);
                }

                await run.DisposeAsync();
                Assert.Empty(ordered);
            }
        }

        [Fact]
        public async Task PrepareAsync_DeletesTemporaryArtifactsOnDispose()
        {
            using var tempStorage = new TempStorage();
            var (writer, _) = await BuildSpoolAsync(tempStorage, "b|1", "a|1", "c|1");

            var sorter = new ExternalMergeSpool(tempStorage, chunkSize: 2);
            var run = await sorter.PrepareAsync(ReaderFor(writer.Path));
            await run.DisposeAsync();
            await writer.DisposeAsync();

            var leftovers = System.IO.Directory.EnumerateFiles(tempStorage.RootDirectory).ToList();
            Assert.Empty(leftovers);
        }

        [Fact]
        public void Constructor_RejectsInvalidConfiguration()
        {
            using var tempStorage = new TempStorage();
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExternalMergeSpool(tempStorage, chunkSize: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExternalMergeSpool(tempStorage, mergeFanIn: 1));
        }
    }
}