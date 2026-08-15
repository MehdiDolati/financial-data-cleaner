using Validator.Application.Abstractions;
using Validator.Domain.Candles;

namespace Validator.Infrastructure.Sorting;

public sealed class ExternalMergeSort
{
    private readonly ITempStorage _tempStorage;

    public ExternalMergeSort(ITempStorage tempStorage)
    {
        _tempStorage = tempStorage ?? throw new ArgumentNullException(nameof(tempStorage));
    }

    public ExternalSortRunStatistics LastRunStatistics { get; private set; } =
        new(0, 0, 0);

    public async Task<ReplayableSortedCandleData> PrepareAsync(
        IEnumerable<PriceCandle> source,
        int chunkSize = 10_000,
        int mergeFanIn = 16,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }

        if (mergeFanIn < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(mergeFanIn));
        }

        await Task.CompletedTask.ConfigureAwait(false);

        var ownedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var levels = new List<List<string>>();
        var chunk = new List<SortRecord>(chunkSize);
        var sequence = 0L;
        var maxChunkRecords = 0;
        var maxMergeCursors = 0;
        var initialRunCount = 0;

        try
        {
            foreach (var candle in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                chunk.Add(new SortRecord(candle, sequence++));
                maxChunkRecords = Math.Max(maxChunkRecords, chunk.Count);

                if (chunk.Count == chunkSize)
                {
                    var run = WriteSortedRun(chunk, cancellationToken);
                    ownedPaths.Add(run);
                    initialRunCount++;
                    AddRunAtLevel(run, 0);
                    chunk.Clear();
                }
            }

            if (chunk.Count > 0)
            {
                var run = WriteSortedRun(chunk, cancellationToken);
                ownedPaths.Add(run);
                initialRunCount++;
                AddRunAtLevel(run, 0);
            }

            var activeRuns = levels.SelectMany(level => level).ToList();
            if (activeRuns.Count == 0)
            {
                var emptyRun = WriteSortedRun([], cancellationToken);
                ownedPaths.Add(emptyRun);
                activeRuns.Add(emptyRun);
            }

            while (activeRuns.Count > 1)
            {
                var nextRuns = new List<string>();
                foreach (var group in activeRuns.Chunk(mergeFanIn))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    nextRuns.Add(group.Length == 1 ? group[0] : MergeOwned(group));
                }

                activeRuns = nextRuns;
            }

            var replayPath = activeRuns[0];
            ownedPaths.Remove(replayPath);
            LastRunStatistics = new ExternalSortRunStatistics(
                maxChunkRecords,
                maxMergeCursors,
                initialRunCount);
            return new ReplayableSortedCandleData(replayPath, _tempStorage);

            void AddRunAtLevel(string runPath, int levelIndex)
            {
                while (levels.Count <= levelIndex)
                {
                    levels.Add([]);
                }

                levels[levelIndex].Add(runPath);
                if (levels[levelIndex].Count < mergeFanIn)
                {
                    return;
                }

                var group = levels[levelIndex].ToArray();
                levels[levelIndex].Clear();
                AddRunAtLevel(MergeOwned(group), levelIndex + 1);
            }

            string MergeOwned(IReadOnlyList<string> inputPaths)
            {
                var outputPath = MergeRuns(
                    inputPaths,
                    cancellationToken,
                    cursorCount => maxMergeCursors = Math.Max(maxMergeCursors, cursorCount));
                ownedPaths.Add(outputPath);

                foreach (var inputPath in inputPaths)
                {
                    _tempStorage.DeleteIfExists(inputPath);
                    ownedPaths.Remove(inputPath);
                }

                return outputPath;
            }
        }
        catch
        {
            foreach (var path in ownedPaths)
            {
                TryDelete(path);
            }

            throw;
        }
    }

    public async Task<List<PriceCandle>> SortAsync(
        IEnumerable<PriceCandle> source,
        int chunkSize = 10_000)
    {
        await using var replay = await PrepareAsync(source, chunkSize).ConfigureAwait(false);
        var result = new List<PriceCandle>();
        await foreach (var candle in replay.ReplayAsync())
        {
            result.Add(candle);
        }

        return result;
    }

    private string WriteSortedRun(
        IReadOnlyCollection<SortRecord> records,
        CancellationToken cancellationToken)
    {
        var path = _tempStorage.CreateTempFile("candle-sort", ".bin");
        try
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream);
            foreach (var record in records
                         .OrderBy(item => item.Candle.Timestamp)
                         .ThenBy(item => item.Sequence))
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteRecord(writer, record);
            }

            return path;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private string MergeRuns(
        IReadOnlyList<string> inputPaths,
        CancellationToken cancellationToken,
        Action<int> observeCursorCount)
    {
        var outputPath = _tempStorage.CreateTempFile("candle-merge", ".bin");
        var cursors = new List<RunCursor>(inputPaths.Count);
        try
        {
            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(output);
            var queue = new PriorityQueue<RunCursor, RunPriority>(RunPriorityComparer.Instance);

            for (var index = 0; index < inputPaths.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cursor = new RunCursor(inputPaths[index], index);
                cursors.Add(cursor);
                if (cursor.MoveNext())
                {
                    queue.Enqueue(cursor, cursor.Priority);
                }
            }

            observeCursorCount(cursors.Count);

            while (queue.TryDequeue(out var cursor, out _))
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteRecord(writer, cursor.Current);
                if (cursor.MoveNext())
                {
                    queue.Enqueue(cursor, cursor.Priority);
                }
            }

            return outputPath;
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }
        finally
        {
            foreach (var cursor in cursors)
            {
                cursor.Dispose();
            }
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            _tempStorage.DeleteIfExists(path);
        }
        catch (IOException)
        {
            // Preserve the primary processing failure; TempStorage disposal retries cleanup.
        }
    }

    private static void WriteRecord(BinaryWriter writer, SortRecord record)
    {
        writer.Write(record.Candle.Timestamp.UtcTicks);
        writer.Write(record.Sequence);
        writer.Write(record.Candle.SourceLine);
        writer.Write(record.Candle.Open);
        writer.Write(record.Candle.High);
        writer.Write(record.Candle.Low);
        writer.Write(record.Candle.Close);
        writer.Write(record.Candle.Volume);
    }

    private static bool TryReadRecord(BinaryReader reader, out SortRecord record)
    {
        if (reader.BaseStream.Position >= reader.BaseStream.Length)
        {
            record = default;
            return false;
        }

        var timestamp = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
        var sequence = reader.ReadInt64();
        var sourceLine = reader.ReadInt64();
        record = new SortRecord(
            new PriceCandle(
                timestamp,
                reader.ReadDecimal(),
                reader.ReadDecimal(),
                reader.ReadDecimal(),
                reader.ReadDecimal(),
                reader.ReadDecimal(),
                sourceLine),
            sequence);
        return true;
    }

    private readonly record struct SortRecord(PriceCandle Candle, long Sequence);

    private readonly record struct RunPriority(long UtcTicks, long Sequence, int RunIndex);

    private sealed class RunPriorityComparer : IComparer<RunPriority>
    {
        public static RunPriorityComparer Instance { get; } = new();

        public int Compare(RunPriority x, RunPriority y)
        {
            var timestamp = x.UtcTicks.CompareTo(y.UtcTicks);
            if (timestamp != 0)
            {
                return timestamp;
            }

            var sequence = x.Sequence.CompareTo(y.Sequence);
            return sequence != 0 ? sequence : x.RunIndex.CompareTo(y.RunIndex);
        }
    }

    private sealed class RunCursor : IDisposable
    {
        private readonly FileStream _stream;
        private readonly BinaryReader _reader;
        private readonly int _runIndex;

        public RunCursor(string path, int runIndex)
        {
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _reader = new BinaryReader(_stream);
            _runIndex = runIndex;
        }

        public SortRecord Current { get; private set; }

        public RunPriority Priority =>
            new(Current.Candle.Timestamp.UtcTicks, Current.Sequence, _runIndex);

        public bool MoveNext()
        {
            if (!TryReadRecord(_reader, out var record))
            {
                return false;
            }

            Current = record;
            return true;
        }

        public void Dispose()
        {
            _reader.Dispose();
            _stream.Dispose();
        }
    }

    public sealed class ReplayableSortedCandleData : IReplayableCandleData, IAsyncDisposable
    {
        private readonly string _path;
        private readonly ITempStorage _tempStorage;
        private bool _disposed;

        internal ReplayableSortedCandleData(string path, ITempStorage tempStorage)
        {
            _path = path;
            _tempStorage = tempStorage;
        }

        public IAsyncEnumerable<PriceCandle> ReplayAsync()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ReadAsync();
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _tempStorage.DeleteIfExists(_path);
                _disposed = true;
            }

            return ValueTask.CompletedTask;
        }

        private async IAsyncEnumerable<PriceCandle> ReadAsync()
        {
            await Task.Yield();
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            while (TryReadRecord(reader, out var record))
            {
                yield return record.Candle;
            }
        }
    }
}

public sealed record ExternalSortRunStatistics(
    int MaxChunkRecords,
    int MaxMergeCursors,
    int InitialRunCount);
