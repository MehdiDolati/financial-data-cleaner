using Validator.Domain.Findings;
using Validator.Infrastructure.Findings;
using Validator.Infrastructure.Sorting;
using Validator.Infrastructure.Tests.Fixtures;

namespace Validator.Infrastructure.Tests.Sorting;

public sealed class BoundedMemoryTests
{
    [Fact]
    public async Task PrepareAsync_LargeUnsortedFixture_IsReplayableAndBoundedByConfiguration()
    {
        using var fixture = LargeFixtureGenerator.Create(rowCount: 20_000);
        var tempRoot = CreateTempRoot();
        using var storage = new TempStorage(tempRoot);
        var sorter = new ExternalMergeSort(storage);

        await using var replay = await sorter.PrepareAsync(
            fixture.ReadCandles(),
            chunkSize: 127,
            mergeFanIn: 4);

        var first = await ReadTimestampsAsync(replay);
        var second = await ReadTimestampsAsync(replay);

        Assert.Equal(fixture.RowCount, first.Count);
        Assert.Equal(first, second);
        Assert.Equal(fixture.ExpectedTimestamp(0), first[0]);
        Assert.Equal(fixture.ExpectedTimestamp(fixture.RowCount - 1), first[^1]);
        Assert.True(sorter.LastRunStatistics.MaxChunkRecords <= 127);
        Assert.True(sorter.LastRunStatistics.MaxMergeCursors <= 4);
        Assert.Single(Directory.EnumerateFiles(tempRoot));

        await replay.DisposeAsync();
        Assert.Empty(Directory.EnumerateFiles(tempRoot));
    }

    [Fact]
    public async Task PrepareAsync_SourceFailure_DeletesEveryRunArtifact()
    {
        var tempRoot = CreateTempRoot();
        using var storage = new TempStorage(tempRoot);
        var sorter = new ExternalMergeSort(storage);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => sorter.PrepareAsync(FailingSource(), chunkSize: 2, mergeFanIn: 2));

        Assert.Empty(Directory.EnumerateFiles(tempRoot));
    }

    [Fact]
    public async Task PrepareAsync_Cancellation_DeletesEveryRunArtifact()
    {
        var tempRoot = CreateTempRoot();
        using var storage = new TempStorage(tempRoot);
        var sorter = new ExternalMergeSort(storage);
        using var fixture = LargeFixtureGenerator.Create(10);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sorter.PrepareAsync(
                fixture.ReadCandles(),
                chunkSize: 2,
                mergeFanIn: 2,
                cancellation.Token));

        Assert.Empty(Directory.EnumerateFiles(tempRoot));
    }

    [Fact]
    public async Task FindingStore_Dispose_DeletesSpoolArtifact()
    {
        var tempRoot = CreateTempRoot();
        using var storage = new TempStorage(tempRoot);
        var store = new SpoolingFindingStore(storage);
        await store.AppendAsync(new ValidationFinding(FindingCategory.Major, 1, true, "finding"));
        Assert.Single(Directory.EnumerateFiles(tempRoot));

        store.Dispose();

        Assert.Empty(Directory.EnumerateFiles(tempRoot));
    }

    private static async Task<List<DateTimeOffset>> ReadTimestampsAsync(
        Validator.Application.Abstractions.IReplayableCandleData replay)
    {
        var timestamps = new List<DateTimeOffset>();
        await foreach (var candle in replay.ReplayAsync())
        {
            timestamps.Add(candle.Timestamp);
        }

        return timestamps;
    }

    private static IEnumerable<Validator.Domain.Candles.PriceCandle> FailingSource()
    {
        yield return Candle(2);
        yield return Candle(1);
        yield return Candle(0);
        throw new InvalidDataException("Synthetic source failure.");
    }

    private static Validator.Domain.Candles.PriceCandle Candle(int minute) =>
        new(
            new DateTimeOffset(2026, 1, 1, 0, minute, 0, TimeSpan.Zero),
            1m,
            2m,
            0.5m,
            1.5m,
            10m);

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"validator-bounded-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}