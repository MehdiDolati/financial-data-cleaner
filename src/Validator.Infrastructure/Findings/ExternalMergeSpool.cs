using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Infrastructure.Sorting;

namespace Validator.Infrastructure.Findings
{
    // Bounded external-merge sorter for normalized spool lines. Lines are keyed
    // by the finding reference prefix before the first '|', so one canonical
    // run groups every finding's children contiguously while preserving append
    // order within a reference. Runs are written in fixed-size chunks and
    // merged with a bounded fan-in, so memory stays within the configured
    // buffer even for one very large duplicate group or gap.
    public sealed class ExternalMergeSpool : ISpoolCanonicalSorter
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly ITempStorage _tempStorage;
        private readonly int _chunkSize;
        private readonly int _mergeFanIn;

        public ExternalMergeSpool(ITempStorage tempStorage, int chunkSize = 10_000, int mergeFanIn = 16)
        {
            _tempStorage = tempStorage ?? throw new ArgumentNullException(nameof(tempStorage));

            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }

            if (mergeFanIn < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(mergeFanIn));
            }

            _chunkSize = chunkSize;
            _mergeFanIn = mergeFanIn;
        }

        public ExternalSortRunStatistics LastRunStatistics { get; private set; } =
            new(0, 0, 0);

        public async Task<ISpoolReplayableRun> PrepareAsync(
            ISpoolReader source,
            CancellationToken cancellationToken = default)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var ownedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var levels = new List<List<string>>();
            var chunk = new List<LineRecord>(_chunkSize);
            var sequence = 0L;
            var maxChunkRecords = 0;
            var maxMergeCursors = 0;
            var initialRunCount = 0;

            try
            {
                await foreach (var line in source.ReadLinesAsync(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    chunk.Add(new LineRecord(line, sequence++));
                    maxChunkRecords = Math.Max(maxChunkRecords, chunk.Count);

                    if (chunk.Count == _chunkSize)
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
                    foreach (var group in activeRuns.Chunk(_mergeFanIn))
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
                return new PreparedSpoolRun(replayPath, _tempStorage);

                void AddRunAtLevel(string runPath, int levelIndex)
                {
                    while (levels.Count <= levelIndex)
                    {
                        levels.Add([]);
                    }

                    levels[levelIndex].Add(runPath);
                    if (levels[levelIndex].Count < _mergeFanIn)
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

        private string WriteSortedRun(
            IReadOnlyCollection<LineRecord> records,
            CancellationToken cancellationToken)
        {
            var path = _tempStorage.CreateTempFile("spool-sort", ".bin");
            try
            {
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                using var writer = new BinaryWriter(stream, Utf8NoBom);
                foreach (var record in records.OrderBy(record => record.Reference, StringComparer.Ordinal).ThenBy(record => record.Sequence))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var bytes = Encoding.UTF8.GetBytes(record.Line);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                    writer.Write(record.Sequence);
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
            var outputPath = _tempStorage.CreateTempFile("spool-merge", ".bin");
            var cursors = new List<RunCursor>(inputPaths.Count);
            try
            {
                using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var writer = new BinaryWriter(output, Utf8NoBom);
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
                    var bytes = Encoding.UTF8.GetBytes(cursor.Current.Line);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                    writer.Write(cursor.Current.Sequence);
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

        private static string ReferenceOf(string line)
        {
            var separator = line.IndexOf('|');
            return separator >= 0 ? line.Substring(0, separator) : line;
        }

        private readonly record struct LineRecord(string Line, long Sequence)
        {
            public string Reference => ReferenceOf(Line);
        }

        private readonly record struct RunPriority(string Reference, long Sequence, int RunIndex);

        private sealed class RunPriorityComparer : IComparer<RunPriority>
        {
            public static RunPriorityComparer Instance { get; } = new();

            public int Compare(RunPriority x, RunPriority y)
            {
                var reference = string.CompareOrdinal(x.Reference, y.Reference);
                if (reference != 0)
                {
                    return reference;
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
                _reader = new BinaryReader(_stream, Utf8NoBom);
                _runIndex = runIndex;
            }

            public LineRecord Current { get; private set; }

            public RunPriority Priority => new(Current.Reference, Current.Sequence, _runIndex);

            public bool MoveNext()
            {
                if (_reader.BaseStream.Position >= _reader.BaseStream.Length)
                {
                    return false;
                }

                var length = _reader.ReadInt32();
                var bytes = _reader.ReadBytes(length);
                var line = Encoding.UTF8.GetString(bytes);
                var sequence = _reader.ReadInt64();
                Current = new LineRecord(line, sequence);
                return true;
            }

            public void Dispose()
            {
                _reader.Dispose();
                _stream.Dispose();
            }
        }

        private sealed class PreparedSpoolRun : ISpoolReplayableRun
        {
            private readonly string _path;
            private readonly ITempStorage _tempStorage;
            private bool _disposed;

            internal PreparedSpoolRun(string path, ITempStorage tempStorage)
            {
                _path = path;
                _tempStorage = tempStorage;
            }

            public IAsyncEnumerable<string> ReplayAsync(CancellationToken cancellationToken = default)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return ReadAsync(cancellationToken);
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

            private async IAsyncEnumerable<string> ReadAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BinaryReader(stream, Utf8NoBom);
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var length = reader.ReadInt32();
                    var bytes = reader.ReadBytes(length);
                    yield return Encoding.UTF8.GetString(bytes);
                    reader.ReadInt64();
                }
            }
        }
    }
}