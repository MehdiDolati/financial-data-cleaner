using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;

namespace Validator.Application.Tests.Reporting;

internal sealed class InMemorySpoolStore
{
    public Dictionary<string, List<string>> Spools { get; } = new();
}

internal sealed class InMemorySpool : ISpoolWriter
{
    private static readonly byte[] NewlineBytes = Encoding.UTF8.GetBytes(Environment.NewLine);

    private readonly InMemorySpoolStore _store;
    private readonly List<string> _lines = new();

    public InMemorySpool(InMemorySpoolStore store)
    {
        _store = store;
        Path = $"memory://spool-{Guid.NewGuid():N}";
        _store.Spools.Add(Path, _lines);
    }

    public string Path { get; }

    public long BytesWritten { get; private set; }

    public ValueTask AppendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _lines.Add(line);
        BytesWritten += Encoding.UTF8.GetByteCount(line) + NewlineBytes.Length;
        return ValueTask.CompletedTask;
    }

    public ValueTask CompleteAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _store.Spools.Remove(Path);
        return ValueTask.CompletedTask;
    }
}

internal sealed class InMemorySpoolReader : ISpoolSeekableReader
{
    private readonly List<(string Line, long Start, long End)> _lines;

    public InMemorySpoolReader(IReadOnlyList<string> lines)
    {
        var newlineBytes = Encoding.UTF8.GetByteCount(Environment.NewLine);
        var offset = 0L;
        _lines = lines
            .Select(line =>
            {
                var length = Encoding.UTF8.GetByteCount(line) + newlineBytes;
                var entry = (Line: line, Start: offset, End: offset + length);
                offset += length;
                return entry;
            })
            .ToList();
    }

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var entry in _lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry.Line;
        }

        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> ReadRangeAsync(
        long startByte,
        long endByte,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var entry in _lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Start >= endByte)
            {
                yield break;
            }

            if (entry.End > startByte)
            {
                yield return entry.Line;
            }
        }

        await Task.CompletedTask;
    }
}